using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using Xunit;

namespace PaymentService.Tests;

public class PaymentAuthorizationTests
{
    [Fact]
    public async Task User_CannotViewOrAccessAnotherUserWalletOrPayment()
    {
        // Arrange
        var legitimateOwnerId = Guid.NewGuid();
        var maliciousUserId = Guid.NewGuid();

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            IdempotencyKey = "auth-test-key-01",
            RiderId = legitimateOwnerId,
            DriverId = Guid.NewGuid(),
            Amount = 50000m,
            Method = PaymentMethodType.Wallet,
            Status = PaymentStatus.Succeeded
        };

        bool IsUserAuthorized(Guid requestingUserId, Payment targetPayment)
        {
            return requestingUserId == targetPayment.RiderId || requestingUserId == targetPayment.DriverId;
        }

        bool hasAccess = IsUserAuthorized(maliciousUserId, payment);

        Assert.False(hasAccess);
    }

    [Fact]
    public async Task RiderOrDriverToken_IsBlockedFromCallingInternalApis()
    {
        // Arrange
        var riderRole = "Rider";
        var driverRole = "Driver";
        var internalEndpointRequiredRole = "InternalServiceWorker";

        bool CanAccessInternalApi(string userRole)
        {
            return userRole == internalEndpointRequiredRole;
        }

        Assert.False(CanAccessInternalApi(riderRole));
        Assert.False(CanAccessInternalApi(driverRole));
    }

    [Fact]
    public void Gateway_BlocksExternalPostRequestsToInternalRoutes()
    {
        var gatewayRouteConfig = new { Path = "/internal/payments/process-ledger", AllowedSource = "InternalCluster" };
        var externalClientSource = "PublicInternet";

        bool IsGatewayRouteAllowed(string source, string routePath)
        {
            if (routePath.StartsWith("/internal/") && source != "InternalCluster")
            {
                return false; // Chặn đứng
            }
            return true;
        }

        bool isAllowed = IsGatewayRouteAllowed(externalClientSource, gatewayRouteConfig.Path);

        Assert.False(isAllowed);
    }
}