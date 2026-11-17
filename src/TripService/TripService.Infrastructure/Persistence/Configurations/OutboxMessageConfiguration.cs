using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripService.Infrastructure.Outbox;

namespace TripService.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Type).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(m => m.Payload).IsRequired();
        builder.Property(m => m.AttemptCount).HasDefaultValue(0);
        builder.HasIndex(m => new { m.ProcessedAt, m.ClaimedUntil, m.OccurredAt });
    }
}
