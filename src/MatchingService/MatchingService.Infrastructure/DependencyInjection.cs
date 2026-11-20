using SwiftRide.Messaging;
using MatchingService.Application.Interfaces;
using MatchingService.Application.Services;
using MatchingService.Infrastructure.Messaging;
using MatchingService.Infrastructure.Mongo;
using MatchingService.Infrastructure.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SwiftRide.Contracts.Events;

namespace MatchingService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, Microsoft.Extensions.Hosting.IHostEnvironment? environment = null)
    {
        services.AddSingleton(new MongoContext(configuration.GetConnectionString("MatchDb")!));
        services.AddScoped<IDriverRepository, DriverRepository>();
        services.AddScoped<DriverAppService>();
        services.AddScoped<TripLifecycleHandler>();
        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddSwiftRideMessaging(configuration, environment,
            configureConsumers: bus =>
            {
                bus.AddConsumer<QuoteRequestedConsumer>()
                    .Endpoint(endpoint => endpoint.Name = QueueNames.MatchingQuoteRequested);
                bus.AddConsumer<DriverRegisteredConsumer>()
                    .Endpoint(endpoint => endpoint.Name = QueueNames.MatchingDriverRegistered);
                bus.AddConsumer<TripAcceptedConsumer>()
                    .Endpoint(endpoint => endpoint.Name = QueueNames.MatchingTripAccepted);
                bus.AddConsumer<TripClosedConsumer>()
                    .Endpoint(endpoint => endpoint.Name = QueueNames.MatchingTripClosed);
            });
        if (environment?.IsEnvironment("Testing") != true)
            services.AddHostedService<QuoteOutboxPublisher>();
        return services;
    }
    public static async Task InitializeDatabaseAsync(this IServiceProvider services)
    {
        var context = services.GetRequiredService<MongoContext>();
        await context.EnsureIndexesAsync();
        await SeedDrivers.ApplyAsync(context);
    }
}


