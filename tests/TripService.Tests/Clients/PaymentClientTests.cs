using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Moq.Protected;
using SwiftRide.Contracts.Payments;
using TripService.Application.Exceptions;
using TripService.Infrastructure.Clients;

namespace TripService.Tests.Clients;

public class PaymentClientTests
{
    private static CreatePaymentRequest SampleRequest(Guid? tripId = null)
    {
        var id = tripId ?? Guid.NewGuid();
        return new CreatePaymentRequest(
            id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            50000m,
            PaymentMethod.WALLET,
            PaymentProtocol.TripPaymentKey(id));
    }

    private static PaymentClient CreateClient(Mock<HttpMessageHandler> handlerMock)
    {
        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:5003")
        };
        return new PaymentClient(httpClient);
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, PaymentState.SUCCEEDED)]
    [InlineData(HttpStatusCode.OK, PaymentState.FAILED)]
    public async Task CreateAsync_Accepts201And200_AndSendsIdempotencyHeader(
        HttpStatusCode statusCode,
        PaymentState expectedState)
    {
        var request = SampleRequest();
        var paymentId = Guid.NewGuid();
        HttpRequestMessage? captured = null;

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = JsonContent.Create(new CreatePaymentResponse(paymentId, expectedState))
            });

        var client = CreateClient(handlerMock);
        var response = await client.CreateAsync(request, CancellationToken.None);

        Assert.Equal(paymentId, response.PaymentId);
        Assert.Equal(expectedState, response.Status);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal("/payments", captured.RequestUri!.AbsolutePath);
        Assert.True(captured.Headers.TryGetValues(PaymentProtocol.IdempotencyHeader, out var values));
        Assert.Equal(request.IdempotencyKey, Assert.Single(values));
    }

    [Fact]
    public async Task CreateAsync_Throws504_WhenTimedOut()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("Simulated timeout"));

        var client = CreateClient(handlerMock);
        var ex = await Assert.ThrowsAsync<DownstreamException>(() =>
            client.CreateAsync(SampleRequest(), CancellationToken.None));

        Assert.Equal(504, ex.StatusCode);
        Assert.Equal(PaymentErrorCodes.PaymentTimeout, ex.Code);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task CreateAsync_Throws502_WhenPaymentReturnsErrorStatus(HttpStatusCode statusCode)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode));

        var client = CreateClient(handlerMock);
        var ex = await Assert.ThrowsAsync<DownstreamException>(() =>
            client.CreateAsync(SampleRequest(), CancellationToken.None));

        Assert.Equal(502, ex.StatusCode);
        Assert.Equal(PaymentErrorCodes.PaymentUnavailable, ex.Code);
    }

    [Fact]
    public async Task CreateAsync_Throws502_WhenNetworkFails()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var client = CreateClient(handlerMock);
        var ex = await Assert.ThrowsAsync<DownstreamException>(() =>
            client.CreateAsync(SampleRequest(), CancellationToken.None));

        Assert.Equal(502, ex.StatusCode);
        Assert.Equal(PaymentErrorCodes.PaymentUnavailable, ex.Code);
    }

    [Fact]
    public async Task CreateAsync_Throws502InvalidResponse_WhenPayloadInvalid()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new CreatePaymentResponse(Guid.Empty, PaymentState.SUCCEEDED))
            });

        var client = CreateClient(handlerMock);
        var ex = await Assert.ThrowsAsync<DownstreamException>(() =>
            client.CreateAsync(SampleRequest(), CancellationToken.None));

        Assert.Equal(502, ex.StatusCode);
        Assert.Equal(PaymentErrorCodes.InvalidPaymentResponse, ex.Code);
    }

    [Fact]
    public async Task GetByTripAsync_ReturnsNullOnlyOn404_AndReturnsDetailOn200()
    {
        var tripId = Guid.NewGuid();
        var detail = new PaymentDetailResponse(
            Guid.NewGuid(),
            tripId,
            50000m,
            PaymentMethod.WALLET,
            PaymentState.SUCCEEDED,
            1,
            null,
            40000m,
            10000m);

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .SetupSequence<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(detail)
            });

        var client = CreateClient(handlerMock);

        Assert.Null(await client.GetByTripAsync(tripId, CancellationToken.None));

        var result = await client.GetByTripAsync(tripId, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(detail.Id, result.Id);
        Assert.Equal(PaymentState.SUCCEEDED, result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetByTripAsync_DoesNotConvertNon404ErrorsToNull(HttpStatusCode statusCode)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode));

        var client = CreateClient(handlerMock);
        var ex = await Assert.ThrowsAsync<DownstreamException>(() =>
            client.GetByTripAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(502, ex.StatusCode);
        Assert.Equal(PaymentErrorCodes.PaymentUnavailable, ex.Code);
    }

    [Fact]
    public async Task ServiceAuthorizationHandler_EmitsRequiredMatchingAndPaymentScopes()
    {
        const string signingKey = "test-signing-key-with-more-than-32-characters-12345";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "swiftride-trip-service",
                ["Jwt:Audience"] = "swiftride",
                ["Jwt:Key"] = signingKey
            })
            .Build();

        HttpRequestMessage? captured = null;
        var innerMock = new Mock<HttpMessageHandler>();
        innerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        var handler = new ServiceAuthorizationHandler(config) { InnerHandler = innerMock.Object };
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5003") };
        await httpClient.GetAsync("/payments/by-trip/" + Guid.NewGuid());

        Assert.NotNull(captured);
        Assert.Equal("Bearer", captured.Headers.Authorization?.Scheme);
        var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = tokenHandler.ValidateToken(
            captured.Headers.Authorization!.Parameter!,
            new TokenValidationParameters
            {
                ValidIssuer = "swiftride-trip-service",
                ValidAudience = "swiftride",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                ValidateLifetime = true
            },
            out _);

        Assert.Equal(PaymentProtocol.ServiceSubject, principal.FindFirstValue("sub"));
        var scopes = principal.FindAll("scope").Select(c => c.Value).ToList();
        Assert.Contains("drivers:status", scopes);
        Assert.Contains(PaymentProtocol.CreateScope, scopes);
        Assert.Contains(PaymentProtocol.ReadScope, scopes);
    }
}
