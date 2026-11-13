using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using PaymentService.Application.DTOs;
using PaymentService.Application.Exceptions;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Api.Security;

public static class PaymentSecurity
{
    public const string RiderPolicy = "PaymentRider";
    public const string WalletPolicy = "PaymentWallet";
    public const string ByTripPolicy = "PaymentByTrip";

    public static IServiceCollection AddPaymentSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection("Jwt");
        var key = jwt["Key"];
        if (string.IsNullOrWhiteSpace(jwt["Issuer"]) || string.IsNullOrWhiteSpace(jwt["Audience"]) ||
            string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Configure Jwt:Issuer, Jwt:Audience and a Jwt:Key of at least 32 bytes.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = jwt["Issuer"],
                ValidateAudience = true, ValidAudience = jwt["Audience"],
                ValidateLifetime = true, ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ClockSkew = TimeSpan.FromSeconds(30), NameClaimType = "name", RoleClaimType = "role"
            };
            options.Events = new JwtBearerEvents
            {
                OnChallenge = async context =>
                {
                    context.HandleResponse();
                    context.Response.StatusCode = 401;
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    await context.Response.WriteAsJsonAsync(new ApiError("UNAUTHORIZED", "Token không hợp lệ, đã hết hạn hoặc chưa đăng nhập."));
                },
                OnForbidden = async context =>
                {
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsJsonAsync(new ApiError("FORBIDDEN", "Bạn không có quyền thực hiện thao tác này."));
                }
            };
        });
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            options.AddPolicy(PaymentProtocol.CreatePolicy, p => p.RequireAuthenticatedUser()
                .RequireAssertion(c => IsService(c.User, PaymentProtocol.CreateScope)));
            options.AddPolicy(PaymentProtocol.ReadInternalPolicy, p => p.RequireAuthenticatedUser()
                .RequireAssertion(c => IsService(c.User, PaymentProtocol.ReadScope)));
            options.AddPolicy(PaymentProtocol.RefundPolicy, p => p.RequireAuthenticatedUser()
                .RequireAssertion(c => IsService(c.User, PaymentProtocol.RefundScope)));
            options.AddPolicy(RiderPolicy, p => p.RequireAuthenticatedUser()
                .RequireAssertion(c => IsUser(c.User) && c.User.IsInRole("Rider")));
            options.AddPolicy(WalletPolicy, p => p.RequireAuthenticatedUser().RequireAssertion(c => IsUser(c.User)));
            options.AddPolicy(ByTripPolicy, p => p.RequireAuthenticatedUser()
                .RequireAssertion(c => IsUser(c.User) || IsService(c.User, PaymentProtocol.ReadScope)));
        });
        return services;
    }

    // Policies check identity type. Application MUST additionally check payment ownership.
    private static bool IsUser(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub"), out var id) && id != Guid.Empty &&
        (user.IsInRole("Rider") || user.IsInRole("Driver"));

    public static bool IsService(ClaimsPrincipal user, string scope) =>
        user.FindFirstValue("sub") == PaymentProtocol.ServiceSubject &&
        user.FindAll("scope").Any(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope));

    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub"), out var id) && id != Guid.Empty
            ? id : throw new UnauthorizedException("UNAUTHORIZED", "Token không có ID người dùng hợp lệ.");
}
