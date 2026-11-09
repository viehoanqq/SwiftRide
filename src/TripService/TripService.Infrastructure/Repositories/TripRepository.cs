using Microsoft.EntityFrameworkCore;
using TripService.Application.Interfaces;
using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Infrastructure.Persistence;

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

    public Task SaveChangesAsync() =>
        db.SaveChangesAsync();

    // Trip là aggregate root: luôn load kèm các bảng con, tránh Fare/History bị null khi dùng
    private IQueryable<Trip> WithDetails() =>
        db.Trips
            .Include(t => t.Location)
            .Include(t => t.Fare)
            .Include(t => t.Offers)
                .ThenInclude(offer => offer.Driver)
            .Include(t => t.History.OrderBy(h => h.Id));   // lịch sử theo đúng thứ tự xảy ra
}
