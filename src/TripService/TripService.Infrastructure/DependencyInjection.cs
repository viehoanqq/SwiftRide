using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TripService.Application.Interfaces;
using TripService.Application.Services;
using TripService.Infrastructure.Auth;
using TripService.Infrastructure.Clients;
using TripService.Infrastructure.Persistence;
using TripService.Infrastructure.Repositories;

namespace TripService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TripDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("TripDb")));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ITripRepository, TripRepository>();
        services.AddScoped<TripAppService>();
        services.AddHttpClient<IMatchingClient, MatchingClient>(client =>
        {
            client.BaseAddress = new Uri(configuration["Matching:BaseUrl"] ?? "http://localhost:5000");
            client.Timeout = TimeSpan.FromSeconds(3);
        });
        services.AddHttpClient<IPaymentClient, PaymentClient>(client =>
        {
            client.BaseAddress = new Uri(configuration["Payment:BaseUrl"] ?? "http://localhost:5003");
            client.Timeout = TimeSpan.FromSeconds(3);
        });
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<ITripRepository, TripRepository>();
        return services;
    }

    // Tự áp dụng migration khi service khởi động, để cả nhóm không phải chạy "dotnet ef database update" thủ công.
    public static async Task MigrateDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        await db.Database.MigrateAsync();
    }
}
