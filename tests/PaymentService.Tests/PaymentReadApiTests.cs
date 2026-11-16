using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using PaymentService.Application.DTOs;
using PaymentService.Application.Interfaces;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Tests;

public class PaymentReadApiTests
{
    [Theory]
    [InlineData("service-create", false, 201)]
    [InlineData("service-create", true, 200)]
    [InlineData("Rider", false, 403)]
    [InlineData("Driver", false, 403)]
    [InlineData("service-refund", false, 403)]
    [InlineData("anonymous", false, 401)]
    public async Task Create_IsInternal_AndReturnsCreatedOrReplay(string identity, bool replay, int expected)
    {
        using var app = new PaymentReadFactory();
        using var client = app.CreateClient();
        app.Payments.Setup(p => p.CreateAsync(It.IsAny<CreatePaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentCreationResult(new(app.PaymentId, PaymentState.SUCCEEDED), replay));
        if (identity != "anonymous") client.DefaultRequestHeaders.Authorization = new("Bearer", app.Token(identity));
        var request = new CreatePaymentRequest(app.TripId, app.UserId, Guid.NewGuid(), 100m, PaymentMethod.WALLET, "create-once");
        using var response = await client.PostAsJsonAsync("/payments", request);
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        app.Payments.Verify(p => p.CreateAsync(It.IsAny<CreatePaymentRequest>(), It.IsAny<CancellationToken>()),
            expected is 200 or 201 ? Times.Once() : Times.Never());
        if (expected is 200 or 201)
        {
            var body = await response.Content.ReadFromJsonAsync<CreatePaymentResponse>();
            Assert.Equal(app.PaymentId, body!.PaymentId);
            Assert.Equal(PaymentState.SUCCEEDED, body.Status);
        }
    }

    [Theory]
    [InlineData("Rider", "id", 200)]
    [InlineData("Driver", "id", 403)]
    [InlineData("Rider", "trip", 200)]
    [InlineData("Driver", "trip", 200)]
    [InlineData("Rider", "wallet", 200)]
    [InlineData("Driver", "wallet", 200)]
    [InlineData("service-create", "refund", 403)]
    [InlineData("service-refund", "refund", 200)]
    [InlineData("Rider", "refund", 403)]
    [InlineData("Driver", "refund", 403)]
    [InlineData("service-refund", "wallet", 403)]
    [InlineData("anonymous", "wallet", 401)]
    public async Task Policies_UseVerifiedCallerAndKeepRefundInternal(string identity, string endpoint, int expected)
    {
        using var app = new PaymentReadFactory();
        using var client = app.CreateClient();
        if (identity != "anonymous") client.DefaultRequestHeaders.Authorization = new("Bearer", app.Token(identity));
        var path = endpoint switch {
            "id" => $"/payments/{app.PaymentId}", "trip" => $"/payments/by-trip/{app.TripId}",
            "refund" => $"/payments/{app.PaymentId}/refund", _ => "/wallets/me" };
        client.DefaultRequestHeaders.Add(PaymentProtocol.IdempotencyHeader, "refund-test");
        using var response = endpoint == "refund"
            ? await client.PostAsJsonAsync(path, new RefundPaymentRequest("Customer request"))
            : await client.GetAsync(path);
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        if (expected == 200 && endpoint == "wallet")
            app.Wallets.Verify(w => w.GetMineAsync(app.UserId, identity, It.IsAny<CancellationToken>()), Times.Once);
        if (expected == 200 && endpoint == "id")
            app.Payments.Verify(p => p.GetByIdAsync(app.PaymentId, app.UserId, It.IsAny<CancellationToken>()), Times.Once);
        if (expected == 200 && endpoint == "trip")
            app.Payments.Verify(p => p.GetByTripAsync(app.TripId, app.UserId, identity, It.IsAny<CancellationToken>()), Times.Once);
        if (endpoint == "refund")
            app.Payments.Verify(p => p.RefundAsync(app.PaymentId, It.IsAny<RefundPaymentRequest>(), "refund-test", It.IsAny<CancellationToken>()),
                expected == 200 ? Times.Once() : Times.Never());
    }
}

internal sealed class PaymentReadFactory : WebApplicationFactory<Program>
{
    private const string Key = "payment-api-tests-only-signing-key-12345678901234567890";
    public Guid UserId { get; } = Guid.NewGuid();
    public Guid PaymentId { get; } = Guid.NewGuid();
    public Guid TripId { get; } = Guid.NewGuid();
    public Mock<IPaymentAppService> Payments { get; } = new();
    public Mock<IWalletAppService> Wallets { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
            ["Jwt:Issuer"] = "payment-tests", ["Jwt:Audience"] = "payment-tests", ["Jwt:Key"] = Key }));
        var detail = new PaymentDetailResponse(PaymentId, TripId, 100m, PaymentMethod.WALLET, PaymentState.SUCCEEDED, 1, null, 80m, 20m);
        Payments.Setup(p => p.GetByIdAsync(PaymentId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(detail);
        Payments.Setup(p => p.GetByTripAsync(TripId, UserId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(detail);
        Payments.Setup(p => p.RefundAsync(PaymentId, It.IsAny<RefundPaymentRequest>(), "refund-test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefundPaymentResponse(PaymentState.REFUNDED));
        Wallets.Setup(w => w.GetMineAsync(UserId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new WalletResponse(100m, []));
        builder.ConfigureTestServices(services => {
            // PaymentSecurity captures the key before the test host applies its config.
            // Override only test credentials; retain signature/lifetime and all policies.
            services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>("Bearer", options => {
                options.TokenValidationParameters.ValidIssuer = "payment-tests";
                options.TokenValidationParameters.ValidAudience = "payment-tests";
                options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key));
            });
            services.RemoveAll<IPaymentAppService>(); services.AddSingleton(Payments.Object);
            services.RemoveAll<IWalletAppService>(); services.AddSingleton(Wallets.Object);
        });
    }

    public string Token(string identity)
    {
        var claims = identity.StartsWith("service-")
            ? new[] { new Claim("sub", PaymentProtocol.ServiceSubject), new Claim("scope", identity == "service-refund" ? PaymentProtocol.RefundScope : PaymentProtocol.CreateScope) }
            : new[] { new Claim("sub", UserId.ToString()), new Claim("role", identity) };
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("payment-tests", "payment-tests", claims,
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256)));
    }
}
