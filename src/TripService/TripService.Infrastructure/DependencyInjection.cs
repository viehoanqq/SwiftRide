using SwiftRide.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TripService.Infrastructure.Persistence;


using TripService.Application.Interfaces;          // thÃªm
using TripService.Infrastructure.Clients;
using TripService.Infrastructure.Messaging;
using TripService.Infrastructure.Outbox;
using TripService.Infrastructure.Repositories; 
using TripService.Application.Services;
using SwiftRide.Contracts.Events;

namespace TripService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, Microsoft.Extensions.Hosting.IHostEnvironment? environment = null)
    {
        services.AddDbContext<TripDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("TripDb")));
        services.AddScoped<ITripRepository, TripRepository>();     // thÃªm: dÃ¹ng DbContext nÃªn Scoped
        services.AddScoped<TripPaymentResultHandler>();

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

        services.AddSwiftRideMessaging(configuration, environment,
            configureConsumers: bus =>
            {
                bus.AddConsumer<PaymentSettledConsumer>()
                    .Endpoint(endpoint => endpoint.Name = QueueNames.TripPaymentSettled);
                bus.AddConsumer<PaymentFailedConsumer>()
                    .Endpoint(endpoint => endpoint.Name = QueueNames.TripPaymentFailed);
            });
        if (environment?.IsEnvironment("Testing") != true)
            services.AddHostedService<TripOutboxPublisher>();
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


