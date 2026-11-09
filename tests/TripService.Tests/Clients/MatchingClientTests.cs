using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Moq;
using Moq.Protected;
using TripService.Application.DTOs;
using TripService.Application.Exceptions;
using TripService.Infrastructure.Clients;

namespace TripService.Tests.Clients;

public class MatchingClientTests
{
    private readonly Mock<IHttpContextAccessor> _mockHttpContextAccessor = new();

    [Fact]
    public async Task GetQuoteAsync_ShouldReturnQuoteResult_WhenResponseIs200Ok()
    {
        // Arrange
        var tripId = Guid.NewGuid();
        var pickup = new GeoPointDto(10.7725, 10.6980);
        var dropoff = new GeoPointDto(10.7750, 10.7000);
        var expectedQuote = new QuoteResult("quote-123", 2.5, 8.0, "Standard", 35000m);

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(expectedQuote)
            });

        var httpClient = new HttpClient(new ForwardRequestHeadersHandler(_mockHttpContextAccessor.Object) { InnerHandler = handlerMock.Object })
        {
            BaseAddress = new Uri("http://localhost:5002")
        };

        var client = new MatchingClient(httpClient, _mockHttpContextAccessor.Object);

        // Act
        var result = await client.GetQuoteAsync(tripId, pickup, dropoff);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("quote-123", result.QuoteId);
        Assert.Equal(35000m, result.Price);
        Assert.Equal(2.5, result.DistanceKm);
    }

    [Fact]
    public async Task GetQuoteAsync_ShouldReturnNull_WhenResponseIs404NotFound()
    {
        // Arrange
        var tripId = Guid.NewGuid();
        var pickup = new GeoPointDto(10.7725, 10.6980);
        var dropoff = new GeoPointDto(10.7750, 10.7000);

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NotFound,
                Content = JsonContent.Create(new { code = "NO_DRIVER_AVAILABLE" })
            });

        var httpClient = new HttpClient(new ForwardRequestHeadersHandler(_mockHttpContextAccessor.Object) { InnerHandler = handlerMock.Object })
        {
            BaseAddress = new Uri("http://localhost:5002")
        };

        var client = new MatchingClient(httpClient, _mockHttpContextAccessor.Object);

        // Act
        var result = await client.GetQuoteAsync(tripId, pickup, dropoff);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetQuoteAsync_ShouldForwardAuthorizationHeader_WhenPresent()
    {
        // Arrange
        var tripId = Guid.NewGuid();
        var pickup = new GeoPointDto(10.7725, 10.6980);
        var dropoff = new GeoPointDto(10.7750, 10.7000);
        var expectedQuote = new QuoteResult("quote-auth", 3.0, 10.0, "Standard", 40000m);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer my-jwt-token";
        _mockHttpContextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        HttpRequestMessage? capturedRequest = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(expectedQuote)
            });

        var httpClient = new HttpClient(new ForwardRequestHeadersHandler(_mockHttpContextAccessor.Object) { InnerHandler = handlerMock.Object })
        {
            BaseAddress = new Uri("http://localhost:5002")
        };

        var client = new MatchingClient(httpClient, _mockHttpContextAccessor.Object);

        // Act
        var result = await client.GetQuoteAsync(tripId, pickup, dropoff);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(capturedRequest);
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization?.Scheme);
        Assert.Equal("my-jwt-token", capturedRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task GetQuoteAsync_ShouldNotRetry_WhenTransient500Occurs()
    {
        // Arrange
        var tripId = Guid.NewGuid();
        var pickup = new GeoPointDto(10.7725, 10.6980);
        var dropoff = new GeoPointDto(10.7750, 10.7000);
        var expectedQuote = new QuoteResult("quote-retry", 1.5, 5.0, "Standard", 25000m);

        var attempts = 0;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(() =>
            {
                attempts++;
                if (attempts == 1)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                }

                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = JsonContent.Create(expectedQuote)
                });
            });

        var httpClient = new HttpClient(new ForwardRequestHeadersHandler(_mockHttpContextAccessor.Object) { InnerHandler = handlerMock.Object })
        {
            BaseAddress = new Uri("http://localhost:5002")
        };

        var client = new MatchingClient(httpClient, _mockHttpContextAccessor.Object);

        // Act
        await Assert.ThrowsAsync<DownstreamException>(() => client.GetQuoteAsync(tripId, pickup, dropoff));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task GetQuoteAsync_ShouldThrow_WhenAllRetriesFailWith500()
    {
        // Arrange
        var tripId = Guid.NewGuid();
        var pickup = new GeoPointDto(10.7725, 10.6980);
        var dropoff = new GeoPointDto(10.7750, 10.7000);

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(() => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var httpClient = new HttpClient(new ForwardRequestHeadersHandler(_mockHttpContextAccessor.Object) { InnerHandler = handlerMock.Object })
        {
            BaseAddress = new Uri("http://localhost:5002")
        };

        var client = new MatchingClient(httpClient, _mockHttpContextAccessor.Object);

        // Act & Assert
        await Assert.ThrowsAnyAsync<DownstreamException>(() => client.GetQuoteAsync(tripId, pickup, dropoff));
    }

    [Fact]
    public async Task GetQuoteAsync_ShouldNotRetry_WhenResponseIsBadRequest()
    {
        var attempts = 0;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(() =>
            {
                attempts++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            });

        var httpClient = new HttpClient(new ForwardRequestHeadersHandler(_mockHttpContextAccessor.Object) { InnerHandler = handlerMock.Object })
        {
            BaseAddress = new Uri("http://localhost:5002")
        };
        var client = new MatchingClient(httpClient, _mockHttpContextAccessor.Object);

        await Assert.ThrowsAsync<DownstreamException>(() =>
            client.GetQuoteAsync(Guid.NewGuid(), new GeoPointDto(10, 10), new GeoPointDto(11, 11)));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task GetQuoteAsync_ShouldThrow_WhenSuccessfulResponseHasNoBody()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        var httpClient = new HttpClient(new ForwardRequestHeadersHandler(_mockHttpContextAccessor.Object) { InnerHandler = handlerMock.Object })
        {
            BaseAddress = new Uri("http://localhost:5002")
        };
        var client = new MatchingClient(httpClient, _mockHttpContextAccessor.Object);

        await Assert.ThrowsAsync<DownstreamException>(() =>
            client.GetQuoteAsync(Guid.NewGuid(), new GeoPointDto(10, 10), new GeoPointDto(11, 11)));
    }

    [Fact]
    public async Task MatchAsync_ShouldPostRequestAndReturnDriver_WhenMatchingFindsOne()
    {
        var driverId = Guid.NewGuid();
        var expectedDriver = new MatchDriverResult(
            driverId.ToString(),
            "Driver One",
            "Available",
            1.25,
            new GeoPointDto(10.77, 106.69));
        HttpRequestMessage? capturedRequest = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(expectedDriver)
            });

        var httpClient = new HttpClient(new ForwardRequestHeadersHandler(_mockHttpContextAccessor.Object) { InnerHandler = handlerMock.Object })
        {
            BaseAddress = new Uri("http://localhost:5002")
        };
        var client = new MatchingClient(httpClient, _mockHttpContextAccessor.Object);

        var result = await client.MatchAsync(new MatchDriverRequest(10.77, 106.69));

        Assert.NotNull(result);
        Assert.Equal(driverId.ToString(), result.DriverId);
        Assert.Equal("Driver One", result.Name);
        Assert.Equal("/match", capturedRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task MatchAsync_ShouldReturnNull_WhenMatchingReturnsNotFound()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = JsonContent.Create(new { code = "NO_DRIVER_AVAILABLE" }) });

        var httpClient = new HttpClient(new ForwardRequestHeadersHandler(_mockHttpContextAccessor.Object) { InnerHandler = handlerMock.Object })
        {
            BaseAddress = new Uri("http://localhost:5002")
        };
        var client = new MatchingClient(httpClient, _mockHttpContextAccessor.Object);

        var result = await client.MatchAsync(new MatchDriverRequest(10.77, 106.69));

        Assert.Null(result);
    }
}
