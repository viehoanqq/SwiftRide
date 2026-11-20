using SwiftRide.Contracts.Events;

namespace SwiftRide.Messaging;

internal static class IntegrationEventLogFields
{
    public static (Guid? TripId, Guid? UserId, string Queue) From(IIntegrationEvent message) => message switch
    {
        QuoteRequested e => (e.TripId, null, QueueNames.MatchingQuoteRequested),
        QuoteCalculated e => (e.TripId, null, QueueNames.TripQuoteCalculated),
        QuoteFailed e => (e.TripId, null, QueueNames.TripQuoteFailed),
        UserRegistered e => (null, e.UserId, QueueNames.PaymentUserRegistered),
        DriverRegistered e => (null, e.DriverId, QueueNames.MatchingDriverRegistered),
        TripAccepted e => (e.TripId, null, QueueNames.MatchingTripAccepted),
        RideCompleted e => (e.TripId, null, QueueNames.PaymentRideCompleted),
        PaymentSettled e => (e.TripId, null, QueueNames.TripPaymentSettled),
        PaymentFailed e => (e.TripId, null, QueueNames.TripPaymentFailed),
        TripClosed e => (e.TripId, null, QueueNames.MatchingTripClosed),
        ProcessPayment e => (e.TripId, null, QueueNames.PaymentProcessPayment),
        ProcessPaymentTimedOut e => (e.TripId, null, QueueNames.TripProcessPaymentTimedOut),
        RefundPayment e => (e.TripId, null, QueueNames.PaymentRefundPayment),
        PaymentRefunded e => (e.TripId, null, QueueNames.TripPaymentRefunded),
        PaymentRefundFailed e => (e.TripId, null, QueueNames.TripPaymentRefundFailed),
        _ => (null, null, "unknown")
    };

    public static Dictionary<string, object?> Scope(IIntegrationEvent message, Guid? transportMessageId,
        string queue, int retryAttempt)
    {
        var (tripId, userId, _) = From(message);
        return new Dictionary<string, object?>
        {
            ["EventId"] = message.EventId,
            ["MessageId"] = transportMessageId,
            ["CorrelationId"] = message.CorrelationId,
            ["CausationId"] = message.CausationId,
            ["TripId"] = tripId,
            ["UserId"] = userId,
            ["Queue"] = queue,
            ["RetryAttempt"] = retryAttempt
        };
    }
}
