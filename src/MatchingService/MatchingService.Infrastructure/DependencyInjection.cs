using SwiftRide.Messaging;
using MatchingService.Application.Interfaces;
using MatchingService.Infrastructure.Mongo;
using MatchingService.Infrastructure.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MatchingService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, Microsoft.Extensions.Hosting.IHostEnvironment? environment = null)
    {
        services.AddSingleton(new MongoContext(configuration.GetConnectionString("MatchDb")!));
        services.AddScoped<IDriverRepository, DriverRepository>();
        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddSwiftRideMessaging(configuration, environment);
        return services;
    }

    // MongoDB khÃ´ng cÃ³ migration: khi khá»Ÿi Ä‘á»™ng thÃ¬ táº¡o index vÃ  seed tÃ i xáº¿ náº¿u collection cÃ²n trá»‘ng.
    public static async Task InitializeDatabaseAsync(this IServiceProvider services)
    {
        var context = services.GetRequiredService<MongoContext>();
        await context.EnsureIndexesAsync();
        await SeedDrivers.ApplyAsync(context);
    }
}


