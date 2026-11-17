using System.Collections.Concurrent;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using SwiftRide.Contracts.Events;
using SwiftRide.Messaging;

namespace MatchingService.Tests;

public class MessagingCorrelationTests
{
    [Fact]
    public async Task HttpMiddleware_ReturnsRequestCorrelationId()
    {
        var context = new DefaultHttpContext();
        var supplied = Guid.NewGuid();
        context.Request.Headers["X-Correlation-Id"] = supplied.ToString("D");
        var middleware = new CorrelationLoggingMiddleware(_ => Task.CompletedTask,
            NullLogger<CorrelationLoggingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(supplied.ToString("D"), context.Response.Headers["X-Correlation-Id"]);
    }

    [Fact]
    public async Task PublishedEvent_UsesWorkflowIdsInTransportEnvelope()
    {
        using var logs = new CapturedLogs();
        using var host = Host.CreateDefaultBuilder()
            .UseEnvironment("Testing")
            .ConfigureLogging(logging => logging.AddProvider(logs))
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<ReceivedTrip>();
                services.AddSwiftRideMessaging(context.Configuration, context.HostingEnvironment,
                    configureConsumers: bus => bus.AddConsumer<CaptureTripConsumer>()
                        .Endpoint(endpoint => endpoint.Name = "test.trip-accepted"));
            }).Build();
        await host.StartAsync();
        try
        {
            var tripId = Guid.NewGuid();
            var eventId = Guid.NewGuid();
            var message = new TripAccepted(eventId, tripId, null, DateTime.UtcNow, tripId, Guid.NewGuid());
            await host.Services.GetRequiredService<IPublishEndpoint>().Publish(message);
            var received = await host.Services.GetRequiredService<ReceivedTrip>().Value.Task
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(eventId, received.MessageId);
            Assert.Equal(tripId, received.CorrelationId);
            Assert.Equal(eventId, received.EventId);
            var publishLogs = logs.Messages.Where(text => text.StartsWith("Message published.")).ToArray();
            Assert.Single(publishLogs);
            Assert.Contains("Diễn giải: Đã phát tin 'Tài xế đã nhận chuyến", publishLogs[0]);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    public sealed class ReceivedTrip
    {
        public TaskCompletionSource<(Guid? MessageId, Guid? CorrelationId, Guid EventId)> Value { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class CaptureTripConsumer(ReceivedTrip received) : IConsumer<TripAccepted>
    {
        public Task Consume(ConsumeContext<TripAccepted> context)
        {
            received.Value.TrySetResult((context.MessageId, context.CorrelationId, context.Message.EventId));
            return Task.CompletedTask;
        }
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Messages);

        public void Dispose() { }

        private sealed class CaptureLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();
            public void Dispose() { }
        }
    }
}
