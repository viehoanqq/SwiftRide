using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using PaymentService.Application.DTOs;
using PaymentService.Application.Interfaces;
using PaymentService.Application.Exceptions;
using PaymentService.Application.Options;
using PaymentService.Application.Services;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Infrastructure;
using PaymentService.Infrastructure.Persistence;
using PaymentService.Infrastructure.Repositories;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Tests;

public class PaymentInfrastructureTests
{
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Testing", true)]
    [InlineData("Production", false)]
    public void FakeCardProcessor_IsRegisteredOnlyInAllowedEnvironments(string environmentName, bool available)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:PayDb"] = "Host=localhost;Port=5433;Database=PayDb;Username=swiftride;Password=swiftride"
        }).Build();
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(environmentName);
        var services = new ServiceCollection();
        services.AddSingleton(new PaymentOptions
        {
            DriverRate = 0.8m,
            FakeCard = new FakeCardOptions { Enabled = true }
        });
        services.AddInfrastructure(configuration, environment.Object);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IPaymentMethodProcessorFactory>();

        if (available)
            Assert.IsType<CardPaymentProcessor>(factory.For(PaymentMethod.CARD));
        else
            Assert.Throws<ValidationException>(() => factory.For(PaymentMethod.CARD));
    }

    private static PayDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PayDbContext>()
            .UseNpgsql("Host=localhost;Port=5433;Database=PayDb;Username=swiftride;Password=swiftride")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new PayDbContext(options);
    }

    [Fact]
    public void AddInfrastructure_RegistersAllRepositoriesAndUnitOfWorkAsScoped()
    {
        var connSection = new Mock<IConfigurationSection>();
        connSection.Setup(s => s["PayDb"])
            .Returns("Host=localhost;Port=5433;Database=PayDb;Username=swiftride;Password=swiftride");
        var config = new Mock<IConfiguration>();
        config.Setup(c => c.GetSection("ConnectionStrings")).Returns(connSection.Object);

        var services = new ServiceCollection();
        services.AddInfrastructure(config.Object);
        using var provider = services.BuildServiceProvider();

        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        Assert.IsType<AccountRepository>(scope1.ServiceProvider.GetRequiredService<IAccountRepository>());
        Assert.IsType<PaymentRepository>(scope1.ServiceProvider.GetRequiredService<IPaymentRepository>());
        Assert.IsType<LedgerRepository>(scope1.ServiceProvider.GetRequiredService<ILedgerRepository>());
        Assert.IsType<RefundRepository>(scope1.ServiceProvider.GetRequiredService<IRefundRepository>());
        Assert.IsType<UnitOfWork>(scope1.ServiceProvider.GetRequiredService<IUnitOfWork>());

        Assert.Same(
            scope1.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            scope1.ServiceProvider.GetRequiredService<IUnitOfWork>());
        Assert.NotSame(
            scope1.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            scope2.ServiceProvider.GetRequiredService<IUnitOfWork>());
    }

    [Fact]
    public void PayDbContextModel_ConfiguresUniqueConstraintsConcurrencyAndRefundReceipts()
    {
        using var db = CreateContext();
        Assert.False(db.Database.HasPendingModelChanges());
        var model = db.Model;

        var accountEntity = model.FindEntityType(typeof(Account))!;
        Assert.Equal("accounts", accountEntity.GetTableName());
        var accountOwnerIndex = accountEntity.GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual(["OwnerId", "OwnerType"]));
        Assert.True(accountOwnerIndex.IsUnique);
        var versionProp = accountEntity.FindProperty(nameof(Account.Version))!;
        Assert.True(versionProp.IsConcurrencyToken);
        Assert.Equal("xmin", versionProp.GetColumnName());

        var paymentEntity = model.FindEntityType(typeof(Payment))!;
        Assert.Equal("payments", paymentEntity.GetTableName());
        var idempotencyProp = paymentEntity.FindProperty(nameof(Payment.IdempotencyKey))!;
        Assert.Equal(100, idempotencyProp.GetMaxLength());
        Assert.True(paymentEntity.GetIndexes().Single(i => i.Properties.Single().Name == nameof(Payment.IdempotencyKey)).IsUnique);
        Assert.True(paymentEntity.GetIndexes().Single(i => i.Properties.Single().Name == nameof(Payment.TripId)).IsUnique);

        var receiptEntity = model.FindEntityType(typeof(RefundReceipt))!;
        Assert.Equal("refund_receipts", receiptEntity.GetTableName());
        Assert.Equal(nameof(RefundReceipt.IdempotencyKey), receiptEntity.FindPrimaryKey()!.Properties.Single().Name);
        Assert.Equal(100, receiptEntity.FindProperty(nameof(RefundReceipt.IdempotencyKey))!.GetMaxLength());
        Assert.Equal(500, receiptEntity.FindProperty(nameof(RefundReceipt.Reason))!.GetMaxLength());
        Assert.True(receiptEntity.GetIndexes().Single(i => i.Properties.Single().Name == nameof(RefundReceipt.PaymentId)).IsUnique);

        var fk = Assert.Single(receiptEntity.GetForeignKeys());
        Assert.Equal(typeof(Payment), fk.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);

        var responseProp = receiptEntity.FindProperty(nameof(RefundReceipt.Response))!;
        Assert.Equal("status", responseProp.GetColumnName());
        Assert.Equal(10, responseProp.GetMaxLength());
        var converter = responseProp.GetValueConverter()!;
        Assert.Equal("REFUNDED", converter.ConvertToProvider(new RefundPaymentResponse(PaymentState.REFUNDED)));
        var roundTrip = Assert.IsType<RefundPaymentResponse>(converter.ConvertFromProvider("REFUNDED"));
        Assert.Equal(PaymentState.REFUNDED, roundTrip.Status);
    }

    [Fact]
    public async Task AccountRepository_GetForUpdateAsync_RequiresActiveTransaction()
    {
        using var db = CreateContext();
        var repo = new AccountRepository(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.GetForUpdateAsync([Guid.NewGuid()], CancellationToken.None));
    }

    [Fact]
    public async Task UnitOfWork_ExecuteAsync_RejectsNullOperation()
    {
        using var db = CreateContext();
        var uow = new UnitOfWork(db);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            uow.ExecuteAsync<int>(null!, CancellationToken.None));
    }
}
