using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SwiftRide.Contracts.Payments;
using TripService.Application.Interfaces;
using TripService.Application.Sagas;
using TripService.Application.Services;
using TripService.Infrastructure.Persistence;

namespace TripService.Infrastructure.Sagas;

public sealed class PaymentSagaReconciler(IServiceScopeFactory scopes,
    ILogger<PaymentSagaReconciler> logger) : BackgroundService
{
    private int paymentOffset;
    private int refundOffset;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
                var paymentCount = await db.PaymentSagas.CountAsync(
                    s => s.State == PaymentSagaState.PaymentUncertain, stoppingToken);
                if (paymentOffset >= paymentCount) paymentOffset = 0;
                var ids = await db.PaymentSagas.AsNoTracking()
                    .Where(s => s.State == PaymentSagaState.PaymentUncertain)
                    .OrderBy(s => s.Id).Skip(paymentOffset).Select(s => s.TripId).Take(50)
                    .ToListAsync(stoppingToken);
                paymentOffset = paymentOffset + ids.Count >= paymentCount ? 0 : paymentOffset + ids.Count;
                var refundCount = await db.PaymentSagas.CountAsync(
                    s => s.State == PaymentSagaState.Compensating && s.RefundTimedOutAt != null,
                    stoppingToken);
                if (refundOffset >= refundCount) refundOffset = 0;
                var refundIds = await db.PaymentSagas.AsNoTracking()
                    .Where(s => s.State == PaymentSagaState.Compensating &&
                        s.RefundTimedOutAt != null)
                    .OrderBy(s => s.Id).Skip(refundOffset).Select(s => s.TripId).Take(50)
                    .ToListAsync(stoppingToken);
                refundOffset = refundOffset + refundIds.Count >= refundCount ? 0 : refundOffset + refundIds.Count;
                foreach (var id in ids)
                {
                    try { await ReconcileAsync(id, stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Payment Saga reconciliation failed for trip {TripId}; retrying.", id);
                    }
                }
                foreach (var id in refundIds)
                {
                    try { await ReconcileRefundAsync(id, stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Refund Saga reconciliation failed for trip {TripId}; retrying.", id);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Payment Saga reconciliation scan failed; retrying."); }

            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task ReconcileAsync(Guid tripId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var saga = await scope.ServiceProvider.GetRequiredService<IPaymentSagaRepository>()
            .GetByTripIdAsync(tripId, cancellationToken);
        if (saga?.State != PaymentSagaState.PaymentUncertain) return;
        var payment = await scope.ServiceProvider.GetRequiredService<IPaymentClient>()
            .GetByTripAsync(tripId, cancellationToken);
        if (payment is null || payment.Status == PaymentState.PENDING) return;
        if (payment.Status is not (PaymentState.SUCCEEDED or PaymentState.FAILED))
        {
            logger.LogWarning("Payment {PaymentId} for trip {TripId} needs manual review: {Status}.",
                payment.Id, tripId, payment.Status);
            return;
        }
        await scope.ServiceProvider.GetRequiredService<TripPaymentResultHandler>()
            .ReconcileAsync(payment, saga.ProcessPaymentEventId, cancellationToken);
    }

    private async Task ReconcileRefundAsync(Guid tripId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var saga = await scope.ServiceProvider.GetRequiredService<IPaymentSagaRepository>()
            .GetByTripIdAsync(tripId, cancellationToken);
        if (saga?.State != PaymentSagaState.Compensating || saga.RefundTimedOutAt is null)
            return;
        var client = scope.ServiceProvider.GetRequiredService<IPaymentClient>();
        var payment = await client.GetByTripAsync(tripId, cancellationToken);
        if (payment is null || payment.Id != saga.PaymentId ||
            payment.Status != PaymentState.REFUNDED)
            return;
        var receipt = await client.GetRefundByKeyAsync(
            PaymentProtocol.TripRefundKey(tripId), cancellationToken);
        if (receipt?.PaymentId != saga.PaymentId || receipt.Status != PaymentState.REFUNDED ||
            receipt.ReasonCode != saga.RefundReasonCode)
        {
            logger.LogWarning("Refund receipt does not match approved command for trip {TripId}.", tripId);
            return;
        }
        await scope.ServiceProvider.GetRequiredService<TripRefundResultHandler>()
            .ReconcileAsync(saga, payment.Amount, cancellationToken);
    }
}
