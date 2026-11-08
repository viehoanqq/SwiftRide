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

    public Task<Trip?> GetByIdAsync(Guid id) =>
        WithDetails().FirstOrDefaultAsync(t => t.Id == id);

    public Task<Trip?> GetActiveByRiderAsync(Guid riderId) =>
        WithDetails()
            .Where(t => t.RiderId == riderId && !FinishedStatuses.Contains(t.Status))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

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
