using System.Text.Json;
using SwiftRide.Contracts.Payments;
using TripService.Application.Exceptions;
using TripService.Application.Services;
using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Domain.Exceptions;
using TripService.Tests.Services;

namespace TripService.Tests.Domain;

public class TripPaymentStateTests
{
    private static Trip Pending()
    {
        var driver = Guid.NewGuid();
        var trip = TripDriverWorkflowTests.OfferedTrip(driver);
        trip.AcceptOffer(driver);
        trip.MarkEnRoute();
        trip.MarkPickedUp();
        trip.MarkDroppedOff();
        return trip;
    }

    [Theory]
    [InlineData(PaymentState.SUCCEEDED, TripStatus.Paid)]
    [InlineData(PaymentState.FAILED, TripStatus.PaymentFailed)]
    public void FinalResult_AddsExactlyOneHistory_AndReplayDoesNothing(PaymentState result, TripStatus expected)
    {
        var trip = Pending();
        var count = trip.History.Count;
        PaymentResultMapping.Apply(trip, result);
        Assert.Equal(expected, trip.Status);
        Assert.Equal(count + 1, trip.History.Count);
        Assert.Equal(TripStatus.PaymentPending, trip.History[^1].FromStatus);
        Assert.Equal(expected, trip.History[^1].ToStatus);
        var snapshot = JsonSerializer.Serialize(trip);
        PaymentResultMapping.Apply(trip, result);
        PaymentResultMapping.Apply(trip, PaymentState.PENDING);
        Assert.Equal(snapshot, JsonSerializer.Serialize(trip));
    }

    [Fact]
    public void Pending_DoesNotInventFailure()
    {
        var trip = Pending();
        var snapshot = JsonSerializer.Serialize(trip);
        PaymentResultMapping.Apply(trip, PaymentState.PENDING);
        Assert.Equal(snapshot, JsonSerializer.Serialize(trip));
    }

    [Fact]
    public void Refund_KeepsPaidTrip_AndRejectsUnpaidTrip()
    {
        var trip = Pending();
        Assert.Throws<DownstreamException>(() => PaymentResultMapping.Apply(trip, PaymentState.REFUNDED));
        trip.MarkPaid();
        var snapshot = JsonSerializer.Serialize(trip);
        PaymentResultMapping.Apply(trip, PaymentState.REFUNDED);
        Assert.Equal(snapshot, JsonSerializer.Serialize(trip));
    }

    [Theory]
    [InlineData(PaymentState.SUCCEEDED)]
    [InlineData(PaymentState.FAILED)]
    public void BeforeDropoff_RejectsFinalResult(PaymentState result)
    {
        var trip = TripDriverWorkflowTests.OfferedTrip(Guid.NewGuid());
        var snapshot = JsonSerializer.Serialize(trip);
        Assert.Throws<InvalidTripStateException>(() => PaymentResultMapping.Apply(trip, result));
        Assert.Equal(snapshot, JsonSerializer.Serialize(trip));
    }

    [Fact]
    public void ContradictoryFinalResult_CannotOverwritePaid()
    {
        var trip = Pending();
        trip.MarkPaid();
        Assert.Throws<InvalidTripStateException>(() => trip.MarkPaymentFailed());
        Assert.Throws<DownstreamException>(() => PaymentResultMapping.Apply(trip, (PaymentState)999));
        Assert.Equal(TripStatus.Paid, trip.Status);
    }
}
