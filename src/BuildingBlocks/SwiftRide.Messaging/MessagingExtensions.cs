using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SwiftRide.Messaging;

public static class MessagingExtensions
{
    public static IServiceCollection AddSwiftRideMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? environment = null,
        Action<IBusRegistrationConfigurator>? configureConsumers = null,
        Action<IBusRegistrationContext, IRabbitMqBusFactoryConfigurator>? configureEndpoints = null)
    {
        var isTesting = environment?.IsEnvironment("Testing") == true
            || string.Equals(configuration["ASPNETCORE_ENVIRONMENT"], "Testing", StringComparison.OrdinalIgnoreCase);

        if (!isTesting)
        {
            services.AddOptions<RabbitMqOptions>()
                .Bind((IConfiguration?)configuration.GetSection(RabbitMqOptions.SectionName) ?? new ConfigurationBuilder().Build())
                .Validate(o => !string.IsNullOrWhiteSpace(o.HostName), "RabbitMQ:HostName is required.")
                .Validate(o => o.Port > 0, "RabbitMQ:Port must be > 0.")
                .Validate(o => !string.IsNullOrWhiteSpace(o.UserName), "RabbitMQ:UserName is required.")
                .Validate(o => !string.IsNullOrWhiteSpace(o.Password), "RabbitMQ:Password is required.")
                .Validate(o => o.RetryLimit is >= 0 and <= 10, "RabbitMQ:RetryLimit must be between 0 and 10.")
                .Validate(o => o.MinRetryIntervalMs > 0 && o.MaxRetryIntervalMs >= o.MinRetryIntervalMs && o.RetryIntervalDeltaMs > 0,
                    "RabbitMQ retry intervals must be positive and ordered.")
                .Validate(o => o.PrefetchCount > 0, "RabbitMQ:PrefetchCount must be > 0.")
                .ValidateOnStart();
        }

        services.AddMassTransit(bus =>
        {
            configureConsumers?.Invoke(bus);
            bus.AddConfigureEndpointsCallback((context, _, endpoint) =>
            {
                if (isTesting) return;
                var options = context.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
                if (endpoint is IRabbitMqReceiveEndpointConfigurator rabbitEndpoint)
                {
                    rabbitEndpoint.Durable = true;
                    rabbitEndpoint.PrefetchCount = options.PrefetchCount;
                }

                endpoint.UseMessageRetry(retry =>
                {
                    retry.Ignore<JsonException>();
                    retry.Ignore<ArgumentException>();
                    retry.Ignore<FormatException>();
                    retry.Exponential(options.RetryLimit,
                        TimeSpan.FromMilliseconds(options.MinRetryIntervalMs),
                        TimeSpan.FromMilliseconds(options.MaxRetryIntervalMs),
                        TimeSpan.FromMilliseconds(options.RetryIntervalDeltaMs));
                });
            });

            if (isTesting)
            {
                bus.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
                return;
            }

            bus.UsingRabbitMq((context, cfg) =>
            {
                var options = context.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
                cfg.Host(options.HostName, options.Port, options.VirtualHost, host =>
                {
                    host.Username(options.UserName);
                    host.Password(options.Password);
                    host.PublisherConfirmation = true;
                });

                configureEndpoints?.Invoke(context, cfg);
                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
