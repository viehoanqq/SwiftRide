using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IdentityService.Infrastructure.Outbox;
namespace IdentityService.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Type).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(m => m.Payload).IsRequired();
        // Worker chỉ quét các message chưa xử lý.
        builder.HasIndex(m => m.OccurredAt).HasFilter("[ProcessedAt] IS NULL");
    }
}
