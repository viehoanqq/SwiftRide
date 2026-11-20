using SwiftRide.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TripService.Infrastructure.Persistence;


using TripService.Application.Interfaces;          // thÃªm
using TripService.Infrastructure.Clients;
using TripService.Infrastructure.Messaging;
using TripService.Infrastructure.Outbox;
using TripService.Infrastructure.Repositories; 
using TripService.Application.Services;
using TripService.Application.Sagas;
using TripService.Infrastructure.Sagas;
using SwiftRide.Contracts.Events;

namespace TripService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, Microsoft.Extensions.Hosting.IHostEnvironment? environment = null)
    {
        services.AddDbContext<TripDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("TripDb")));
        services.AddScoped<ITripRepository, TripRepository>();     // thÃªm: dÃ¹ng DbContext nÃªn Scoped
        services.AddScoped<IPaymentSagaRepository, PaymentSagaRepository>();
        services.AddScoped<PaymentSagaTimeoutDispatcher>();
        var timeoutPollSeconds = configuration.GetValue("PaymentSaga:TimeoutPollSeconds", 5);
        var timeoutBatchSize = configuration.GetValue("PaymentSaga:TimeoutBatchSize", 50);
        if (timeoutPollSeconds is < 1 or > 300 || timeoutBatchSize is < 1 or > 500)
            throw new InvalidOperationException("Payment Saga timeout poll seconds must be 1-300 and batch size 1-500.");
        services.AddSingleton(new PaymentSagaTimeoutOptions(TimeSpan.FromSeconds(timeoutPollSeconds), timeoutBatchSize));
        services.AddScoped<TripPaymentResultHandler>();
        services.AddScoped<TripQuoteResultHandler>();
        services.AddScoped<TripRefundCoordinator>();
        services.AddScoped<TripRefundResultHandler>();

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

        var paymentServiceUrl = configuration["Services:PaymentService"];
        if (!Uri.TryCreate(paymentServiceUrl, UriKind.Absolute, out var paymentServiceUri) ||
            (paymentServiceUri.Scheme != Uri.UriSchemeHttp && paymentServiceUri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Services:PaymentService must be an absolute HTTP or HTTPS URL.");
        services.AddHttpClient<IPaymentClient, PaymentClient>(client =>
        {
            client.BaseAddress = paymentServiceUri;
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).AddHttpMessageHandler<ServiceAuthorizationHandler>();

        services.AddSwiftRideMessaging(configuration, environment,
            configureConsumers: bus =>
            {
                bus.AddConsumer<QuoteCalculatedConsumer>()
                    .Endpoint(endpoint => endpoint.Name = QueueNames.TripQuoteCalculated);
                bus.AddConsumer<QuoteFailedConsumer>()
                    .Endpoint(endpoint => endpoint.Name = QueueNames.TripQuoteFailed);
                bus.AddConsumer<PaymentSettledConsumer>()
                    .Endpoint(endpoint => endpoint.Name = QueueNames.TripPaymentSettled);
                bus.AddConsumer<PaymentFailedConsumer>()
                    .Endpoint(endpoint => endpoint.Name = QueueNames.TripPaymentFailed);
                bus.AddConsumer<ProcessPaymentTimedOutConsumer>()
                    .Endpoint(endpoint => endpoint.Name = QueueNames.TripProcessPaymentTimedOut);
                bus.AddConsumer<PaymentRefundedConsumer>()
                    .Endpoint(endpoint => endpoint.Name = QueueNames.TripPaymentRefunded);
                bus.AddConsumer<PaymentRefundFailedConsumer>()
                    .Endpoint(endpoint => endpoint.Name = QueueNames.TripPaymentRefundFailed);
            });
        if (environment?.IsEnvironment("Testing") != true)
        {
            services.AddHostedService<TripOutboxPublisher>();
            services.AddHostedService<PaymentSagaTimeoutScheduler>();
            services.AddHostedService<PaymentSagaReconciler>();
        }
        return services;
    }

    // Tá»± Ã¡p dá»¥ng migration khi service khá»Ÿi Ä‘á»™ng, Ä‘á»ƒ cáº£ nhÃ³m khÃ´ng pháº£i cháº¡y "dotnet ef database update" thá»§ cÃ´ng.
    public static async Task MigrateDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        await db.Database.MigrateAsync();
    }
}


