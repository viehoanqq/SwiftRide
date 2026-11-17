using MassTransit;
using MatchingService.Application.Services;
using SwiftRide.Contracts.Events;

namespace MatchingService.Infrastructure.Messaging;

public sealed class TripAcceptedConsumer(TripLifecycleHandler handler) : IConsumer<TripAccepted>
{
    public Task Consume(ConsumeContext<TripAccepted> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class TripClosedConsumer(TripLifecycleHandler handler) : IConsumer<TripClosed>
{
    public Task Consume(ConsumeContext<TripClosed> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}
