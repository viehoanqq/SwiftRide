using Moq;
using TripService.Application.DTOs;
using TripService.Application.Exceptions;
using TripService.Application.Interfaces;
using TripService.Application.Services;
using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Domain.Exceptions;

namespace TripService.Tests.Services;

public class TripConfirmTests
{
    private readonly Mock<ITripRepository> _mockTripRepo = new();
    private readonly Mock<IMatchingClient> _mockMatchingClient = new();
    private readonly TripAppService _service;

    public TripConfirmTests()
    {
        _service = new TripAppService(_mockTripRepo.Object, Mock.Of<IDriverStatusClient>());
        _mockMatchingClient
            .Setup(client => client.MatchAsync(It.IsAny<MatchDriverRequest>()))
            .ReturnsAsync(new MatchDriverResult(
                Guid.NewGuid().ToString(),
                "Driver",
                "Available",
                1.2,
                new GeoPointDto(10.77, 106.69)));
    }

    private static Trip CreateSampleQuotedTrip(Guid riderId)
    {
        var trip = Trip.Create(riderId, new TripLocation
        {
            PickupLat = 10.7725m,
            PickupLng = 10.6980m,
            PickupAddress = "Quận 1",
            DropoffLat = 10.7750m,
            DropoffLng = 10.7000m,
            DropoffAddress = "Quận 3"
        });
        trip.MarkQuoted(new TripFare
        {
            QuoteId = "quote-001",
            Price = 50000m,
            Status = FareStatus.Pending,
            Strategy = "Standard",
            DistanceKm = 3.0,
            DurationMin = 10.0,
            QuotedAt = DateTime.UtcNow.AddMinutes(-5)
        });

        return trip;
    }

    [Fact]
    public async Task ConfirmAsync_ShouldCancelTrip_WhenAcceptIsFalse()
    {
        // Arrange
        var riderId = Guid.NewGuid();
        var trip = CreateSampleQuotedTrip(riderId);
        var tripId = trip.Id;

        _mockTripRepo.Setup(r => r.GetByIdAsync(tripId)).ReturnsAsync(trip);

        var request = new ConfirmTripRequest(Accept: false, PaymentMethod: null);

        // Act
        var response = await _service.ConfirmAsync(tripId, riderId, request);

        // Assert
        Assert.Equal("Cancelled", response.Status);
        Assert.Equal(TripStatus.Cancelled, trip.Status);
        Assert.NotNull(trip.Fare);
        Assert.Equal(FareStatus.Rejected, trip.Fare.Status);
        Assert.NotNull(trip.Fare.RespondedAt);
        Assert.Contains(trip.History, h => h.FromStatus == TripStatus.Quoted && h.ToStatus == TripStatus.Cancelled);
        _mockTripRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Theory]
    [InlineData("WALLET", PaymentMethod.Wallet)]
    [InlineData("wallet", PaymentMethod.Wallet)]
    [InlineData("CARD", PaymentMethod.Card)]
    [InlineData("card", PaymentMethod.Card)]
    public async Task ConfirmAsync_ShouldAcceptFareAndSetPaymentMethod_WhenAcceptIsTrue(string paymentMethodInput, PaymentMethod expectedMethod)
    {
        // Arrange
        var riderId = Guid.NewGuid();
        var trip = CreateSampleQuotedTrip(riderId);
        var tripId = trip.Id;

        _mockTripRepo.Setup(r => r.GetByIdAsync(tripId)).ReturnsAsync(trip);

        var token = expectedMethod == PaymentMethod.Card ? "test-card-approved" : null;
        var request = new ConfirmTripRequest(Accept: true, PaymentMethod: paymentMethodInput, CardToken: token);

        // Act
        var response = await _service.ConfirmAsync(tripId, riderId, request);

        // Assert
        Assert.Equal("FindingDriver", response.Status);
        Assert.Equal(TripStatus.FindingDriver, trip.Status);
        Assert.NotNull(trip.Fare);
        Assert.Equal(FareStatus.Accepted, trip.Fare.Status);
        Assert.Equal(expectedMethod, trip.Fare.PaymentMethod);
        Assert.Equal(token, trip.Fare.CardToken);
        Assert.NotNull(trip.Fare.RespondedAt);
        Assert.Empty(trip.Offers);
        Assert.Equal(TripStatus.Quoted, trip.History[^1].FromStatus);
        Assert.Equal(TripStatus.FindingDriver, trip.History[^1].ToStatus);
        _mockTripRepo.Verify(r => r.AddOutboxEvent(It.Is<SwiftRide.Contracts.Events.IIntegrationEvent>(
            e => e is SwiftRide.Contracts.Events.MatchRequested && e.CorrelationId == tripId)), Times.Once);
        _mockMatchingClient.VerifyNoOtherCalls();
        _mockTripRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task ConfirmAsync_RejectsMissingCardTokenAndWalletToken()
    {
        var riderId = Guid.NewGuid();
        var trip = CreateSampleQuotedTrip(riderId);
        _mockTripRepo.Setup(r => r.GetByIdAsync(trip.Id)).ReturnsAsync(trip);

        await Assert.ThrowsAsync<ValidationException>(() => _service.ConfirmAsync(
            trip.Id, riderId, new ConfirmTripRequest(true, "CARD")));
        await Assert.ThrowsAsync<ValidationException>(() => _service.ConfirmAsync(
            trip.Id, riderId, new ConfirmTripRequest(true, "WALLET", "test-card-approved")));

        Assert.Equal(TripStatus.Quoted, trip.Status);
        _mockTripRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task ConfirmAsync_DuplicateDoesNotQueueAnotherRequest_AndPendingCanBeCancelled()
    {
        var riderId = Guid.NewGuid();
        var trip = CreateSampleQuotedTrip(riderId);
        _mockTripRepo.Setup(r => r.GetByIdAsync(trip.Id)).ReturnsAsync(trip);
        await _service.ConfirmAsync(trip.Id, riderId, new(true, "WALLET"));
        await Assert.ThrowsAsync<InvalidTripStateException>(() =>
            _service.ConfirmAsync(trip.Id, riderId, new(true, "WALLET")));
        _mockTripRepo.Verify(r => r.AddOutboxEvent(It.IsAny<SwiftRide.Contracts.Events.IIntegrationEvent>()), Times.Once);
        var cancelled = await _service.ConfirmAsync(trip.Id, riderId, new(false, null));
        Assert.Equal("Cancelled", cancelled.Status);
        _mockMatchingClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConfirmAsync_ShouldThrowNotFound_WhenTripDoesNotExist()
    {
        // Arrange
        var tripId = Guid.NewGuid();
        var riderId = Guid.NewGuid();

        _mockTripRepo.Setup(r => r.GetByIdAsync(tripId)).ReturnsAsync((Trip?)null);

        var request = new ConfirmTripRequest(Accept: true, PaymentMethod: "WALLET");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _service.ConfirmAsync(tripId, riderId, request));
        Assert.Equal("TRIP_NOT_FOUND", ex.Code);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task ConfirmAsync_ShouldThrowForbidden_WhenUserIsNotTripRider()
    {
        // Arrange
        var tripRiderId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var trip = CreateSampleQuotedTrip(tripRiderId);
        var tripId = trip.Id;

        _mockTripRepo.Setup(r => r.GetByIdAsync(tripId)).ReturnsAsync(trip);

        var request = new ConfirmTripRequest(Accept: true, PaymentMethod: "WALLET");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => _service.ConfirmAsync(tripId, otherUserId, request));
        Assert.Equal("FORBIDDEN", ex.Code);
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [InlineData(TripStatus.Requested, true)]
    [InlineData(TripStatus.DriverOffered, true)]
    [InlineData(TripStatus.DriverOffered, false)]
    [InlineData(TripStatus.Cancelled, true)]
    [InlineData(TripStatus.Cancelled, false)]
    [InlineData(TripStatus.Failed, true)]
    [InlineData(TripStatus.Failed, false)]
    public async Task ConfirmAsync_ShouldThrowInvalidTripStateException_WhenStatusIsNotQuoted(
        TripStatus invalidStatus,
        bool accept)
    {
        // Arrange
        var riderId = Guid.NewGuid();
        var trip = CreateSampleQuotedTrip(riderId);
        switch (invalidStatus)
        {
            case TripStatus.Requested:
                trip = Trip.Create(riderId, trip.Location);
                break;
            case TripStatus.DriverOffered:
                trip.Confirm(PaymentMethod.Wallet);
                trip.OfferDriver(Guid.NewGuid(), 1);
                break;
            case TripStatus.Cancelled:
                trip.Cancel();
                break;
            case TripStatus.Failed:
                trip.MarkFailed();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(invalidStatus), invalidStatus, null);
        }
        var tripId = trip.Id;

        _mockTripRepo.Setup(r => r.GetByIdAsync(tripId)).ReturnsAsync(trip);

        var request = new ConfirmTripRequest(Accept: accept, PaymentMethod: accept ? "WALLET" : null);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidTripStateException>(() => _service.ConfirmAsync(tripId, riderId, request));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CASH")]
    [InlineData("BITCOIN")]
    public async Task ConfirmAsync_ShouldThrowValidationException_WhenAcceptIsTrueAndPaymentMethodInvalid(string? invalidMethod)
    {
        // Arrange
        var riderId = Guid.NewGuid();
        var trip = CreateSampleQuotedTrip(riderId);
        var tripId = trip.Id;

        _mockTripRepo.Setup(r => r.GetByIdAsync(tripId)).ReturnsAsync(trip);

        var request = new ConfirmTripRequest(Accept: true, PaymentMethod: invalidMethod);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.ConfirmAsync(tripId, riderId, request));
        Assert.Equal("VALIDATION_ERROR", ex.Code);
        Assert.Equal(400, ex.StatusCode);
    }
}
