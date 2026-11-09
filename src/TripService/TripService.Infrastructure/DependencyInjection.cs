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

        services.AddHttpClient("DriverRegistration", client =>
        {
            client.BaseAddress = matchingServiceUri;
            client.Timeout = TimeSpan.FromSeconds(10);
        }).AddHttpMessageHandler<ServiceAuthorizationHandler>();
        services.AddHostedService<TripService.Infrastructure.Outbox.DriverRegistrationWorker>();
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
