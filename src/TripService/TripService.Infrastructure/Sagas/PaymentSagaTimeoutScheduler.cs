using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SwiftRide.Contracts.Events;
using TripService.Application.Interfaces;
using TripService.Application.Sagas;
using TripService.Infrastructure.Persistence;

namespace TripService.Infrastructure.Sagas;

public sealed class PaymentSagaTimeoutScheduler(
    IServiceScopeFactory scopes,
    ILogger<PaymentSagaTimeoutScheduler> logger,
    PaymentSagaTimeoutOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<PaymentSagaTimeoutDispatcher>()
                    .DispatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Payment Saga timeout scan failed; retrying."); }

            try { await Task.Delay(options.PollInterval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}

public sealed record PaymentSagaTimeoutOptions(TimeSpan PollInterval, int BatchSize);

public sealed class PaymentSagaTimeoutDispatcher(TripDbContext db, ITripRepository trips,
    PaymentSagaTimeoutOptions options)
{
    public async Task<int> DispatchAsync(CancellationToken cancellationToken)
    {
        // The locks survive until both the Saga marker and its outbox row commit.
        // A second Trip instance skips these rows instead of scheduling duplicates.
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var due = await db.PaymentSagas.FromSqlRaw("""
            SELECT TOP ({0}) * FROM dbo.PaymentSagas WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE State = 'AwaitingPayment'
              AND PaymentTimeoutEventId IS NULL
              AND PaymentDeadlineAt <= SYSUTCDATETIME()
            ORDER BY PaymentDeadlineAt, Id
            """, options.BatchSize).ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var saga in due)
        {
            var eventId = Guid.NewGuid();
            saga.PaymentTimeoutEventId = eventId;
            trips.AddOutboxEvent(new ProcessPaymentTimedOut(
                eventId, saga.CorrelationId, saga.ProcessPaymentEventId, now,
                saga.TripId, saga.ProcessPaymentEventId, saga.PaymentDeadlineAt));
        }

        // Refund timeout is an uncertainty marker, never a definitive refusal.
        var dueRefunds = await db.PaymentSagas.FromSqlRaw("""
            SELECT TOP ({0}) * FROM dbo.PaymentSagas WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE State = 'Compensating'
              AND RefundTimedOutAt IS NULL
              AND RefundDeadlineAt <= SYSUTCDATETIME()
            ORDER BY RefundDeadlineAt, Id
            """, options.BatchSize).ToListAsync(cancellationToken);
        foreach (var saga in dueRefunds) saga.RefundTimedOutAt = now;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return due.Count + dueRefunds.Count;
    }
}
