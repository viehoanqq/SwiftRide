using MassTransit;
using Microsoft.Extensions.Logging;
using SwiftRide.Contracts.Events;
using TripService.Application.Sagas;

namespace TripService.Infrastructure.Messaging;

public sealed class ProcessPaymentTimedOutConsumer(
    IPaymentSagaRepository sagas,
    ILogger<ProcessPaymentTimedOutConsumer> logger) : IConsumer<ProcessPaymentTimedOut>
{
    public async Task Consume(ConsumeContext<ProcessPaymentTimedOut> context)
    {
        var message = context.Message;
        var saga = await sagas.GetByTripIdAsync(message.TripId, context.CancellationToken);
        if (saga is null || saga.CorrelationId != message.CorrelationId ||
            saga.ProcessPaymentEventId != message.ProcessPaymentEventId ||
            saga.PaymentTimeoutEventId != message.EventId ||
            saga.PaymentDeadlineAt != message.DeadlineAt ||
            saga.State != PaymentSagaState.AwaitingPayment)
        {
            logger.LogInformation("Ignoring stale payment timeout {EventId} for trip {TripId}.",
                message.EventId, message.TripId);
            return;
        }

        saga.Apply(PaymentSagaSignal.PaymentOutcomeUnknown, DateTime.UtcNow);
        await sagas.SaveChangesAsync(context.CancellationToken);
        logger.LogWarning("Payment outcome is unknown for trip {TripId}; reconciliation is required.",
            message.TripId);
    }
}
