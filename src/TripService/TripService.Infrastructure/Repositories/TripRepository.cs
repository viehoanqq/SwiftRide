using Microsoft.EntityFrameworkCore;
using TripService.Application.Interfaces;
using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Infrastructure.Persistence;
using TripService.Application.Exceptions;
using Microsoft.Data.SqlClient;

namespace TripService.Infrastructure.Repositories;

public class TripRepository(TripDbContext db) : ITripRepository
{
    // Các trạng thái coi như chuyến đã kết thúc
    private static readonly TripStatus[] FinishedStatuses =
        [TripStatus.Paid, TripStatus.Cancelled, TripStatus.Failed, TripStatus.PaymentFailed];

    public async Task AddAsync(Trip trip) =>
        await db.Trips.AddAsync(trip);          // chỉ đưa vào context, chưa ghi DB

    public void AddOffer(TripDriverOffer offer) =>
        db.TripDriverOffers.Add(offer);

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
                         t.Status == TripStatus.PickedUp) &&
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
