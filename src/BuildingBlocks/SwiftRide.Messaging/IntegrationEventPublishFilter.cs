using MassTransit;
using Microsoft.Extensions.Logging;
using SwiftRide.Contracts.Events;

namespace SwiftRide.Messaging;

public sealed class IntegrationEventPublishFilter<T>(ILogger<IntegrationEventPublishFilter<T>> logger)
    : IFilter<PublishContext<T>> where T : class
{
    public async Task Send(PublishContext<T> context, IPipe<PublishContext<T>> next)
    {
        if (context.Message is not IIntegrationEvent message)
        {
            await next.Send(context);
            return;
        }
        context.MessageId = message.EventId;
        context.CorrelationId = message.CorrelationId;
        var (tripId, userId, queue) = IntegrationEventLogFields.From(message);
        using var scope = logger.BeginScope(IntegrationEventLogFields.Scope(message, context.MessageId, queue, 0));
        try
        {
            await next.Send(context);
            logger.LogInformation("Message published. Outcome={Outcome} EventType={EventType} EventId={EventId} MessageId={MessageId} CorrelationId={CorrelationId} CausationId={CausationId} TripId={TripId} UserId={UserId} Queue={Queue}",
                "Success", typeof(T).Name, message.EventId, context.MessageId, message.CorrelationId,
                message.CausationId, tripId, userId, queue);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Message publish failed. Outcome={Outcome} EventType={EventType} EventId={EventId} MessageId={MessageId} CorrelationId={CorrelationId} CausationId={CausationId} TripId={TripId} UserId={UserId} Queue={Queue}",
                "Failed", typeof(T).Name, message.EventId, context.MessageId, message.CorrelationId,
                message.CausationId, tripId, userId, queue);
            throw;
        }
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("integrationEventPublishLog");
}
