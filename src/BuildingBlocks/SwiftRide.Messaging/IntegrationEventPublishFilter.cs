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
        // MassTransit also traverses the IIntegrationEvent publish topology. Log only the concrete event.
        if (typeof(T) == typeof(IIntegrationEvent))
        {
            await next.Send(context);
            return;
        }
        var (tripId, userId, queue) = IntegrationEventLogFields.From(message);
        var description = IntegrationEventDescriptions.Name(message);
        var subject = IntegrationEventDescriptions.Subject(message, tripId, userId);
        using var scope = logger.BeginScope(IntegrationEventLogFields.Scope(message, context.MessageId, queue, 0));
        try
        {
            await next.Send(context);
            logger.LogInformation("Message published. Outcome={Outcome} EventType={EventType} EventId={EventId} MessageId={MessageId} CorrelationId={CorrelationId} CausationId={CausationId} TripId={TripId} UserId={UserId} Queue={Queue}\n      Diễn giải: Đã phát tin '{Description}' cho {Subject}; queue nhận: {Queue}.",
                "Success", typeof(T).Name, message.EventId, context.MessageId, message.CorrelationId,
                message.CausationId, tripId, userId, queue, description, subject, queue);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Message publish failed. Outcome={Outcome} EventType={EventType} EventId={EventId} MessageId={MessageId} CorrelationId={CorrelationId} CausationId={CausationId} TripId={TripId} UserId={UserId} Queue={Queue}\n      Diễn giải: Phát tin '{Description}' cho {Subject} thất bại; kiểm tra lỗi kèm theo.",
                "Failed", typeof(T).Name, message.EventId, context.MessageId, message.CorrelationId,
                message.CausationId, tripId, userId, queue, description, subject);
            throw;
        }
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("integrationEventPublishLog");
}
