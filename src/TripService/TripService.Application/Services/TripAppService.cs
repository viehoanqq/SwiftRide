using TripService.Application.DTOs;
using TripService.Application.Exceptions;
using TripService.Application.Interfaces;
using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Domain.Exceptions;

namespace TripService.Application.Services;

public class TripAppService(ITripRepository trips, IMatchingClient matching) : ITripDriverWorkflow
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
            else
            {
                var offer = trip.OfferDriver(driverId, trip.Offers.Count + 1);
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

    // public Task<IReadOnlyList<DriverOfferDto>> GetOffersAsync(
    //     Guid driverId,
    //     CancellationToken cancellationToken)
    //     => throw new NotImplementedException("Driver offer workflow is not implemented yet.");

   public async Task<IReadOnlyList<DriverOfferDto>> GetOffersAsync(
        Guid driverId,
        CancellationToken cancellationToken)
    {
        var tripsWithOffers = await trips.GetTripsWithPendingOffersForDriverAsync(driverId, cancellationToken);
        
        var result = new List<DriverOfferDto>();
        foreach (var t in tripsWithOffers)
        {
            var offer = t.Offers.FirstOrDefault(o => o.DriverId == driverId);
            if (offer == null) continue;

            result.Add(new DriverOfferDto(
                offer.Id,
                t.Id,
                new LocationDto((double)t.Location.PickupLat, (double)t.Location.PickupLng, t.Location.PickupAddress),
                new LocationDto((double)t.Location.DropoffLat, (double)t.Location.DropoffLng, t.Location.DropoffAddress),
                t.Fare?.Price ?? 0,
                0.0, // Khoảng cách giả định tới điểm đón
                offer.CreatedAt
            ));
        }
        return result;
    }
    public Task<TripDetailDto?> GetCurrentTripAsync(
        Guid driverId,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("Driver current-trip workflow is not implemented yet.");

    public Task<TripDriverActionResponse> AcceptOfferAsync(
        Guid tripId,
        Guid driverId,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("Driver offer acceptance is not implemented yet.");

    public Task<TripDriverActionResponse> RejectOfferAsync(
        Guid tripId,
        Guid driverId,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("Driver offer rejection is not implemented yet.");

    public Task<TripDriverActionResponse> MarkEnRouteAsync(
        Guid tripId,
        Guid driverId,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("En-route workflow is not implemented yet.");

    public Task<TripDriverActionResponse> MarkPickedUpAsync(
        Guid tripId,
        Guid driverId,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("Pickup workflow is not implemented yet.");

    public Task<TripDriverActionResponse> MarkDroppedOffAsync(
        Guid tripId,
        Guid driverId,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("Dropoff workflow is not implemented yet.");

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
    public async Task<bool> ProcessInitialMatchingAsync(
        Guid tripId,
        string paymentMethod,
        CancellationToken cancellationToken)
    {
        var trip = await trips.GetByIdAsync(tripId);
        if (trip is null || trip.Status != TripStatus.Quoted) return false;

        if (!Enum.TryParse<PaymentMethod>(paymentMethod, ignoreCase: true, out var parsedPaymentMethod))
        {
            return false;
        }

        trip.Confirm(parsedPaymentMethod);

        var matchedDriver = await matching.MatchAsync(new MatchDriverRequest(
            (double)trip.Location.PickupLat,
            (double)trip.Location.PickupLng));

        if (matchedDriver is null)
        {
            trip.MarkFailed();
            await trips.SaveChangesAsync();
            return false;
        }

        if (Guid.TryParse(matchedDriver.DriverId, out var driverId) && driverId != Guid.Empty)
        {
            var offer = trip.OfferDriver(driverId, trip.Offers.Count + 1);
            trips.AddOffer(offer);
        }

        await trips.SaveChangesAsync();
        return true;
    }

    public async Task CancelTripAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var trip = await trips.GetByIdAsync(tripId);
        if (trip is not null)
        {
            trip.Cancel();
            await trips.SaveChangesAsync();
        }
    }

    public async Task<TripDriverActionResponse> UpdateTripStatusAsync(
        Guid tripId, 
        TripStatus status, 
        CancellationToken cancellationToken)
    {
        var trip = await trips.GetByIdAsync(tripId);
        if (trip is null)
        {
            throw new NotFoundException("TRIP_NOT_FOUND", $"Không tìm thấy chuyến đi với ID {tripId}");
        }

        if (status == TripStatus.EnRoute)
        {
            trip.MarkEnRoute();
        }
        else if (status == TripStatus.PickedUp)
        {
            trip.MarkPickedUp();
        }
        else if (status == TripStatus.DroppedOff)
        {
            trip.MarkDroppedOff();
        }

        await trips.SaveChangesAsync();
        return new TripDriverActionResponse(trip.Status.ToString());   
     }

}
