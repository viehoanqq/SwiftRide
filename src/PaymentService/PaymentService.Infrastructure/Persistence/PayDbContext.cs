using Microsoft.EntityFrameworkCore;
using PaymentService.Application.DTOs;
using PaymentService.Domain.Entities;
using PaymentService.Infrastructure.Messaging;

namespace PaymentService.Infrastructure.Persistence;

public class PayDbContext(DbContextOptions<PayDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<RefundReceipt> RefundReceipts => Set<RefundReceipt>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    public DbSet<FakeCardAuthorization> FakeCardAuthorizations => Set<FakeCardAuthorization>();
    public DbSet<FakeCardRefund> FakeCardRefunds => Set<FakeCardRefund>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PayDbContext).Assembly);
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            modelBuilder.Entity<Account>().Property(a => a.Version).ValueGeneratedNever().IsConcurrencyToken();
        SeedData.Apply(modelBuilder);
    }
}
