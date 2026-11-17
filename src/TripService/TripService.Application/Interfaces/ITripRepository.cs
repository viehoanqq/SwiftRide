using TripService.Domain.Entities;
using SwiftRide.Contracts.Events;
using TripService.Application.DTOs;

namespace TripService.Application.Interfaces;

public interface ITripRepository
{
    Task AddAsync(Trip trip);                          // chỉ đưa vào context, CHƯA lưu
    void AddOffer(TripDriverOffer offer);
    void AddOutboxEvent(IIntegrationEvent integrationEvent);
    Task<RideCompleted?> GetRideCompletedAsync(Guid eventId, CancellationToken cancellationToken);
    Task<ProcessedPaymentResult?> GetProcessedPaymentResultAsync(Guid eventId, CancellationToken cancellationToken);
    void AddProcessedPaymentResult(ProcessedPaymentResult result);
    Task<Trip?> GetByIdAsync(Guid id);                 // Include Location, Fare, History, Offers
    Task<Trip?> GetActiveByRiderAsync(Guid riderId);   // chuyến chưa kết thúc: không phải Paid/Cancelled/Failed/PaymentFailed
    Task<IReadOnlyList<Trip>> GetTripsWithPendingOffersForDriverAsync(
        Guid driverId,
        CancellationToken cancellationToken);
    Task<Trip?> GetActiveByDriverAsync(Guid driverId, CancellationToken cancellationToken);
    Task SaveChangesAsync();                           // lưu tất cả thay đổi một lần
}
