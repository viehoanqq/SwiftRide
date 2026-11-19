using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripService.Application.Sagas;

namespace TripService.Infrastructure.Persistence.Configurations;

public sealed class PaymentSagaConfiguration : IEntityTypeConfiguration<PaymentSagaInstance>
{
    public void Configure(EntityTypeBuilder<PaymentSagaInstance> builder)
    {
        builder.ToTable("PaymentSagas");
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.TripId).IsUnique();
        builder.HasIndex(s => s.CorrelationId).IsUnique();
        builder.Property(s => s.State).HasConversion<string>().HasMaxLength(32).IsUnicode(false);
        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.HasOne<TripService.Domain.Entities.Trip>().WithOne()
            .HasForeignKey<PaymentSagaInstance>(s => s.TripId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
