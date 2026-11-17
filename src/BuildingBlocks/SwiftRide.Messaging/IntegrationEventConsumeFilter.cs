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
        var (tripId, userId, _) = IntegrationEventLogFields.From(message);
        var description = IntegrationEventDescriptions.Name(message);
        var subject = IntegrationEventDescriptions.Subject(message, tripId, userId);
        using var scope = logger.BeginScope(IntegrationEventLogFields.Scope(message, context.MessageId, queue, attempt));
        try
        {
            await next.Send(context);
            logger.LogInformation("Message consumed. Outcome={Outcome} EventType={EventType} EventId={EventId} MessageId={MessageId} CorrelationId={CorrelationId} CausationId={CausationId} TripId={TripId} UserId={UserId} Queue={Queue} RetryAttempt={RetryAttempt}\n      Diễn giải: Đã xử lý tin '{Description}' cho {Subject}; lần thử: {Attempt}.",
                "Success", typeof(T).Name, message.EventId, context.MessageId, message.CorrelationId, message.CausationId,
                tripId, userId, queue, attempt, description, subject, attempt);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Message consume failed. Outcome={Outcome} EventType={EventType} EventId={EventId} MessageId={MessageId} CorrelationId={CorrelationId} CausationId={CausationId} TripId={TripId} UserId={UserId} Queue={Queue} RetryAttempt={RetryAttempt}\n      Diễn giải: Xử lý tin '{Description}' cho {Subject} thất bại ở lần thử {Attempt}; xem lỗi kèm theo.",
                "Failed", typeof(T).Name, message.EventId, context.MessageId, message.CorrelationId, message.CausationId,
                tripId, userId, queue, attempt, description, subject, attempt);
            throw;
        }
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("integrationEventConsumeLog");
}
