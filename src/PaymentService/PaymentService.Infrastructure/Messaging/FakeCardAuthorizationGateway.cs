using Microsoft.EntityFrameworkCore;
using PaymentService.Application.DTOs;
using PaymentService.Application.Exceptions;
using PaymentService.Application.Interfaces;
using PaymentService.Domain.Rules;
using PaymentService.Infrastructure.Persistence;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Infrastructure.Messaging;

// Persistent deterministic simulator. Calls participate in the caller's payment transaction.
public sealed class FakeCardAuthorizationGateway(PayDbContext db) : ICardAuthorizationGateway
{
    public async Task<CardAuthorizationResult> AuthorizeAsync(string token, string idempotencyKey,
        decimal amount, CancellationToken cancellationToken)
    {
        ValidateKey(idempotencyKey);
        MoneyRules.ValidateAmount(amount);
        await LockAsync("fake-card-auth:" + idempotencyKey, cancellationToken);
        var existing = db.FakeCardAuthorizations.Local.FirstOrDefault(a => a.IdempotencyKey == idempotencyKey)
            ?? await db.FakeCardAuthorizations.SingleOrDefaultAsync(
                a => a.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.Token != token || existing.Amount != amount)
                throw new ConflictException(PaymentErrorCodes.IdempotencyConflict,
                    "Card authorization key was used with a different payload.");
            return existing.Result;
        }

        var result = token switch
        {
            FakeCardTokens.Approved => CardAuthorizationResult.Approved,
            FakeCardTokens.Declined => CardAuthorizationResult.Declined,
            FakeCardTokens.Unknown => CardAuthorizationResult.Unknown,
            _ => throw new ValidationException("Unknown test card token.")
        };
        db.FakeCardAuthorizations.Add(new FakeCardAuthorization
        {
            IdempotencyKey = idempotencyKey, Token = token,
            Amount = amount, Result = result, CreatedAt = DateTime.UtcNow
        });
        return result;
    }

    public Task<CardAuthorizationResult?> FindAuthorizationAsync(string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateKey(idempotencyKey);
        return db.FakeCardAuthorizations.AsNoTracking()
            .Where(a => a.IdempotencyKey == idempotencyKey)
            .Select(a => (CardAuthorizationResult?)a.Result)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task RefundAsync(string authorizationKey, string refundKey, decimal amount,
        CancellationToken cancellationToken)
    {
        ValidateKey(authorizationKey);
        ValidateKey(refundKey);
        MoneyRules.ValidateAmount(amount);
        await LockAsync("fake-card-refund:" + authorizationKey, cancellationToken);
        var existing = db.FakeCardRefunds.Local.FirstOrDefault(r => r.IdempotencyKey == refundKey)
            ?? await db.FakeCardRefunds.SingleOrDefaultAsync(
                r => r.IdempotencyKey == refundKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.AuthorizationKey != authorizationKey || existing.Amount != amount)
                throw new ConflictException(PaymentErrorCodes.IdempotencyConflict,
                    "Card refund key was used with a different payload.");
            return;
        }
        if (db.FakeCardRefunds.Local.Any(r => r.AuthorizationKey == authorizationKey) ||
            await db.FakeCardRefunds.AnyAsync(r => r.AuthorizationKey == authorizationKey, cancellationToken))
            throw new ConflictException(PaymentErrorCodes.IdempotencyConflict,
                "Card authorization was already refunded with another key.");

        var authorization = db.FakeCardAuthorizations.Local.FirstOrDefault(a => a.IdempotencyKey == authorizationKey)
            ?? await db.FakeCardAuthorizations.SingleOrDefaultAsync(
                a => a.IdempotencyKey == authorizationKey, cancellationToken);
        if (authorization is null || authorization.Result != CardAuthorizationResult.Approved ||
            authorization.Amount != amount)
            throw new ValidationException("Only an approved card authorization can be refunded for its original amount.");
        db.FakeCardRefunds.Add(new FakeCardRefund
        {
            IdempotencyKey = refundKey, AuthorizationKey = authorizationKey,
            Amount = amount, CreatedAt = DateTime.UtcNow
        });
    }

    private async Task LockAsync(string resource, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Fake card operations require an active transaction.");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({resource}, 0))", cancellationToken);
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
            throw new ValidationException("Card idempotency key is required (1-100 characters).");
    }
}
