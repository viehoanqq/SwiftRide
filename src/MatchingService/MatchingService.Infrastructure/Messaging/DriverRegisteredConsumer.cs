using MassTransit;
using MatchingService.Application.Services;
using SwiftRide.Contracts.Events;

namespace MatchingService.Infrastructure.Messaging;

public sealed class DriverRegisteredConsumer(DriverAppService drivers) : IConsumer<DriverRegistered>
{
    public Task Consume(ConsumeContext<DriverRegistered> context) =>
        drivers.RegisterAsync(context.Message.DriverId, context.Message, context.CancellationToken);
}
