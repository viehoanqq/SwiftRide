using System.Text.Json;
using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Domain.Exceptions;

namespace TripService.Tests.Domain;

public class TripDriverStateTests
{
    [Fact]
    public void AcceptOffer_CorrectDriver_AcceptsOfferAndAddsOneHistory()
    {
        var (trip, driverId) = CreateOfferedTrip();
        var count = trip.History.Count;
        var before = DateTime.UtcNow;

        trip.AcceptOffer(driverId);

        var offer = Assert.Single(trip.Offers);
        Assert.Equal(OfferStatus.Accepted, offer.Status);
        Assert.NotNull(offer.RespondedAt);
        Assert.InRange(offer.RespondedAt.Value, before, DateTime.UtcNow);
        Assert.True(trip.UpdatedAt >= offer.RespondedAt.Value);
        AssertTransition(trip, count, TripStatus.DriverOffered, TripStatus.DriverAccepted);
    }

    [Fact]
    public void AcceptOffer_WrongDriver_ThrowsWithoutChangingTrip()
    {
        var (trip, _) = CreateOfferedTrip();
        var before = Snapshot(trip);

        Assert.Throws<InvalidTripStateException>(() => trip.AcceptOffer(Guid.NewGuid()));

        Assert.Equal(before, Snapshot(trip));
    }

    [Fact]
    public void RejectOffer_CorrectDriver_RejectsOfferWithoutAddingHistory()
    {
        var (trip, driverId) = CreateOfferedTrip();
        var history = JsonSerializer.Serialize(trip.History);
        var before = DateTime.UtcNow;

        trip.RejectOffer(driverId);

        var offer = Assert.Single(trip.Offers);
        Assert.Equal(OfferStatus.Rejected, offer.Status);
        Assert.NotNull(offer.RespondedAt);
        Assert.InRange(offer.RespondedAt.Value, before, DateTime.UtcNow);
        Assert.Equal(offer.RespondedAt.Value, trip.UpdatedAt);
        Assert.Equal(TripStatus.DriverOffered, trip.Status);
        Assert.Equal(history, JsonSerializer.Serialize(trip.History));
    }

    [Fact]
    public void RejectOffer_Twice_SecondCallThrowsWithoutChangingTrip()
    {
        var (trip, driverId) = CreateOfferedTrip();
        trip.RejectOffer(driverId);
        var before = Snapshot(trip);

        Assert.Throws<InvalidTripStateException>(() => trip.RejectOffer(driverId));

        Assert.Equal(before, Snapshot(trip));
    }

    [Fact]
    public void OfferDriver_AfterRejection_AddsPendingOfferWithoutAddingHistory()
    {
        var (trip, driverId) = CreateOfferedTrip();
        trip.RejectOffer(driverId);
        var firstOffer = JsonSerializer.Serialize(trip.Offers[0]);
        var history = JsonSerializer.Serialize(trip.History);
        var nextDriverId = Guid.NewGuid();

        var offer = trip.OfferDriver(nextDriverId, 2, 1.5);

        Assert.Equal(2, trip.Offers.Count);
        Assert.Same(offer, trip.Offers[1]);
        Assert.Equal(nextDriverId, offer.DriverId);
        Assert.Equal(trip.Id, offer.TripId);
        Assert.Equal(2, offer.AttemptNo);
        Assert.Equal(OfferStatus.Pending, offer.Status);
        Assert.Null(offer.RespondedAt);
        Assert.Equal(firstOffer, JsonSerializer.Serialize(trip.Offers[0]));
        Assert.Equal(TripStatus.DriverOffered, trip.Status);
        Assert.Equal(history, JsonSerializer.Serialize(trip.History));
    }

    [Fact]
    public void AcceptThenEnRouteThenPickup_EachActionAddsOneHistory()
    {
        var (trip, driverId) = CreateOfferedTrip();
        var count = trip.History.Count;

        trip.AcceptOffer(driverId);
        AssertTransition(trip, count, TripStatus.DriverOffered, TripStatus.DriverAccepted);

        trip.MarkEnRoute();
        AssertTransition(trip, count + 1, TripStatus.DriverAccepted, TripStatus.EnRoute);

        trip.MarkPickedUp();
        AssertTransition(trip, count + 2, TripStatus.EnRoute, TripStatus.PickedUp);
        Assert.Equal(OfferStatus.Accepted, Assert.Single(trip.Offers).Status);
    }

    [Fact]
    public void MarkPickedUp_BeforeEnRoute_ThrowsWithoutChangingTrip()
    {
        var (trip, driverId) = CreateOfferedTrip();
        trip.AcceptOffer(driverId);
        var before = Snapshot(trip);

        Assert.Throws<InvalidTripStateException>(() => trip.MarkPickedUp());

        Assert.Equal(before, Snapshot(trip));
    }

    [Fact]
    public void MarkDroppedOff_WhenPickedUp_AddsTwoOrderedHistoryEntries()
    {
        var trip = CreatePickedUpTrip();
        var count = trip.History.Count;
        var previousHistory = JsonSerializer.Serialize(trip.History);
        var offers = JsonSerializer.Serialize(trip.Offers);

        trip.MarkDroppedOff();

        Assert.Equal(TripStatus.PaymentPending, trip.Status);
        Assert.Equal(count + 2, trip.History.Count);
        Assert.Equal(previousHistory, JsonSerializer.Serialize(trip.History.Take(count).ToList()));
        Assert.Collection(trip.History.Skip(count),
            entry => AssertHistory(entry, trip.Id, TripStatus.PickedUp, TripStatus.DroppedOff),
            entry => AssertHistory(entry, trip.Id, TripStatus.DroppedOff, TripStatus.PaymentPending));
        Assert.True(trip.History[^2].CreatedAt <= trip.History[^1].CreatedAt);
        Assert.Equal(trip.History[^1].CreatedAt, trip.UpdatedAt);
        Assert.Equal(offers, JsonSerializer.Serialize(trip.Offers));
    }

    [Fact]
    public void MarkDroppedOff_Twice_SecondCallThrowsWithoutChangingTrip()
    {
        var trip = CreatePickedUpTrip();
        trip.MarkDroppedOff();
        var before = Snapshot(trip);

        Assert.Throws<InvalidTripStateException>(() => trip.MarkDroppedOff());

        Assert.Equal(before, Snapshot(trip));
    }

    private static (Trip Trip, Guid DriverId) CreateOfferedTrip()
    {
        var trip = Trip.Create(Guid.NewGuid(), new TripLocation
        {
            PickupLat = 10.7769m,
            PickupLng = 106.7009m,
            PickupAddress = "Pickup",
            DropoffLat = 10.782m,
            DropoffLng = 106.69m,
            DropoffAddress = "Dropoff"
        });
        trip.MarkQuoted(new TripFare
        {
            QuoteId = Guid.NewGuid().ToString(),
            Price = 50000m,
            Status = FareStatus.Pending,
            QuotedAt = DateTime.UtcNow,
            DistanceKm = 5,
            DurationMin = 10,
            Strategy = "Standard"
        });
        trip.Confirm(PaymentMethod.Wallet);
        var driverId = Guid.NewGuid();
        trip.OfferDriver(driverId, 1, 1);
        return (trip, driverId);
    }

    private static Trip CreatePickedUpTrip()
    {
        var (trip, driverId) = CreateOfferedTrip();
        trip.AcceptOffer(driverId);
        trip.MarkEnRoute();
        trip.MarkPickedUp();
        return trip;
    }

    // Capture values, not mutable references, including timestamps and all history entries.
    private static string Snapshot(Trip trip) => JsonSerializer.Serialize(trip);

    private static void AssertTransition(Trip trip, int previousCount, TripStatus from, TripStatus to)
    {
        Assert.Equal(to, trip.Status);
        Assert.Equal(previousCount + 1, trip.History.Count);
        AssertHistory(trip.History[^1], trip.Id, from, to);
        Assert.Equal(trip.History[^1].CreatedAt, trip.UpdatedAt);
    }

    private static void AssertHistory(TripStatusHistory entry, Guid tripId, TripStatus from, TripStatus to)
    {
        Assert.Equal(tripId, entry.TripId);
        Assert.Equal(from, entry.FromStatus);
        Assert.Equal(to, entry.ToStatus);
    }
}
