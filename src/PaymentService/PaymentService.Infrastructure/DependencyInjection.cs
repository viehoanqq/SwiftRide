using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentService.Application.Interfaces;
using PaymentService.Infrastructure.Persistence;
using PaymentService.Infrastructure.Repositories;

namespace PaymentService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("PayDb");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("ConnectionStrings:PayDb is required.");
        services.AddDbContext<PayDbContext>(options =>
            options.UseNpgsql(connection, npgsql => npgsql.CommandTimeout(30))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<ILedgerRepository, LedgerRepository>();
        services.AddScoped<IRefundRepository, RefundRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

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
