using SwiftRide.Contracts.Events;
using TripService.Application.DTOs;
using TripService.Application.Exceptions;
using TripService.Application.Interfaces;
using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Domain.Exceptions;
using TripService.Application.Options;
using TripService.Application.Sagas;
using SwiftRide.Contracts.Payments;
using ContractPaymentMethod = SwiftRide.Contracts.Payments.PaymentMethod;
using PaymentMethod = TripService.Domain.Enums.PaymentMethod;

namespace TripService.Application.Services;

public class TripAppService(ITripRepository trips,
    IDriverStatusClient driverStatusClient,
    TripWorkflowOptions? workflowOptions = null,
    IPaymentSagaRepository? paymentSagas = null) : ITripDriverWorkflow
{
    // Requested + QuoteRequested commit together; Matching returns the quote asynchronously.
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

        trips.AddOutboxEvent(new QuoteRequested(
            Guid.NewGuid(), trip.Id, null, DateTime.UtcNow, trip.Id,
            pickup.Lat, pickup.Lng, dropoff.Lat, dropoff.Lng));

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

        if (trip.Status != TripStatus.Quoted && !((trip.Status is TripStatus.Requested or TripStatus.FindingDriver) && !request.Accept))
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

            if (paymentMethod == PaymentMethod.Card &&
                (string.IsNullOrWhiteSpace(request.CardToken) || request.CardToken.Length > 100))
                throw new ValidationException("CARD yêu cầu CardToken hợp lệ (tối đa 100 ký tự).");
            if (paymentMethod == PaymentMethod.Wallet && request.CardToken is not null)
                throw new ValidationException("WALLET không nhận CardToken.");

            trip.Confirm(paymentMethod);
            trip.Fare!.CardToken = request.CardToken;

            RequestMatch(trip);
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
            var offer = t.Offers.FirstOrDefault(o => o.DriverId == driverId && o.Status == OfferStatus.Pending);
            if (offer == null) continue;

            result.Add(new DriverOfferDto(
                offer.Id,
                t.Id,
                new LocationDto((double)t.Location.PickupLat, (double)t.Location.PickupLng, t.Location.PickupAddress),
                new LocationDto((double)t.Location.DropoffLat, (double)t.Location.DropoffLng, t.Location.DropoffAddress),
                t.Fare?.Price ?? 0,
                offer.DistanceToPickupKm,
                offer.CreatedAt
            ));
        }
        return result;
    }
    
    public async Task<TripDetailDto?> GetCurrentTripAsync(Guid driverId, CancellationToken cancellationToken)
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
    trips.AddOutboxEvent(new TripAccepted(
        Guid.NewGuid(), trip.Id, null, DateTime.UtcNow, trip.Id, driverId));
    await trips.SaveChangesAsync();

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
            RequestMatch(trip);
        }
        await trips.SaveChangesAsync();
        return new TripDriverActionResponse(trip.Status.ToString());
    }

    private void RequestMatch(Trip trip)
    {
        trip.BeginMatching();
        trips.AddOutboxEvent(new MatchRequested(
            Guid.NewGuid(), trip.Id, null, DateTime.UtcNow, trip.Id, trip.Offers.Count + 1,
            (double)trip.Location.PickupLat, (double)trip.Location.PickupLng,
            trip.Offers.Select(o => o.DriverId).ToArray()));
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

    public async Task<TripDriverActionResponse> MarkDroppedOffAsync(
        Guid tripId,
        Guid driverId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var trip = await GetDriverTripAsync(tripId, driverId, true);

        // A repeated HTTP request must not create another payment event.
        if (trip.Status is TripStatus.PaymentPending or TripStatus.Paid or TripStatus.PaymentFailed)
            return new TripDriverActionResponse(trip.Status.ToString());

        if (trip.Status != TripStatus.PickedUp)
        {
            trip.MarkDroppedOff(); // Domain produces the existing invalid-transition error.
            throw new InvalidOperationException("Unexpected trip state after dropoff.");
        }

        if (trip.Fare?.PaymentMethod is not PaymentMethod method)
            throw new InvalidTripStateException("INVALID_TRIP_STATE: Chuyến đi chưa có thông tin thanh toán.");

        trip.MarkDroppedOff(); // Adds DroppedOff and PaymentPending history entries.
        var paymentMethod = method switch
        {
            PaymentMethod.Wallet => ContractPaymentMethod.WALLET,
            PaymentMethod.Card => ContractPaymentMethod.CARD,
            _ => throw new InvalidTripStateException("INVALID_TRIP_STATE: Phương thức thanh toán không hợp lệ.")
        };
        if (paymentSagas is null)
            throw new InvalidOperationException("Payment Saga repository is not configured.");
        var now = DateTime.UtcNow;
        var deadlineSeconds = (workflowOptions ?? new TripWorkflowOptions()).PaymentSagaDeadlineSeconds;
        if (deadlineSeconds is < 10 or > 3600)
            throw new InvalidOperationException("Payment Saga deadline must be 10-3600 seconds.");
        var deadline = now.AddSeconds(deadlineSeconds);
        var commandId = Guid.NewGuid();
        paymentSagas.Add(PaymentSagaInstance.Start(trip.Id, commandId, deadline, now));
        trips.AddOutboxEvent(new ProcessPayment(
            commandId, trip.Id, null, now,
            trip.Id, trip.RiderId, driverId, trip.Fare.Price, paymentMethod,
            PaymentProtocol.TripPaymentKey(trip.Id), deadline, trip.Fare.CardToken));
        await trips.SaveChangesAsync();
        return new TripDriverActionResponse(trip.Status.ToString());
    }
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
