using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TripService.Infrastructure.Persistence;


using TripService.Application.Interfaces;          // thêm
using TripService.Infrastructure.Auth;             // thêm
using TripService.Infrastructure.Clients;
using TripService.Infrastructure.Repositories; 

namespace TripService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TripDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("TripDb")));
        services.AddScoped<IUserRepository, UserRepository>();     // thêm: dùng DbContext nên Scoped
        services.AddSingleton<IPasswordHasher, PasswordHasher>();  // thêm: không giữ trạng thái nên Singleton   
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<ITripRepository, TripRepository>();     // thêm: dùng DbContext nên Scoped

        services.AddHttpContextAccessor();
        services.AddHttpClient<IMatchingClient, MatchingClient>(client =>
        {
            var baseUrl = configuration["Services:MatchingService"] ?? "http://localhost:5002";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(3);
        });

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
