using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;
using PaymentService.Application.DTOs;
using PaymentService.Application.Options;
using PaymentService.Application.Services;
using PaymentService.Domain.Enums;
using PaymentService.Infrastructure.Messaging;
using PaymentService.Infrastructure.Repositories;
using SwiftRide.Contracts.Events;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Tests;

public sealed class RefundPaymentConsumerTests
{
    private static readonly Guid RiderId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid DriverId = Guid.Parse("22222222-0000-0000-0000-000000000001");

    [PostgresFact]
    public async Task RefundCommand_CommitsLedgerReceiptCheckpointAndOutbox_OnlyOnce()
    {
        await using var database = await PaymentConsumerDatabase.CreateAsync();
        var command = await CreateCommandAsync(database);

        await DeliverAsync(database, command);
        await DeliverAsync(database, command);

        await using var verify = database.Context();
        var payment = await verify.Payments.Include(p => p.Entries).SingleAsync();
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(6, payment.Entries.Count);
        Assert.All(payment.Entries.GroupBy(e => e.AccountId), entries =>
            Assert.Equal(0m, entries.Sum(e => e.Amount)));
        Assert.Single(await verify.RefundReceipts.ToListAsync());
        Assert.Single(await verify.ProcessedMessages.ToListAsync());
        var outcome = JsonSerializer.Deserialize<PaymentRefunded>(
            Assert.Single(await verify.OutboxMessages.ToListAsync()).Payload)!;
        Assert.Equal(command.EventId, outcome.CausationId);
        Assert.Equal(command.PaymentId, outcome.PaymentId);
    }

    [PostgresFact]
    public async Task InsufficientRecipientFunds_RecordsDefinitiveFailureWithoutRefund()
    {
        await using var database = await PaymentConsumerDatabase.CreateAsync();
        var command = await CreateCommandAsync(database);
        await using (var db = database.Context())
        {
            var driver = await db.Accounts.SingleAsync(a => a.OwnerId == DriverId.ToString("D"));
            driver.Balance = 0;
            await db.SaveChangesAsync();
        }

        await DeliverAsync(database, command);
        await DeliverAsync(database, command);

        await using var verify = database.Context();
        var payment = await verify.Payments.Include(p => p.Entries).SingleAsync();
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(3, payment.Entries.Count);
        Assert.Empty(await verify.RefundReceipts.ToListAsync());
        Assert.Single(await verify.ProcessedMessages.ToListAsync());
        var outcome = JsonSerializer.Deserialize<PaymentRefundFailed>(
            Assert.Single(await verify.OutboxMessages.ToListAsync()).Payload)!;
        Assert.Equal(SagaRefundFailureCodes.InsufficientRecipientFunds, outcome.FailureCode);
        Assert.Equal(command.EventId, outcome.CausationId);
    }

    [PostgresFact]
    public async Task WrongTripOrAmount_RecordsMismatchAndDoesNotRefund()
    {
        await using var database = await PaymentConsumerDatabase.CreateAsync();
        var command = await CreateCommandAsync(database);
        command = command with { Amount = command.Amount + 1m };

        await DeliverAsync(database, command);

        await using var verify = database.Context();
        Assert.Equal(PaymentStatus.Succeeded, (await verify.Payments.SingleAsync()).Status);
        Assert.Empty(await verify.RefundReceipts.ToListAsync());
        var outcome = JsonSerializer.Deserialize<PaymentRefundFailed>(
            Assert.Single(await verify.OutboxMessages.ToListAsync()).Payload)!;
        Assert.Equal(SagaRefundFailureCodes.PaymentMismatch, outcome.FailureCode);
    }

    [PostgresFact]
    public async Task ApprovedCard_RefundsGatewayAndLedgerInSameTransaction()
    {
        await using var database = await PaymentConsumerDatabase.CreateAsync();
        var command = await CreateCommandAsync(database, card: true);

        await DeliverAsync(database, command, card: true);
        await DeliverAsync(database, command, card: true);

        await using var verify = database.Context();
        Assert.Equal(PaymentStatus.Refunded, (await verify.Payments.SingleAsync()).Status);
        Assert.Equal(6, await verify.LedgerEntries.CountAsync());
        Assert.Single(await verify.FakeCardRefunds.ToListAsync());
        Assert.Single(await verify.RefundReceipts.ToListAsync());
        Assert.Single(await verify.ProcessedMessages.ToListAsync());
        Assert.Equal(nameof(PaymentRefunded),
            (await verify.OutboxMessages.SingleAsync()).Type);
    }

    private static async Task<RefundPayment> CreateCommandAsync(PaymentConsumerDatabase database,
        bool card = false)
    {
        var tripId = Guid.NewGuid();
        const decimal amount = 50_000m;
        Guid paymentId;
        await using (var db = database.Context())
        {
            var uow = new UnitOfWork(db);
            var accounts = new AccountRepository(db);
            var options = new PaymentOptions { DriverRate = 0.8m,
                FakeCard = new FakeCardOptions { Enabled = card } };
            var factory = new PaymentMethodProcessorFactory(card
                ? [new WalletPaymentProcessor(accounts, options),
                   new CardPaymentProcessor(accounts, new FakeCardAuthorizationGateway(db), options)]
                : [new WalletPaymentProcessor(accounts, options)]);
            var checkout = new PaymentCheckoutService(new PaymentRepository(db),
                accounts, uow, options, factory);
            paymentId = (await checkout.CreateAsync(new CreatePaymentRequest(tripId, RiderId,
                DriverId, amount, card ? PaymentMethod.CARD : PaymentMethod.WALLET,
                PaymentProtocol.TripPaymentKey(tripId), card ? FakeCardTokens.Approved : null),
                CancellationToken.None)).Response.PaymentId;
        }
        return new RefundPayment(Guid.NewGuid(), tripId, null, DateTime.UtcNow,
            tripId, paymentId, amount, SagaRefundReasonCodes.ApprovedCustomerRefund,
            PaymentProtocol.TripRefundKey(tripId));
    }

    private static async Task DeliverAsync(PaymentConsumerDatabase database, RefundPayment command,
        bool card = false)
    {
        await using var db = database.Context();
        var uow = new UnitOfWork(db);
        var refunds = new RefundRepository(db);
        var service = new RefundAppService(new PaymentRepository(db), refunds,
            new AccountRepository(db), uow, card ? new FakeCardAuthorizationGateway(db) : null);
        var consumer = new RefundPaymentConsumer(service, refunds, uow, db);
        var context = new Mock<ConsumeContext<RefundPayment>>();
        context.SetupGet(c => c.Message).Returns(command);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        await consumer.Consume(context.Object);
    }
}
