using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentService.Infrastructure.Persistence;

namespace PaymentService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<PayDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("PayDb"))
                .UseSnakeCaseNamingConvention());

        return services;
    }

    // Tự áp dụng migration khi service khởi động.
    public static async Task MigrateDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PayDbContext>();
        await db.Database.MigrateAsync();
    }
}
