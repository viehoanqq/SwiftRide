using MatchingService.Infrastructure.Mongo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MatchingService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(new MongoContext(configuration.GetConnectionString("MatchDb")!));

        return services;
    }

    // MongoDB không có migration: khi khởi động thì tạo index và seed tài xế nếu collection còn trống.
    public static async Task InitializeDatabaseAsync(this IServiceProvider services)
    {
        var context = services.GetRequiredService<MongoContext>();
        await context.EnsureIndexesAsync();
        await SeedDrivers.ApplyAsync(context);
    }
}
