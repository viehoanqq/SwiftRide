using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TripService.Infrastructure.Persistence.Configurations;

public sealed class ProcessedPaymentResultConfiguration : IEntityTypeConfiguration<ProcessedPaymentResultMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedPaymentResultMessage> builder)
    {
        builder.ToTable("ProcessedPaymentResults");
        builder.HasKey(m => m.EventId);
        builder.Property(m => m.EventType).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(m => m.PayloadHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.HasIndex(m => m.TripId);
    }
}
