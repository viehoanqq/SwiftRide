using System.Text.Json;
using SwiftRide.Contracts.Events;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Tests;

public sealed class PaymentSagaContractTests
{
    [Fact]
    public void Commands_UseStableTripKeysAndCorrelationAcrossSerialization()
    {
        var tripId = Guid.NewGuid();
        var rideEventId = Guid.NewGuid();
        var deadline = DateTime.UtcNow.AddMinutes(2);
        var command = new ProcessPayment(Guid.NewGuid(), tripId, rideEventId,
            DateTime.UtcNow, tripId, Guid.NewGuid(), Guid.NewGuid(), 50_000m,
            PaymentMethod.CARD, PaymentProtocol.TripPaymentKey(tripId), deadline,
            "test-card-approved");
        var restored = JsonSerializer.Deserialize<ProcessPayment>(JsonSerializer.Serialize(command));

        Assert.Equal(command, restored);
        Assert.Equal(tripId, restored!.CorrelationId);
        Assert.Equal(rideEventId, restored.CausationId);
        Assert.Equal(PaymentProtocol.TripPaymentKey(tripId), restored.IdempotencyKey);
        Assert.Equal("trip:" + tripId.ToString("D") + ":refund:v1", PaymentProtocol.TripRefundKey(tripId));
        Assert.Throws<ArgumentException>(() => PaymentProtocol.TripRefundKey(Guid.Empty));
    }

    [Fact]
    public void RefundOutcomes_KeepCommandCausationAndNoCardToken()
    {
        var tripId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var refundCommandId = Guid.NewGuid();
        IIntegrationEvent refunded = new PaymentRefunded(Guid.NewGuid(), tripId, refundCommandId,
            DateTime.UtcNow, tripId, paymentId, 50_000m,
            SagaRefundReasonCodes.InvalidTripCharge);
        IIntegrationEvent failed = new PaymentRefundFailed(Guid.NewGuid(), tripId, refundCommandId,
            DateTime.UtcNow, tripId, paymentId,
            SagaRefundFailureCodes.InsufficientRecipientFunds);

        Assert.Equal(refundCommandId, refunded.CausationId);
        Assert.Equal(refundCommandId, failed.CausationId);
        Assert.Equal(EventSchema.V1, refunded.SchemaVersion);
        Assert.DoesNotContain("CardToken", JsonSerializer.Serialize(refunded, refunded.GetType()));
        Assert.DoesNotContain("CardToken", JsonSerializer.Serialize(failed, failed.GetType()));
    }

}
