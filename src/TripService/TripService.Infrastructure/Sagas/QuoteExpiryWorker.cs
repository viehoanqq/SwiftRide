using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TripService.Application.Options;
using TripService.Infrastructure.Persistence;

namespace TripService.Infrastructure.Sagas;

public sealed class QuoteExpiryWorker(IServiceScopeFactory scopes, TripWorkflowOptions options,
    ILogger<QuoteExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<QuoteExpiryDispatcher>().DispatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Quote expiry scan failed; will retry."); }
            try { await Task.Delay(TimeSpan.FromSeconds(options.QuoteExpiryPollSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}

public sealed class QuoteExpiryDispatcher(TripDbContext db, TripWorkflowOptions options,
    ILogger<QuoteExpiryDispatcher> logger)
{
    public async Task<int> DispatchAsync(CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var due = await db.Trips.FromSqlRaw("""
            SELECT TOP ({0}) t.* FROM dbo.Trips t WITH (UPDLOCK, READPAST, ROWLOCK)
            INNER JOIN dbo.TripFares f ON f.TripId = t.Id
            WHERE t.Status = 'Quoted'
              AND f.QuotedAt <= DATEADD(second, -{1}, SYSUTCDATETIME())
            ORDER BY f.QuotedAt, t.Id
            """, options.QuoteExpiryBatchSize, options.QuoteConfirmationTimeoutSeconds).ToListAsync(ct);
        foreach (var trip in due)
        {
            await db.Entry(trip).Reference(t => t.Fare).LoadAsync(ct);
            trip.Cancel(); // QuotedState rejects fare and records Cancelled history.
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        foreach (var trip in due)
            logger.LogInformation("Quote expired. TripId={TripId} CorrelationId={CorrelationId} Outcome=Cancelled\n      Diễn giải: Chuyến {TripId} hết thời gian xác nhận báo giá và đã tự hủy.",
                trip.Id, trip.Id, trip.Id);
        return due.Count;
    }
}
