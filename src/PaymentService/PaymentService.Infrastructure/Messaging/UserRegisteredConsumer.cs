using MassTransit;
using PaymentService.Application.Services;
using SwiftRide.Contracts.Events;

namespace PaymentService.Infrastructure.Messaging;

public sealed class UserRegisteredConsumer(AccountRegistrationService accounts) : IConsumer<UserRegistered>
{
    public Task Consume(ConsumeContext<UserRegistered> context) =>
        accounts.RegisterAsync(context.Message.UserId, context.Message, context.CancellationToken);
}
