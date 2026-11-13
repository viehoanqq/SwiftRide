using Microsoft.EntityFrameworkCore;
using PaymentService.Application.Interfaces;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Infrastructure.Persistence;

namespace PaymentService.Infrastructure.Repositories;

public sealed class PaymentRepository(PayDbContext db) : IPaymentRepository
{
    public async Task<Account?> GetAccountByOwnerAsync(string ownerId, OwnerType ownerType, CancellationToken ct = default)
        => await db.Accounts.FirstOrDefaultAsync(a => a.OwnerId == ownerId && a.OwnerType == ownerType, ct);

    public async Task<Account> AddAccountAsync(Account account, CancellationToken ct = default)
    {
        await db.Accounts.AddAsync(account, ct);
        return account;
    }

    public Task UpdateAccountAsync(Account account, CancellationToken ct = default)
    {
        db.Accounts.Update(account);
        return Task.CompletedTask;
    }

    public async Task<Payment?> GetPaymentByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
        => await db.Payments
            .Include(p => p.Entries)
            .FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey, ct);

    public async Task<Payment?> GetPaymentByTripIdAsync(Guid tripId, CancellationToken ct = default)
        => await db.Payments
            .Include(p => p.Entries)
            .FirstOrDefaultAsync(p => p.TripId == tripId, ct);

    public async Task<Payment?> GetPaymentByIdAsync(Guid paymentId, CancellationToken ct = default)
        => await db.Payments
            .Include(p => p.Entries)
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct);

    public async Task AddPaymentAsync(Payment payment, CancellationToken ct = default)
        => await db.Payments.AddAsync(payment, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
