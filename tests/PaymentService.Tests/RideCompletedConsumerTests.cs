using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;
using PaymentService.Application.Options;
using PaymentService.Application.Exceptions;
using PaymentService.Application.Services;
using PaymentService.Application.Interfaces;
using PaymentService.Application.DTOs;
using PaymentService.Domain.Enums;
using PaymentService.Infrastructure.Messaging;
using PaymentService.Infrastructure.Persistence;
using PaymentService.Infrastructure.Repositories;
using SwiftRide.Contracts.Events;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Tests;

public class RideCompletedConsumerTests
{
    private static readonly Guid DriverId = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly Guid FundedRiderId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid LowBalanceRiderId = Guid.Parse("11111111-0000-0000-0000-000000000002");

    [PostgresFact]
    public async Task ProcessPayment_ReplayKeepsOneChargeAndOneResultWithCommandCausation()
    {
        await using var database = await PaymentConsumerDatabase.CreateAsync();
        var tripId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var command = new ProcessPayment(Guid.NewGuid(), tripId, null, now,
            tripId, FundedRiderId, DriverId, 50_000m, PaymentMethod.WALLET,
            PaymentProtocol.TripPaymentKey(tripId), now.AddMinutes(2));

        await DeliverCommand(database, command);
        await DeliverCommand(database, command);

        await using var verify = database.Context();
        var payment = Assert.Single(await verify.Payments.ToListAsync());
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(3, await verify.LedgerEntries.CountAsync());
        Assert.Single(await verify.ProcessedMessages.ToListAsync());
        var outcome = JsonSerializer.Deserialize<PaymentSettled>(
            Assert.Single(await verify.OutboxMessages.ToListAsync()).Payload)!;
        Assert.Equal(command.EventId, outcome.CausationId);
        Assert.Equal(payment.Id, outcome.PaymentId);
    }

    [PostgresFact]
    public async Task ProcessPayment_InsufficientFundsPreservesFailureAndCheckpoint()
    {
        await using var database = await PaymentConsumerDatabase.CreateAsync();
        var tripId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var command = new ProcessPayment(Guid.NewGuid(), tripId, null, now,
            tripId, LowBalanceRiderId, DriverId, 50_000m, PaymentMethod.WALLET,
            PaymentProtocol.TripPaymentKey(tripId), now.AddMinutes(2));

        await DeliverCommand(database, command);

        await using var verify = database.Context();
        Assert.Equal(PaymentStatus.Failed, Assert.Single(await verify.Payments.ToListAsync()).Status);
        Assert.Empty(await verify.LedgerEntries.ToListAsync());
        Assert.Single(await verify.ProcessedMessages.ToListAsync());
        var outcome = JsonSerializer.Deserialize<PaymentFailed>(
            Assert.Single(await verify.OutboxMessages.ToListAsync()).Payload)!;
        Assert.Equal("INSUFFICIENT_FUNDS", outcome.Reason);
        Assert.Equal(command.EventId, outcome.CausationId);
    }

    [PostgresFact]
    public async Task SuccessfulRide_CommitsSettlementCheckpointAndOutcome_AndReplayDoesNotChargeAgain()
    {
        await using var database = await PaymentConsumerDatabase.CreateAsync();
        var tripId = Guid.NewGuid();
        var ride = new RideCompleted(Guid.NewGuid(), tripId, null, DateTime.UtcNow,
            tripId, FundedRiderId, DriverId, 50_000m, PaymentMethod.WALLET);

        await Deliver(database, ride);
        await Deliver(database, ride);

        await using var verify = database.Context();
        var payment = Assert.Single(await verify.Payments.ToListAsync());
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(3, await verify.LedgerEntries.CountAsync());
        Assert.Equal(450_000m, (await verify.Accounts.SingleAsync(a => a.OwnerId == FundedRiderId.ToString("D"))).Balance);
        Assert.Single(await verify.ProcessedMessages.ToListAsync());
        var outbox = Assert.Single(await verify.OutboxMessages.ToListAsync());
        Assert.Equal(nameof(PaymentSettled), outbox.Type);
        var settled = JsonSerializer.Deserialize<PaymentSettled>(outbox.Payload)!;
        Assert.Equal(ride.EventId, settled.CausationId);
        Assert.Equal(payment.Id, settled.PaymentId);
    }

    [PostgresFact]
    public async Task InsufficientFunds_CommitsFailedPaymentCheckpointAndFailureEvent()
    {
        await using var database = await PaymentConsumerDatabase.CreateAsync();
        var tripId = Guid.NewGuid();
        var ride = new RideCompleted(Guid.NewGuid(), tripId, null, DateTime.UtcNow,
            tripId, LowBalanceRiderId, DriverId, 50_000m, PaymentMethod.WALLET);

        await Deliver(database, ride);
        await Deliver(database, ride);

        await using var verify = database.Context();
        var payment = Assert.Single(await verify.Payments.ToListAsync());
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Empty(await verify.LedgerEntries.ToListAsync());
        Assert.Equal(20_000m, (await verify.Accounts.SingleAsync(a => a.OwnerId == LowBalanceRiderId.ToString("D"))).Balance);
        Assert.Single(await verify.ProcessedMessages.ToListAsync());
        var outbox = Assert.Single(await verify.OutboxMessages.ToListAsync());
        Assert.Equal(nameof(PaymentFailed), outbox.Type);
        var failed = JsonSerializer.Deserialize<PaymentFailed>(outbox.Payload)!;
        Assert.Equal("INSUFFICIENT_FUNDS", failed.Reason);
        Assert.Equal(ride.EventId, failed.CausationId);
    }

    [PostgresFact]
    public async Task UnknownCard_CommitsPendingCheckpointWithoutPublishingFinalEvent()
    {
        await using var database = await PaymentConsumerDatabase.CreateAsync();
        var tripId = Guid.NewGuid();
        var ride = new RideCompleted(Guid.NewGuid(), tripId, null, DateTime.UtcNow,
            tripId, FundedRiderId, DriverId, 50_000m, PaymentMethod.CARD, FakeCardTokens.Unknown);

        async Task DeliverCard()
        {
            await using var db = database.Context();
            var uow = new UnitOfWork(db);
            var accounts = new AccountRepository(db);
            var options = new PaymentOptions { DriverRate = 0.8m, FakeCard = new FakeCardOptions { Enabled = true } };
            IPaymentMethodProcessorFactory factory = new PaymentMethodProcessorFactory(
            [
                new WalletPaymentProcessor(accounts, options),
                new CardPaymentProcessor(accounts, new FakeCardAuthorizationGateway(db), options)
            ]);
            var checkout = new PaymentCheckoutService(new PaymentRepository(db), accounts, uow, options, factory);
            var consumer = new RideCompletedConsumer(checkout, uow, db);
            var context = new Mock<ConsumeContext<RideCompleted>>();
            context.SetupGet(c => c.Message).Returns(ride);
            context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
            await consumer.Consume(context.Object);
        }

        await DeliverCard();
        await DeliverCard();

        await using var verify = database.Context();
        var payment = Assert.Single(await verify.Payments.ToListAsync());
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(1, payment.AttemptCount);
        Assert.Single(await verify.ProcessedMessages.ToListAsync());
        Assert.Empty(await verify.OutboxMessages.ToListAsync());
        Assert.Empty(await verify.LedgerEntries.ToListAsync());
    }

    [PostgresFact]
    public async Task MissingDriverAccount_RollsBackPaymentCheckpointAndOutcome()
    {
        await using var database = await PaymentConsumerDatabase.CreateAsync();
        var tripId = Guid.NewGuid();
        var ride = new RideCompleted(Guid.NewGuid(), tripId, null, DateTime.UtcNow,
            tripId, FundedRiderId, Guid.NewGuid(), 50_000m, PaymentMethod.WALLET);

        await Assert.ThrowsAsync<NotFoundException>(() => Deliver(database, ride));

        await using var verify = database.Context();
        Assert.Empty(await verify.Payments.ToListAsync());
        Assert.Empty(await verify.LedgerEntries.ToListAsync());
        Assert.Empty(await verify.ProcessedMessages.ToListAsync());
        Assert.Empty(await verify.OutboxMessages.ToListAsync());
        Assert.Equal(500_000m, (await verify.Accounts.SingleAsync(a => a.OwnerId == FundedRiderId.ToString("D"))).Balance);
    }

    private static async Task Deliver(PaymentConsumerDatabase database, RideCompleted ride)
    {
        await using var db = database.Context();
        var uow = new UnitOfWork(db);
        var checkout = new PaymentCheckoutService(new PaymentRepository(db), new AccountRepository(db), uow,
            new PaymentOptions { DriverRate = 0.8m });
        var consumer = new RideCompletedConsumer(checkout, uow, db);
        var context = new Mock<ConsumeContext<RideCompleted>>();
        context.SetupGet(c => c.Message).Returns(ride);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        await consumer.Consume(context.Object);
    }

    private static async Task DeliverCommand(PaymentConsumerDatabase database, ProcessPayment command)
    {
        await using var db = database.Context();
        var uow = new UnitOfWork(db);
        var checkout = new PaymentCheckoutService(new PaymentRepository(db), new AccountRepository(db), uow,
            new PaymentOptions { DriverRate = 0.8m });
        var consumer = new ProcessPaymentConsumer(checkout, uow, db);
        var context = new Mock<ConsumeContext<ProcessPayment>>();
        context.SetupGet(c => c.Message).Returns(command);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        await consumer.Consume(context.Object);
    }
}
