using MassTransit;
using Microsoft.Extensions.Logging;
using SwiftRide.Contracts.Events;

namespace SwiftRide.Messaging;

public sealed class IntegrationEventConsumeFilter<T>(ILogger<IntegrationEventConsumeFilter<T>> logger)
    : IFilter<ConsumeContext<T>> where T : class
{
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        if (context.Message is not IIntegrationEvent message)
        {
            await next.Send(context);
            return;
        }
        var (_, _, expectedQueue) = IntegrationEventLogFields.From(message);
        var queue = context.ReceiveContext.InputAddress?.Segments.LastOrDefault()?.Trim('/') ?? expectedQueue;
        var attempt = context.GetRetryAttempt();
        using var scope = logger.BeginScope(IntegrationEventLogFields.Scope(message, context.MessageId, queue, attempt));
        try
        {
            await next.Send(context);
            logger.LogInformation("Message consumed. Outcome={Outcome} EventType={EventType} EventId={EventId} MessageId={MessageId} CorrelationId={CorrelationId} CausationId={CausationId} TripId={TripId} UserId={UserId} Queue={Queue} RetryAttempt={RetryAttempt}",
                "Success", typeof(T).Name, message.EventId, context.MessageId, message.CorrelationId, message.CausationId,
                IntegrationEventLogFields.From(message).TripId,
                IntegrationEventLogFields.From(message).UserId, queue, attempt);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Message consume failed. Outcome={Outcome} EventType={EventType} EventId={EventId} MessageId={MessageId} CorrelationId={CorrelationId} CausationId={CausationId} TripId={TripId} UserId={UserId} Queue={Queue} RetryAttempt={RetryAttempt}",
                "Failed", typeof(T).Name, message.EventId, context.MessageId, message.CorrelationId, message.CausationId,
                IntegrationEventLogFields.From(message).TripId,
                IntegrationEventLogFields.From(message).UserId, queue, attempt);
            throw;
        }
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("integrationEventConsumeLog");
}
