using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using PaymentService.Application.Interfaces;
using PaymentService.Application.Services;
using PaymentService.Infrastructure.Persistence;
using SwiftRide.Contracts.Events;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Infrastructure.Messaging;

public sealed class RideCompletedConsumer(
    PaymentCheckoutService checkout,
    IUnitOfWork unitOfWork,
    PayDbContext db) : IConsumer<RideCompleted>
{
    public Task Consume(ConsumeContext<RideCompleted> context)
    {
        var ride = context.Message;
        if (ride.EventId == Guid.Empty || ride.CorrelationId == Guid.Empty ||
            ride.TripId == Guid.Empty || ride.CorrelationId != ride.TripId || ride.SchemaVersion != EventSchema.V1)
            throw new ArgumentException("Invalid RideCompleted metadata.");

        var request = new CreatePaymentRequest(
            ride.TripId, ride.RiderId, ride.DriverId, ride.Amount,
            ride.PaymentMethod, PaymentProtocol.TripPaymentKey(ride.TripId), ride.CardToken);

        return PaymentCommandProcessor.HandleAsync(checkout, unitOfWork, db,
            request, ride.EventId, ride.CorrelationId, context.CancellationToken);
    }
}

internal static class PaymentCommandProcessor
{
    public static Task<int> HandleAsync(PaymentCheckoutService checkout, IUnitOfWork unitOfWork,
        PayDbContext db, CreatePaymentRequest request, Guid messageId, Guid correlationId,
        CancellationToken cancellationToken) => unitOfWork.ExecuteAsync(async ct =>
        {
            // Checkout takes advisory locks for both the payment key and TripId.
            var result = await checkout.CreateInsideTransactionAsync(request, ct);
            if (await db.ProcessedMessages.AnyAsync(m => m.MessageId == messageId, ct))
                return 0;

            if (result.Response.Status == PaymentState.PENDING)
            {
                // Unknown card outcome: commit the pending payment and checkpoint, without a final event.
                db.ProcessedMessages.Add(new ProcessedMessage
                {
                    MessageId = messageId,
                    ProcessedAt = DateTime.UtcNow
                });
                return 0;
            }

            IIntegrationEvent outcome = result.Response.Status switch
            {
                PaymentState.SUCCEEDED or PaymentState.REFUNDED => new PaymentSettled(
                    Guid.NewGuid(), correlationId, messageId, DateTime.UtcNow,
                    request.TripId, result.Response.PaymentId, request.Amount),
                PaymentState.FAILED => new PaymentFailed(
                    Guid.NewGuid(), correlationId, messageId, DateTime.UtcNow,
                    request.TripId, result.Response.PaymentId,
                    (db.Payments.Local.FirstOrDefault(p => p.Id == result.Response.PaymentId)
                        ?? await db.Payments.SingleAsync(p => p.Id == result.Response.PaymentId, ct)).FailureReason
                        ?? throw new InvalidOperationException("Failed payment has no reason.")),
                _ => throw new InvalidOperationException("Checkout has no definitive result yet.")
            };

            db.ProcessedMessages.Add(new ProcessedMessage
            {
                MessageId = messageId,
                ProcessedAt = DateTime.UtcNow
            });
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = outcome.EventId,
                Type = outcome.GetType().Name,
                Payload = JsonSerializer.Serialize(outcome, outcome.GetType()),
                CorrelationId = outcome.CorrelationId,
                OccurredAt = outcome.OccurredAt
            });
            return 0;
        }, cancellationToken);
}
