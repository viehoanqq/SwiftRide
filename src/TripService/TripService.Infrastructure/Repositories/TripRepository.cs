using Microsoft.EntityFrameworkCore;
using TripService.Application.Interfaces;
using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Infrastructure.Persistence;
using TripService.Application.Exceptions;
using Microsoft.Data.SqlClient;
using System.Text.Json;
using SwiftRide.Contracts.Events;
using TripService.Infrastructure.Outbox;
using TripService.Application.DTOs;

namespace TripService.Infrastructure.Repositories;

public class TripRepository(TripDbContext db) : ITripRepository
{
    // Các trạng thái coi như chuyến đã kết thúc
    private static readonly TripStatus[] FinishedStatuses =
        [TripStatus.Paid, TripStatus.Cancelled, TripStatus.Failed, TripStatus.PaymentFailed,
            TripStatus.RefundPending, TripStatus.Refunded, TripStatus.RefundNeedsAttention];

    public async Task AddAsync(Trip trip) =>
        await db.Trips.AddAsync(trip);          // chỉ đưa vào context, chưa ghi DB

    public void AddOffer(TripDriverOffer offer) =>
        db.TripDriverOffers.Add(offer);

    public void AddOutboxEvent(IIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.EventId == Guid.Empty || integrationEvent.CorrelationId == Guid.Empty ||
            integrationEvent.OccurredAt.Kind != DateTimeKind.Utc || integrationEvent.SchemaVersion < 1)
            throw new ArgumentException("Integration event metadata is invalid.", nameof(integrationEvent));
        if (integrationEvent is not (TripAccepted or RideCompleted or TripClosed or ProcessPayment or ProcessPaymentTimedOut or RefundPayment))
            throw new ArgumentException("Unsupported Trip outbox event type.", nameof(integrationEvent));

        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = integrationEvent.EventId,
            Type = integrationEvent.GetType().Name,
            Payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
            CorrelationId = integrationEvent.CorrelationId,
            OccurredAt = integrationEvent.OccurredAt
        });
    }

    public async Task<RideCompleted?> GetRideCompletedAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var payload = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Id == eventId && m.Type == nameof(RideCompleted))
            .Select(m => m.Payload).SingleOrDefaultAsync(cancellationToken);
        if (payload is null) return null;
        var ride = JsonSerializer.Deserialize<RideCompleted>(payload);
        return ride?.EventId == eventId ? ride : null;
    }

    public Task<ProcessedPaymentResult?> GetProcessedPaymentResultAsync(Guid eventId, CancellationToken cancellationToken) =>
        db.ProcessedPaymentResults.AsNoTracking()
            .Where(m => m.EventId == eventId)
            .Select(m => new ProcessedPaymentResult(m.EventId, m.TripId, m.EventType, m.PayloadHash))
            .SingleOrDefaultAsync(cancellationToken);

    public void AddProcessedPaymentResult(ProcessedPaymentResult result) =>
        db.ProcessedPaymentResults.Add(new ProcessedPaymentResultMessage
        {
            EventId = result.EventId,
            TripId = result.TripId,
            EventType = result.EventType,
            PayloadHash = result.PayloadHash,
            ProcessedAt = DateTime.UtcNow
        });

    public Task<Trip?> GetByIdAsync(Guid id) =>
        WithDetails().FirstOrDefaultAsync(t => t.Id == id);

    public Task<Trip?> GetActiveByRiderAsync(Guid riderId) =>
        WithDetails()
            .Where(t => t.RiderId == riderId && !FinishedStatuses.Contains(t.Status))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

    public async Task<IReadOnlyList<Trip>>
    GetTripsWithPendingOffersForDriverAsync(
        Guid driverId,
        CancellationToken cancellationToken) =>
    await WithDetails()
        .AsNoTracking()
        .Where(trip =>
            trip.Status == TripStatus.DriverOffered &&
            trip.Offers.Any(offer =>
                offer.DriverId == driverId &&
                offer.Status == OfferStatus.Pending))
        .OrderByDescending(trip => trip.CreatedAt)
        .ThenBy(trip => trip.Id)
        .ToListAsync(cancellationToken);

    public Task<Trip?> GetActiveByDriverAsync(
    Guid driverId,
    CancellationToken cancellationToken) =>
    WithDetails()
        .Where(trip =>
            !FinishedStatuses.Contains(trip.Status) &&
            trip.Offers.Any(offer =>
                offer.DriverId == driverId &&
                offer.Status == OfferStatus.Accepted))
        .OrderByDescending(trip => trip.UpdatedAt)
        .FirstOrDefaultAsync(cancellationToken);

    public async Task SaveChangesAsync()
    {
        db.ChangeTracker.DetectChanges();
        var changed = db.ChangeTracker.Entries<Trip>()
            .Where(e => e.State == EntityState.Modified && e.Property(t => t.Status).IsModified &&
                (e.Entity.Status == TripStatus.DriverAccepted || e.Entity.Status == TripStatus.PaymentPending))
            .Select(e => e.Entity).ToList();
        if (changed.Count == 0)
        {
            await db.SaveChangesAsync();
            return;
        }
        // Serialize driver assignments and commit Trip, offer and history together.
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            // Serialize assignments across different Trip rows for the same driver.
            var assignments = changed.Select(trip => new
            {
                Trip = trip,
                DriverId = trip.Offers.Single(o => o.Status == OfferStatus.Accepted).DriverId
            }).OrderBy(x => x.DriverId).ToList();
            foreach (var assignment in assignments)
            {
                var resource = $"driver-assignment:{assignment.DriverId:D}";
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DECLARE @result int;
                    EXEC @result = sys.sp_getapplock @Resource={resource},
                        @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=5000;
                    IF @result < 0 THROW 51000, 'Driver assignment lock unavailable.', 1;
                    """);
                if (assignment.Trip.Status == TripStatus.DriverAccepted &&
                    await db.Trips.AnyAsync(t => t.Id != assignment.Trip.Id &&
                        (t.Status == TripStatus.DriverAccepted || t.Status == TripStatus.EnRoute ||
                         t.Status == TripStatus.PickedUp || t.Status == TripStatus.DroppedOff ||
                         t.Status == TripStatus.PaymentPending) &&
                        t.Offers.Any(o => o.DriverId == assignment.DriverId && o.Status == OfferStatus.Accepted)))
                    throw new ConflictException("DRIVER_BUSY", "Tài xế đã nhận một chuyến khác.");
            }
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (SqlException ex) when (ex.Number == 51000)
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            throw new ConflictException("DRIVER_BUSY", "Đang có yêu cầu cập nhật tài xế khác. Vui lòng thử lại.");
        }
        catch
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            throw;
        }
    }

    // Trip là aggregate root: luôn load kèm các bảng con, tránh Fare/History bị null khi dùng
    private IQueryable<Trip> WithDetails() =>
        db.Trips
            .Include(t => t.Location)
            .Include(t => t.Fare)
            .Include(t => t.Offers)
            .Include(t => t.History.OrderBy(h => h.Id));   // lịch sử theo đúng thứ tự xảy ra
}
