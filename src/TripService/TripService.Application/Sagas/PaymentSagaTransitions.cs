namespace TripService.Application.Sagas;

public enum PaymentSagaState
{
    AwaitingPayment,
    PaymentUncertain,
    Completed,
    Failed,
    Compensating,
    Compensated,
    NeedsAttention
}

public enum PaymentSagaSignal
{
    PaymentSettled,
    PaymentDeclined,
    PaymentOutcomeUnknown,
    PaymentStillPending,
    ManualReviewRequired,
    VerifiedPaymentSettled,
    VerifiedPaymentDeclined,
    ApprovedRefundForVerifiedCharge,
    PaymentRefunded,
    RefundRejected,
    RefundOutcomeUnknown
}

/// <summary>
/// Pure transition rules. The caller verifies message identity, payment ownership,
/// charge evidence and approval before choosing a signal, then persists the result.
/// </summary>
public static class PaymentSagaTransitions
{
    public static PaymentSagaState Next(PaymentSagaState state, PaymentSagaSignal signal) =>
        (state, signal) switch
        {
            (PaymentSagaState.AwaitingPayment, PaymentSagaSignal.PaymentSettled) => PaymentSagaState.Completed,
            (PaymentSagaState.AwaitingPayment, PaymentSagaSignal.PaymentDeclined) => PaymentSagaState.Failed,
            (PaymentSagaState.AwaitingPayment, PaymentSagaSignal.PaymentOutcomeUnknown) => PaymentSagaState.PaymentUncertain,
            (PaymentSagaState.AwaitingPayment, PaymentSagaSignal.ManualReviewRequired) => PaymentSagaState.NeedsAttention,

            (PaymentSagaState.PaymentUncertain, PaymentSagaSignal.PaymentSettled or PaymentSagaSignal.VerifiedPaymentSettled) => PaymentSagaState.Completed,
            (PaymentSagaState.PaymentUncertain, PaymentSagaSignal.PaymentDeclined or PaymentSagaSignal.VerifiedPaymentDeclined) => PaymentSagaState.Failed,
            (PaymentSagaState.PaymentUncertain, PaymentSagaSignal.PaymentOutcomeUnknown or PaymentSagaSignal.PaymentStillPending) => PaymentSagaState.PaymentUncertain,
            (PaymentSagaState.PaymentUncertain, PaymentSagaSignal.ApprovedRefundForVerifiedCharge) => PaymentSagaState.Compensating,
            (PaymentSagaState.PaymentUncertain, PaymentSagaSignal.ManualReviewRequired) => PaymentSagaState.NeedsAttention,

            (PaymentSagaState.Completed, PaymentSagaSignal.ApprovedRefundForVerifiedCharge) => PaymentSagaState.Compensating,
            (PaymentSagaState.NeedsAttention, PaymentSagaSignal.VerifiedPaymentSettled) => PaymentSagaState.Completed,
            (PaymentSagaState.NeedsAttention, PaymentSagaSignal.VerifiedPaymentDeclined) => PaymentSagaState.Failed,
            (PaymentSagaState.NeedsAttention, PaymentSagaSignal.ApprovedRefundForVerifiedCharge) => PaymentSagaState.Compensating,

            (PaymentSagaState.Compensating, PaymentSagaSignal.PaymentRefunded) => PaymentSagaState.Compensated,
            (PaymentSagaState.Compensating, PaymentSagaSignal.RefundRejected or PaymentSagaSignal.ManualReviewRequired) => PaymentSagaState.NeedsAttention,
            (PaymentSagaState.Compensating, PaymentSagaSignal.RefundOutcomeUnknown) => PaymentSagaState.Compensating,

            // A timeout scheduled before a definitive result is stale. Identity checks still
            // belong to the caller so an unrelated timeout cannot be silently accepted.
            (PaymentSagaState.Completed or PaymentSagaState.Failed or PaymentSagaState.Compensated,
                PaymentSagaSignal.PaymentOutcomeUnknown or PaymentSagaSignal.RefundOutcomeUnknown) => state,

            _ => throw new InvalidOperationException($"Invalid payment saga transition: {state} + {signal}.")
        };
}
