using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentService.Application.Interfaces;
using PaymentService.Application.Services;
using PaymentService.Infrastructure.Persistence;
using PaymentService.Infrastructure.Repositories;

namespace PaymentService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<PayDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("PayDb"))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IPaymentRepository, PaymentRepository>();

        var riderStartingBalance = decimal.TryParse(configuration["Payment:StartingBalance"], out var balance)
            ? balance
            : 500_000m;

        services.AddScoped(sp => new PaymentAppService(
            sp.GetRequiredService<IPaymentRepository>(),
            riderStartingBalance));

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
