using TripService.Application.DTOs;
using TripService.Application.Exceptions;
using TripService.Application.Interfaces;
using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Domain.Exceptions;
using TripService.Application.Options;

namespace TripService.Application.Services;

public class TripAppService(ITripRepository trips, IMatchingClient matching,
    IDriverStatusClient driverStatusClient,
    TripWorkflowOptions? workflowOptions = null) : ITripDriverWorkflow
{
    // MINH: tạo chuyến → /quote → Quoted hoặc Failed. 409 nếu đang có chuyến chưa kết thúc
    public async Task<CreateTripResponse> CreateAsync(Guid riderId, CreateTripRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateLocation(request.Pickup, "Điểm đón");
        ValidateLocation(request.Dropoff, "Điểm đến");

        if (await trips.GetActiveByRiderAsync(riderId) is not null)
        {
            throw new ConflictException(
                "ACTIVE_TRIP_EXISTS",
                "Bạn đang có chuyến đi chưa kết thúc.");
        }

        var pickup = request.Pickup;
        var dropoff = request.Dropoff;
        var trip = Trip.Create(
            riderId,
            new TripLocation
            {
                PickupLat = (decimal)pickup.Lat,
                PickupLng = (decimal)pickup.Lng,
                PickupAddress = pickup.Address.Trim(),
                DropoffLat = (decimal)dropoff.Lat,
                DropoffLng = (decimal)dropoff.Lng,
                DropoffAddress = dropoff.Address.Trim()
            });

        var quote = await matching.GetQuoteAsync(
            trip.Id,
            new GeoPointDto(pickup.Lat, pickup.Lng),
            new GeoPointDto(dropoff.Lat, dropoff.Lng));

        if (quote is null)
        {
            trip.MarkFailed();
        }
        else
        {
            trip.MarkQuoted(new TripFare
            {
                QuoteId = quote.QuoteId,
                Price = quote.Price,
                DistanceKm = quote.DistanceKm,
                DurationMin = quote.DurationMin,
                Strategy = quote.Strategy,
                Status = FareStatus.Pending,
                QuotedAt = DateTime.UtcNow
            });
        }

        await trips.AddAsync(trip);
        await trips.SaveChangesAsync();

        return new CreateTripResponse(
            trip.Id,
            trip.Status.ToString(),
            trip.Fare?.Price,
            trip.Fare?.DistanceKm,
            trip.Fare?.DurationMin,
            trip.Fare?.Strategy);
    }

    // MINH: chuyến chưa kết thúc của rider, null nếu không có
    public async Task<TripDetailDto?> GetCurrentAsync(Guid riderId)
    {
        var trip = await trips.GetActiveByRiderAsync(riderId);
        return trip?.ToTripDetailDto();
    }

    // ĐẠT: accept → Fare Accepted + PaymentMethod; từ chối → Cancelled. Chỉ rider của chuyến
    public async Task<ConfirmTripResponse> ConfirmAsync(Guid tripId, Guid riderId, ConfirmTripRequest request)
    {
        var trip = await trips.GetByIdAsync(tripId);
        if (trip is null)
        {
            throw new NotFoundException("TRIP_NOT_FOUND", $"Không tìm thấy chuyến đi với ID {tripId}");
        }

        if (trip.RiderId != riderId)
        {
            throw new ForbiddenException("FORBIDDEN", "Bạn không có quyền thao tác trên chuyến đi này.");
        }

        if (trip.Status != TripStatus.Quoted)
        {
            throw new InvalidTripStateException(
                $"INVALID_TRIP_STATE: Không thể xác nhận hoặc từ chối chuyến đi khi đang ở trạng thái '{trip.Status}'.");
        }

        if (!request.Accept)
        {
            trip.Cancel();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.PaymentMethod))
            {
                throw new ValidationException("Phương thức thanh toán là bắt buộc khi chấp nhận chuyến đi.");
            }

            if (!Enum.TryParse<PaymentMethod>(request.PaymentMethod, ignoreCase: true, out var paymentMethod)
                || !Enum.IsDefined(paymentMethod)
                || !string.Equals(request.PaymentMethod, paymentMethod.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException("Phương thức thanh toán không hợp lệ. Chỉ chấp nhận WALLET hoặc CARD.");
            }

            trip.Confirm(paymentMethod);

            var matchedDriver = await matching.MatchAsync(new MatchDriverRequest(
                (double)trip.Location.PickupLat,
                (double)trip.Location.PickupLng));

            if (matchedDriver is null)
            {
                trip.MarkFailed();
            }
            else if (!Guid.TryParse(matchedDriver.DriverId, out var driverId) || driverId == Guid.Empty)
            {
                throw new InvalidDataException(
                    "Matching service returned a DriverId that is not a valid TripDB user GUID.");
            }
            else if (!double.IsFinite(matchedDriver.DistanceKm) || matchedDriver.DistanceKm < 0)
            {
                throw new InvalidDataException(
                    "Matching service returned an invalid distance to pickup.");
            }
            else
            {
                var offer = trip.OfferDriver(
                    driverId,
                    trip.Offers.Count + 1,
                    matchedDriver.DistanceKm);
                offer.DriverName = matchedDriver.Name;
                trips.AddOffer(offer);
            }
        }

        await trips.SaveChangesAsync();
        return new ConfirmTripResponse(trip.Status.ToString());
    }

    // Rider của chuyến và tài xế đang được offer hoặc đã nhận chuyến mới được xem chi tiết.
    public async Task<TripDetailDto> GetByIdAsync(Guid tripId, Guid userId, string role)
    {
        var trip = await trips.GetByIdAsync(tripId);
        if (trip is null)
        {
            throw new NotFoundException("TRIP_NOT_FOUND", $"Không tìm thấy chuyến đi với ID {tripId}");
        }

        var isRiderOwner = string.Equals(role, "Rider", StringComparison.OrdinalIgnoreCase)
            && trip.RiderId == userId;
        var isAssignedDriver = string.Equals(role, "Driver", StringComparison.OrdinalIgnoreCase)
            && trip.Offers.Any(offer =>
                offer.DriverId == userId &&
                offer.Status is OfferStatus.Pending or OfferStatus.Accepted);

        if (!isRiderOwner && !isAssignedDriver)
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                "Bạn không có quyền xem chi tiết chuyến đi này.");
        }

        return trip.ToTripDetailDto();
    }

    public async Task<IReadOnlyList<DriverOfferDto>> GetOffersAsync(
        Guid driverId,
        CancellationToken cancellationToken)
    {
        var pendingTrips = await trips.GetTripsWithPendingOffersForDriverAsync(
            driverId, cancellationToken);

        return pendingTrips.SelectMany(trip => trip.Offers
                .Where(offer => offer.DriverId == driverId && offer.Status == OfferStatus.Pending)
                .Select(offer => new DriverOfferDto(
                    offer.Id,
                    trip.Id,
                    new LocationDto((double)trip.Location.PickupLat,
                        (double)trip.Location.PickupLng, trip.Location.PickupAddress),
                    new LocationDto((double)trip.Location.DropoffLat,
                        (double)trip.Location.DropoffLng, trip.Location.DropoffAddress),
                    trip.Fare?.Price ?? throw new InvalidOperationException("Offered trip has no fare."),
                    offer.DistanceToPickupKm,
                    new DateTimeOffset(DateTime.SpecifyKind(offer.CreatedAt, DateTimeKind.Utc)))))
            .OrderByDescending(offer => offer.CreatedAt)
            .ThenBy(offer => offer.OfferId)
            .ToList();
    }

    public async Task<TripDetailDto?> GetCurrentTripAsync(
        Guid driverId,
        CancellationToken cancellationToken)
    {
        var trip = await trips.GetActiveByDriverAsync(driverId, cancellationToken);
        return trip?.ToTripDetailDto();
    }

    public async Task<TripDriverActionResponse> AcceptOfferAsync(
    Guid tripId,
    Guid driverId,
    CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();

    var trip = await trips.GetByIdAsync(tripId);

    if (trip is null)
    {
        throw new NotFoundException(
            "TRIP_NOT_FOUND",
            "Không tìm thấy chuyến đi.");
    }

    if (!trip.Offers.Any(offer => offer.DriverId == driverId))
    {
        throw new ForbiddenException(
            "FORBIDDEN",
            "Bạn không được mời nhận chuyến đi này.");
    }

    // Domain kiểm tra trạng thái Trip, đúng offer Pending,
    // cập nhật offer và ghi history.
    trip.AcceptOffer(driverId);

    await trips.SaveChangesAsync();
    await driverStatusClient.SetStatusAsync(driverId, DriverAvailabilityStatus.Busy, cancellationToken);

    return new TripDriverActionResponse(trip.Status.ToString());
}
    public async Task<TripDriverActionResponse> RejectOfferAsync(
        Guid tripId,
        Guid driverId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var trip = await GetDriverTripAsync(tripId, driverId, false);
        trip.RejectOffer(driverId);
        var maxAttempts = workflowOptions?.MaxOfferAttempts ?? 2;
        if (trip.Offers.Count >= maxAttempts)
        {
            trip.MarkFailed();
        }
        else
        {
            var excluded = trip.Offers.Select(o => o.DriverId.ToString("D")).ToList();
            var matched = await matching.MatchAsync(new MatchDriverRequest(
                (double)trip.Location.PickupLat, (double)trip.Location.PickupLng,
                ExcludeDriverIds: excluded));
            if (matched is null) trip.MarkFailed();
            else
            {
                if (!Guid.TryParse(matched.DriverId, out var nextDriver) || nextDriver == Guid.Empty ||
                    trip.Offers.Any(o => o.DriverId == nextDriver) ||
                    !double.IsFinite(matched.DistanceKm) || matched.DistanceKm < 0)
                    throw new DownstreamException(502, "MATCHING_INVALID_RESPONSE", "Matching trả về tài xế không hợp lệ.");
                var offer = trip.OfferDriver(nextDriver, trip.Offers.Count + 1, matched.DistanceKm);
                offer.DriverName = matched.Name;
                trips.AddOffer(offer);
            }
        }
        await trips.SaveChangesAsync();
        return new TripDriverActionResponse(trip.Status.ToString());
    }

    public Task<TripDriverActionResponse> MarkEnRouteAsync(
        Guid tripId,
        Guid driverId,
        CancellationToken cancellationToken)
        => ApplyDriverActionAsync(tripId, driverId, trip => trip.MarkEnRoute(), cancellationToken);

    public Task<TripDriverActionResponse> MarkPickedUpAsync(
        Guid tripId,
        Guid driverId,
        CancellationToken cancellationToken)
        => ApplyDriverActionAsync(tripId, driverId, trip => trip.MarkPickedUp(), cancellationToken);

    public Task<TripDriverActionResponse> MarkDroppedOffAsync(
        Guid tripId,
        Guid driverId,
        CancellationToken cancellationToken)
        => ApplyDriverActionAsync(tripId, driverId, trip => trip.MarkDroppedOff(), cancellationToken,
            DriverAvailabilityStatus.Available);

    private async Task<TripDriverActionResponse> ApplyDriverActionAsync(
        Guid tripId, Guid driverId, Action<Trip> action, CancellationToken cancellationToken,
        DriverAvailabilityStatus? matchingStatus = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var trip = await GetDriverTripAsync(tripId, driverId, true);
        action(trip);
        await trips.SaveChangesAsync();
        if (matchingStatus.HasValue)
            await driverStatusClient.SetStatusAsync(driverId, matchingStatus.Value, cancellationToken);
        return new TripDriverActionResponse(trip.Status.ToString());
    }

    private async Task<Trip> GetDriverTripAsync(Guid tripId, Guid driverId, bool requireAccepted)
    {
        var trip = await trips.GetByIdAsync(tripId)
            ?? throw new NotFoundException("TRIP_NOT_FOUND", "Không tìm thấy chuyến đi.");
        if (!trip.Offers.Any(o => o.DriverId == driverId &&
                (!requireAccepted || o.Status == OfferStatus.Accepted)))
            throw new ForbiddenException("FORBIDDEN", "Bạn không có quyền thao tác trên chuyến đi này.");
        return trip;
    }

    private static void ValidateLocation(LocationDto? location, string label)
    {
        if (location is null ||
            !double.IsFinite(location.Lat) ||
            !double.IsFinite(location.Lng) ||
            location.Lat is < -90 or > 90 ||
            location.Lng is < -180 or > 180 ||
            string.IsNullOrWhiteSpace(location.Address))
        {
            throw new ValidationException($"{label} phải có tọa độ và địa chỉ hợp lệ.");
        }
    }

}
