using Microsoft.EntityFrameworkCore;
using PaymentService.Application.DTOs;
using PaymentService.Application.Exceptions;
using PaymentService.Application.Interfaces;
using PaymentService.Application.Options;
using PaymentService.Application.Services;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Infrastructure.Messaging;
using PaymentService.Infrastructure.Persistence;
using PaymentService.Infrastructure.Repositories;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Tests;

public sealed class FakeCardCheckoutTests
{
    private static readonly Guid RiderId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid DriverId = Guid.Parse("22222222-0000-0000-0000-000000000001");

    private static PaymentCheckoutService Checkout(PayDbContext db, bool enabled = true)
    {
        var accounts = new AccountRepository(db);
        var options = new PaymentOptions
        {
            DriverRate = 0.75m,
            FakeCard = new FakeCardOptions { Enabled = enabled }
        };
        IPaymentMethodProcessorFactory factory = new PaymentMethodProcessorFactory(
        [
            new WalletPaymentProcessor(accounts, options),
            new CardPaymentProcessor(accounts, new FakeCardAuthorizationGateway(db), options)
        ]);
        return new PaymentCheckoutService(new PaymentRepository(db), accounts, new UnitOfWork(db), options, factory);
    }

    private static CreatePaymentRequest Request(string token) => new(Guid.NewGuid(), RiderId, DriverId,
        50_000m, PaymentMethod.CARD, Guid.NewGuid().ToString("D"), token);

    [PostgresFact]
    public async Task Gateway_AuthorizationLookupAndRefundAreIdempotentAcrossContexts()
    {
        await using var database = await PaymentConsumerDatabase.CreateAsync();
        const string authorizationKey = "gateway-auth-1";
        const string refundKey = "gateway-refund-1";
        await using (var db = database.Context())
        {
            var gateway = new FakeCardAuthorizationGateway(db);
            await new UnitOfWork(db).ExecuteAsync(async ct =>
                await gateway.AuthorizeAsync(FakeCardTokens.Approved, authorizationKey, 50_000m, ct),
                CancellationToken.None);
        }
        await using (var db = database.Context())
        {
            var gateway = new FakeCardAuthorizationGateway(db);
            Assert.Equal(CardAuthorizationResult.Approved,
                await gateway.FindAuthorizationAsync(authorizationKey, CancellationToken.None));
            await new UnitOfWork(db).ExecuteAsync(async ct =>
            {
                Assert.Equal(CardAuthorizationResult.Approved,
                    await gateway.AuthorizeAsync(FakeCardTokens.Approved, authorizationKey, 50_000m, ct));
                await gateway.RefundAsync(authorizationKey, refundKey, 50_000m, ct);
                return 0;
            }, CancellationToken.None);
        }
        await using (var db = database.Context())
        {
            var gateway = new FakeCardAuthorizationGateway(db);
            await new UnitOfWork(db).ExecuteAsync(async ct =>
            {
                await gateway.RefundAsync(authorizationKey, refundKey, 50_000m, ct);
                await Assert.ThrowsAsync<ConflictException>(() =>
                    gateway.AuthorizeAsync(FakeCardTokens.Declined, authorizationKey, 50_000m, ct));
                await Assert.ThrowsAsync<ConflictException>(() =>
                    gateway.RefundAsync(authorizationKey, "different-refund-key", 50_000m, ct));
                return 0;
            }, CancellationToken.None);
            Assert.Single(await db.FakeCardAuthorizations.ToListAsync());
            Assert.Single(await db.FakeCardRefunds.ToListAsync());
        }
    }

    [PostgresFact]
    public async Task ApprovedCard_UsesExistingSettlementAndRefund_AndReplayIsIdempotent()
    {
        await using var database = await PaymentConsumerDatabase.CreateAsync();
        var request = Request(FakeCardTokens.Approved);
        Guid paymentId;
        await using (var db = database.Context())
        {
            var first = await Checkout(db).CreateAsync(request, CancellationToken.None);
            paymentId = first.Response.PaymentId;
            Assert.Equal(PaymentState.SUCCEEDED, first.Response.Status);
            Assert.False(first.IsReplay);
        }
        await using (var db = database.Context())
        {
            var replay = await Checkout(db).CreateAsync(request, CancellationToken.None);
            Assert.True(replay.IsReplay);
            Assert.Equal(paymentId, replay.Response.PaymentId);
            Assert.Equal(CardAuthorizationResult.Approved,
                await new FakeCardAuthorizationGateway(db).FindAuthorizationAsync(
                    request.IdempotencyKey, CancellationToken.None));
            Assert.Single(await db.FakeCardAuthorizations.ToListAsync());
            var conflict = await Assert.ThrowsAsync<ConflictException>(() =>
                Checkout(db).CreateAsync(request with { CardToken = FakeCardTokens.Declined }, CancellationToken.None));
            Assert.Equal(PaymentErrorCodes.IdempotencyConflict, conflict.Code);
        }
        await using (var db = database.Context())
        {
            var payment = await db.Payments.Include(p => p.Entries).SingleAsync();
            Assert.Equal(PaymentMethodType.Card, payment.Method);
            Assert.Equal(FakeCardTokens.Approved, payment.CardToken);
            Assert.Equal(1, payment.AttemptCount);
            Assert.Equal(3, payment.Entries.Count);
            Assert.Equal(-50_000m, payment.Entries.Single(e => e.Amount < 0).Amount);
            Assert.Equal(37_500m, payment.Entries.Single(e => e.Amount == 37_500m).Amount);
            Assert.Equal(12_500m, payment.Entries.Single(e => e.Amount == 12_500m).Amount);
            Assert.Equal(500_000m, (await db.Accounts.SingleAsync(a => a.OwnerId == RiderId.ToString("D"))).Balance);
            Assert.Equal(-50_000m, (await db.Accounts.SingleAsync(a => a.OwnerId == Account.GatewayOwnerId)).Balance);

            var refund = new RefundAppService(new PaymentRepository(db), new RefundRepository(db),
                new AccountRepository(db), new UnitOfWork(db), new FakeCardAuthorizationGateway(db));
            Assert.Equal(PaymentState.REFUNDED,
                (await refund.RefundAsync(paymentId, new RefundPaymentRequest("test refund"),
                    "card-refund-1", CancellationToken.None)).Status);
            Assert.Equal(PaymentState.REFUNDED,
                (await refund.RefundAsync(paymentId, new RefundPaymentRequest("test refund"),
                    "card-refund-1", CancellationToken.None)).Status);
        }
        await using (var db = database.Context())
        {
            var payment = await db.Payments.Include(p => p.Entries).SingleAsync();
            Assert.Equal(PaymentStatus.Refunded, payment.Status);
            Assert.Equal(6, payment.Entries.Count);
            Assert.All(payment.Entries.GroupBy(e => e.AccountId), entries => Assert.Equal(0m, entries.Sum(e => e.Amount)));
            Assert.Equal(0m, (await db.Accounts.SingleAsync(a => a.OwnerId == Account.GatewayOwnerId)).Balance);
            Assert.Single(await db.RefundReceipts.ToListAsync());
            Assert.Single(await db.FakeCardRefunds.ToListAsync());
        }
    }

    [PostgresFact]
    public async Task DeclinedCard_PersistsFailedPaymentWithoutBalanceOrLedgerChanges()
    {
        await using var database = await PaymentConsumerDatabase.CreateAsync();
        var request = Request(FakeCardTokens.Declined);
        await using (var db = database.Context())
        {
            var result = await Checkout(db).CreateAsync(request, CancellationToken.None);
            Assert.Equal(PaymentState.FAILED, result.Response.Status);
        }
        await using var verify = database.Context();
        var payment = await verify.Payments.SingleAsync();
        Assert.Equal("CARD_DECLINED", payment.FailureReason);
        Assert.Equal(1, payment.AttemptCount);
        Assert.Empty(await verify.LedgerEntries.ToListAsync());
        Assert.Equal(0m, (await verify.Accounts.SingleAsync(a => a.OwnerId == Account.GatewayOwnerId)).Balance);
    }

    [PostgresFact]
    public async Task UnknownCard_StaysPendingAndReservesIdempotencyKeyWithoutLedger()
    {
        await using var database = await PaymentConsumerDatabase.CreateAsync();
        var request = Request(FakeCardTokens.Unknown);
        await using (var db = database.Context())
        {
            var result = await Checkout(db).CreateAsync(request, CancellationToken.None);
            Assert.Equal(PaymentState.PENDING, result.Response.Status);
        }
        await using var verify = database.Context();
        var payment = Assert.Single(await verify.Payments.ToListAsync());
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(CardAuthorizationResult.Unknown,
            await new FakeCardAuthorizationGateway(verify).FindAuthorizationAsync(
                request.IdempotencyKey, CancellationToken.None));
        Assert.Equal(1, payment.AttemptCount);
        Assert.Empty(await verify.LedgerEntries.ToListAsync());
        Assert.Equal(0m, (await verify.Accounts.SingleAsync(a => a.OwnerId == Account.GatewayOwnerId)).Balance);
        var replay = await Checkout(verify).CreateAsync(request, CancellationToken.None);
        Assert.True(replay.IsReplay);
        Assert.Equal(payment.Id, replay.Response.PaymentId);
        var conflict = await Assert.ThrowsAsync<ConflictException>(() =>
            Checkout(verify).CreateAsync(request with { CardToken = FakeCardTokens.Approved }, CancellationToken.None));
        Assert.Equal(PaymentErrorCodes.IdempotencyConflict, conflict.Code);
    }

    [PostgresFact]
    public async Task DisabledCard_IsRejectedBeforeAnyPaymentIsSaved()
    {
        await using var database = await PaymentConsumerDatabase.CreateAsync();
        await using (var db = database.Context())
            await Assert.ThrowsAsync<ValidationException>(() =>
                Checkout(db, enabled: false).CreateAsync(Request(FakeCardTokens.Approved), CancellationToken.None));
        await using var verify = database.Context();
        Assert.Empty(await verify.Payments.ToListAsync());
    }
}
