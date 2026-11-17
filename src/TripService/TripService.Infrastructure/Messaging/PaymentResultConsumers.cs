using MassTransit;
using SwiftRide.Contracts.Events;
using TripService.Application.Services;

namespace TripService.Infrastructure.Messaging;

public sealed class PaymentSettledConsumer(TripPaymentResultHandler handler) : IConsumer<PaymentSettled>
{
    public Task Consume(ConsumeContext<PaymentSettled> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class PaymentFailedConsumer(TripPaymentResultHandler handler) : IConsumer<PaymentFailed>
{
    public Task Consume(ConsumeContext<PaymentFailed> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}
