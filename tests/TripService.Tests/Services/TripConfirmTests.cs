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
        _service = new TripAppService(_mockTripRepo.Object, _mockMatchingClient.Object);
    }

    private static Trip CreateSampleQuotedTrip(Guid tripId, Guid riderId)
    {
        return new Trip
        {
            Id = tripId,
            RiderId = riderId,
            Status = TripStatus.Quoted,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
            Location = new TripLocation
            {
                TripId = tripId,
                PickupLat = 10.7725m,
                PickupLng = 10.6980m,
                PickupAddress = "Quận 1",
                DropoffLat = 10.7750m,
                DropoffLng = 10.7000m,
                DropoffAddress = "Quận 3"
            },
            Fare = new TripFare
            {
                TripId = tripId,
                QuoteId = "quote-001",
                Price = 50000m,
                Status = FareStatus.Pending,
                Strategy = "Standard",
                DistanceKm = 3.0,
                DurationMin = 10.0,
                QuotedAt = DateTime.UtcNow.AddMinutes(-5)
            },
            History = []
        };
    }

    [Fact]
    public async Task ConfirmAsync_ShouldCancelTrip_WhenAcceptIsFalse()
    {
        // Arrange
        var tripId = Guid.NewGuid();
        var riderId = Guid.NewGuid();
        var trip = CreateSampleQuotedTrip(tripId, riderId);

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
        var tripId = Guid.NewGuid();
        var riderId = Guid.NewGuid();
        var trip = CreateSampleQuotedTrip(tripId, riderId);

        _mockTripRepo.Setup(r => r.GetByIdAsync(tripId)).ReturnsAsync(trip);

        var request = new ConfirmTripRequest(Accept: true, PaymentMethod: paymentMethodInput);

        // Act
        var response = await _service.ConfirmAsync(tripId, riderId, request);

        // Assert
        Assert.Equal("Quoted", response.Status);
        Assert.Equal(TripStatus.Quoted, trip.Status);
        Assert.NotNull(trip.Fare);
        Assert.Equal(FareStatus.Accepted, trip.Fare.Status);
        Assert.Equal(expectedMethod, trip.Fare.PaymentMethod);
        Assert.NotNull(trip.Fare.RespondedAt);
        _mockTripRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
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
        var tripId = Guid.NewGuid();
        var tripRiderId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var trip = CreateSampleQuotedTrip(tripId, tripRiderId);

        _mockTripRepo.Setup(r => r.GetByIdAsync(tripId)).ReturnsAsync(trip);

        var request = new ConfirmTripRequest(Accept: true, PaymentMethod: "WALLET");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => _service.ConfirmAsync(tripId, otherUserId, request));
        Assert.Equal("FORBIDDEN", ex.Code);
        Assert.Equal(403, ex.StatusCode);
    }

    [Theory]
    [InlineData(TripStatus.Requested)]
    [InlineData(TripStatus.DriverOffered)]
    [InlineData(TripStatus.DriverAccepted)]
    [InlineData(TripStatus.Cancelled)]
    [InlineData(TripStatus.Paid)]
    [InlineData(TripStatus.Failed)]
    public async Task ConfirmAsync_ShouldThrowInvalidTripStateException_WhenStatusIsNotQuoted(TripStatus invalidStatus)
    {
        // Arrange
        var tripId = Guid.NewGuid();
        var riderId = Guid.NewGuid();
        var trip = CreateSampleQuotedTrip(tripId, riderId);
        trip.Status = invalidStatus;

        _mockTripRepo.Setup(r => r.GetByIdAsync(tripId)).ReturnsAsync(trip);

        var request = new ConfirmTripRequest(Accept: true, PaymentMethod: "WALLET");

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
        var tripId = Guid.NewGuid();
        var riderId = Guid.NewGuid();
        var trip = CreateSampleQuotedTrip(tripId, riderId);

        _mockTripRepo.Setup(r => r.GetByIdAsync(tripId)).ReturnsAsync(trip);

        var request = new ConfirmTripRequest(Accept: true, PaymentMethod: invalidMethod);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.ConfirmAsync(tripId, riderId, request));
        Assert.Equal("VALIDATION_ERROR", ex.Code);
        Assert.Equal(400, ex.StatusCode);
    }
}
