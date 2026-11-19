using SwiftRide.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PaymentService.Application.Interfaces;
using PaymentService.Application.Services;
using PaymentService.Infrastructure.Messaging;
using PaymentService.Infrastructure.Persistence;
using PaymentService.Infrastructure.Repositories;
using SwiftRide.Contracts.Events;

namespace PaymentService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, Microsoft.Extensions.Hosting.IHostEnvironment? environment = null)
    {
        var connection = configuration.GetConnectionString("PayDb");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("ConnectionStrings:PayDb is required.");
        services.AddDbContext<PayDbContext>(options =>
            options.UseNpgsql(connection, npgsql => npgsql.CommandTimeout(30))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<AccountRegistrationService>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<ILedgerRepository, LedgerRepository>();
        services.AddScoped<IRefundRepository, RefundRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IPaymentMethodProcessor, WalletPaymentProcessor>();
        if (environment?.IsDevelopment() == true || environment?.IsEnvironment("Testing") == true)
        {
            services.AddScoped<ICardAuthorizationGateway, FakeCardAuthorizationGateway>();
            services.AddScoped<IPaymentMethodProcessor, CardPaymentProcessor>();
        }
        services.AddScoped<IPaymentMethodProcessorFactory, PaymentMethodProcessorFactory>();
        services.AddScoped<PaymentCheckoutService>();
        services.AddScoped<RefundAppService>();
        services.AddScoped<IPaymentCheckoutService>(sp => sp.GetRequiredService<PaymentCheckoutService>());
        services.AddSwiftRideMessaging(configuration, environment,
            configureConsumers: bus =>
            {
                bus.AddConsumer<UserRegisteredConsumer>()
                    .Endpoint(endpoint => endpoint.Name = QueueNames.PaymentUserRegistered);
                // Keep the current dropoff path until Trip starts publishing ProcessPayment.
                bus.AddConsumer<RideCompletedConsumer>()
                    .Endpoint(endpoint => endpoint.Name = QueueNames.PaymentRideCompleted);
                bus.AddConsumer<ProcessPaymentConsumer>()
                    .Endpoint(endpoint => endpoint.Name = QueueNames.PaymentProcessPayment);
                bus.AddConsumer<RefundPaymentConsumer>()
                    .Endpoint(endpoint => endpoint.Name = QueueNames.PaymentRefundPayment);
            });
        if (environment?.IsEnvironment("Testing") != true)
            services.AddHostedService<PaymentOutboxPublisher>();
        return services;
    }

    // Tá»± Ã¡p dá»¥ng migration khi service khá»Ÿi Ä‘á»™ng.
    public static async Task MigrateDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PayDbContext>();
        await db.Database.MigrateAsync();
    }
}


