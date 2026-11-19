using TripService.Application.Sagas;

namespace TripService.Tests.Services;

public sealed class PaymentSagaTransitionsTests
{
    [Fact]
    public void PaymentTimeout_DoesNotBecomePaymentFailedOrStartRefund()
    {
        var state = PaymentSagaTransitions.Next(
            PaymentSagaState.AwaitingPayment, PaymentSagaSignal.PaymentOutcomeUnknown);
        Assert.Equal(PaymentSagaState.PaymentUncertain, state);
        Assert.Equal(state, PaymentSagaTransitions.Next(state, PaymentSagaSignal.PaymentStillPending));
        Assert.Equal(PaymentSagaState.Completed,
            PaymentSagaTransitions.Next(state, PaymentSagaSignal.PaymentSettled));
        Assert.Equal(PaymentSagaState.Completed,
            PaymentSagaTransitions.Next(PaymentSagaState.Completed, PaymentSagaSignal.PaymentOutcomeUnknown));
    }

    [Fact]
    public void ApprovedRefund_RequiresVerifiedChargeAndOnlyRefundConfirmationCompletesIt()
    {
        Assert.Throws<InvalidOperationException>(() => PaymentSagaTransitions.Next(
            PaymentSagaState.AwaitingPayment, PaymentSagaSignal.ApprovedRefundForVerifiedCharge));
        Assert.Throws<InvalidOperationException>(() => PaymentSagaTransitions.Next(
            PaymentSagaState.Failed, PaymentSagaSignal.ApprovedRefundForVerifiedCharge));

        var compensating = PaymentSagaTransitions.Next(
            PaymentSagaState.Completed, PaymentSagaSignal.ApprovedRefundForVerifiedCharge);
        Assert.Equal(PaymentSagaState.Compensating, compensating);
        Assert.Equal(compensating,
            PaymentSagaTransitions.Next(compensating, PaymentSagaSignal.RefundOutcomeUnknown));
        Assert.Equal(PaymentSagaState.Compensated,
            PaymentSagaTransitions.Next(compensating, PaymentSagaSignal.PaymentRefunded));
    }

    [Fact]
    public void DefinitiveRefundRejection_NeedsAttentionAndNeverClaimsCompensated()
    {
        Assert.Equal(PaymentSagaState.NeedsAttention,
            PaymentSagaTransitions.Next(PaymentSagaState.Compensating, PaymentSagaSignal.RefundRejected));
        Assert.Throws<InvalidOperationException>(() => PaymentSagaTransitions.Next(
            PaymentSagaState.NeedsAttention, PaymentSagaSignal.PaymentRefunded));
        Assert.Equal(PaymentSagaState.Compensating,
            PaymentSagaTransitions.Next(PaymentSagaState.NeedsAttention,
                PaymentSagaSignal.ApprovedRefundForVerifiedCharge));
    }

    [Fact]
    public void PaymentDeclined_IsTerminalUnlessManualReconciliationChangesTheEvidence()
    {
        Assert.Equal(PaymentSagaState.Failed,
            PaymentSagaTransitions.Next(PaymentSagaState.AwaitingPayment, PaymentSagaSignal.PaymentDeclined));
        Assert.Throws<InvalidOperationException>(() => PaymentSagaTransitions.Next(
            PaymentSagaState.Failed, PaymentSagaSignal.PaymentSettled));
        Assert.Equal(PaymentSagaState.Completed,
            PaymentSagaTransitions.Next(PaymentSagaState.NeedsAttention,
                PaymentSagaSignal.VerifiedPaymentSettled));
    }
}
