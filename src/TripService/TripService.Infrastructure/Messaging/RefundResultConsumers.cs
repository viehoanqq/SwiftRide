using MassTransit;
using SwiftRide.Contracts.Events;
using TripService.Application.Sagas;

namespace TripService.Infrastructure.Messaging;

public sealed class PaymentRefundedConsumer(TripRefundResultHandler handler) : IConsumer<PaymentRefunded>
{
    public Task Consume(ConsumeContext<PaymentRefunded> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class PaymentRefundFailedConsumer(TripRefundResultHandler handler) : IConsumer<PaymentRefundFailed>
{
    public Task Consume(ConsumeContext<PaymentRefundFailed> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}
