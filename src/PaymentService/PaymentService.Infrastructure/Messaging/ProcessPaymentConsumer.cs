using MassTransit;
using PaymentService.Application.Interfaces;
using PaymentService.Application.Services;
using PaymentService.Infrastructure.Persistence;
using SwiftRide.Contracts.Events;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Infrastructure.Messaging;

public sealed class ProcessPaymentConsumer(
    PaymentCheckoutService checkout,
    IUnitOfWork unitOfWork,
    PayDbContext db) : IConsumer<ProcessPayment>
{
    public Task Consume(ConsumeContext<ProcessPayment> context)
    {
        var command = context.Message;
        if (command.EventId == Guid.Empty || command.TripId == Guid.Empty ||
            command.RiderId == Guid.Empty || command.DriverId == Guid.Empty ||
            command.CorrelationId != command.TripId ||
            command.OccurredAt.Kind != DateTimeKind.Utc ||
            command.DeadlineAt.Kind != DateTimeKind.Utc ||
            command.DeadlineAt <= command.OccurredAt ||
            command.SchemaVersion != EventSchema.V1 ||
            command.IdempotencyKey != PaymentProtocol.TripPaymentKey(command.TripId))
            throw new ArgumentException("Invalid ProcessPayment command.");

        var request = new CreatePaymentRequest(
            command.TripId, command.RiderId, command.DriverId, command.Amount,
            command.PaymentMethod, command.IdempotencyKey, command.CardToken);

        return PaymentCommandProcessor.HandleAsync(checkout, unitOfWork, db,
            request, command.EventId, command.CorrelationId, context.CancellationToken);
    }
}
