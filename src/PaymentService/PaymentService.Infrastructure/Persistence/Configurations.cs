using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentService.Application.DTOs;
using PaymentService.Domain.Entities;
using PaymentService.Infrastructure.Messaging;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Infrastructure.Persistence;

// Tên bảng / cột tự chuyển sang snake_case (UseSnakeCaseNamingConvention).
// Enum lưu dạng chuỗi in hoa: RIDER, PENDING, CHARGE...
internal static class EnumColumn
{
    public static PropertyBuilder<TEnum> AsUpperString<TEnum>(this PropertyBuilder<TEnum> property, int maxLength)
        where TEnum : struct, Enum =>
        property.HasConversion(v => v.ToString().ToUpperInvariant(), v => Enum.Parse<TEnum>(v, true))
            .HasMaxLength(maxLength);
}

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.OwnerId)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.OwnerType)
            .AsUpperString(10);

        builder.Property(a => a.Balance)
            .HasPrecision(18, 2);

        
        builder.Property(a => a.Version)
            .IsConcurrencyToken();

        builder.HasIndex(a => new { a.OwnerId, a.OwnerType })
            .IsUnique();
    }
}
public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.IdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.Method).AsUpperString(10);
        builder.Property(p => p.CardToken).HasMaxLength(100);
        builder.Property(p => p.Status).AsUpperString(10);
        builder.HasIndex(p => p.TripId).IsUnique();
        builder.HasIndex(p => p.IdempotencyKey).IsUnique();

        builder.HasMany(p => p.Entries)
            .WithOne()
            .HasForeignKey(e => e.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EntryType).AsUpperString(10);
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.HasIndex(e => e.AccountId);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Type).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(m => m.OccurredAt).HasFilter("processed_at IS NULL");
    }
}

public class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.HasKey(m => m.MessageId);
    }
}

public class RefundReceiptConfiguration : IEntityTypeConfiguration<RefundReceipt>
{
    public void Configure(EntityTypeBuilder<RefundReceipt> builder)
    {
        builder.HasKey(r => r.IdempotencyKey);
        builder.Property(r => r.IdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        builder.Property(r => r.Response)
            .HasConversion(
                v => v.Status.ToString().ToUpperInvariant(),
                v => new RefundPaymentResponse(Enum.Parse<PaymentState>(v, true)))
            .HasColumnName("status")
            .HasMaxLength(10)
            .IsRequired();
        builder.HasIndex(r => r.PaymentId).IsUnique();

        builder.HasOne<Payment>()
            .WithOne()
            .HasForeignKey<RefundReceipt>(r => r.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
