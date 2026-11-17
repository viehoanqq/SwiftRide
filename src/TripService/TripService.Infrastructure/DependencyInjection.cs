using SwiftRide.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TripService.Infrastructure.Persistence;


using TripService.Application.Interfaces;          // thÃªm
using TripService.Infrastructure.Clients;
using TripService.Infrastructure.Repositories; 

namespace TripService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, Microsoft.Extensions.Hosting.IHostEnvironment? environment = null)
    {
        services.AddDbContext<TripDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("TripDb")));
        services.AddScoped<ITripRepository, TripRepository>();     // thÃªm: dÃ¹ng DbContext nÃªn Scoped

        services.AddHttpContextAccessor();
        services.AddTransient<ForwardRequestHeadersHandler>();
        services.AddTransient<ServiceAuthorizationHandler>();
        var matchingServiceUrl = configuration["Services:MatchingService"];
        if (!Uri.TryCreate(matchingServiceUrl, UriKind.Absolute, out var matchingServiceUri) ||
            (matchingServiceUri.Scheme != Uri.UriSchemeHttp && matchingServiceUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Configuration 'Services:MatchingService' must be an absolute HTTP or HTTPS URL.");
        }

        services.AddHttpClient<IMatchingClient, MatchingClient>(client =>
        {
            client.BaseAddress = matchingServiceUri;
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).AddHttpMessageHandler<ForwardRequestHeadersHandler>();

        services.AddHttpClient<IDriverStatusClient, DriverStatusClient>(client =>
        {
            client.BaseAddress = matchingServiceUri;
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).AddHttpMessageHandler<ServiceAuthorizationHandler>();

        var paymentServiceUrl = configuration["Services:PaymentService"];
        if (!Uri.TryCreate(paymentServiceUrl, UriKind.Absolute, out var paymentServiceUri) ||
            (paymentServiceUri.Scheme != Uri.UriSchemeHttp && paymentServiceUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Configuration 'Services:PaymentService' must be an absolute HTTP or HTTPS URL.");
        }

        services.AddHttpClient<IPaymentClient, PaymentClient>(client =>
        {
            client.BaseAddress = paymentServiceUri;
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).AddHttpMessageHandler<ServiceAuthorizationHandler>();
        services.AddSwiftRideMessaging(configuration, environment);
        return services;
    }

    // Tá»± Ã¡p dá»¥ng migration khi service khá»Ÿi Ä‘á»™ng, Ä‘á»ƒ cáº£ nhÃ³m khÃ´ng pháº£i cháº¡y "dotnet ef database update" thá»§ cÃ´ng.
    public static async Task MigrateDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        await db.Database.MigrateAsync();
    }
}


