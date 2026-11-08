using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripService.Domain.Entities;
using TripService.Infrastructure.Outbox;

namespace TripService.Infrastructure.Persistence.Configurations;

public class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> builder)
    {
        builder.ToTable("Trips");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(t => t.RowVersion).IsRowVersion();
        builder.HasIndex(t => new { t.RiderId, t.Status });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.RiderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Location)
            .WithOne()
            .HasForeignKey<TripLocation>(l => l.TripId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Fare)
            .WithOne()
            .HasForeignKey<TripFare>(f => f.TripId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.Offers)
            .WithOne()
            .HasForeignKey(o => o.TripId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.History)
            .WithOne()
            .HasForeignKey(h => h.TripId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TripLocationConfiguration : IEntityTypeConfiguration<TripLocation>
{
    public void Configure(EntityTypeBuilder<TripLocation> builder)
    {
        builder.ToTable("TripLocations");
        builder.HasKey(l => l.TripId);
        builder.Property(l => l.PickupLat).HasPrecision(9, 6);
        builder.Property(l => l.PickupLng).HasPrecision(9, 6);
        builder.Property(l => l.DropoffLat).HasPrecision(9, 6);
        builder.Property(l => l.DropoffLng).HasPrecision(9, 6);
        builder.Property(l => l.PickupAddress).HasMaxLength(255).IsRequired();
        builder.Property(l => l.DropoffAddress).HasMaxLength(255).IsRequired();
    }
}

public class TripFareConfiguration : IEntityTypeConfiguration<TripFare>
{
    public void Configure(EntityTypeBuilder<TripFare> builder)
    {
        builder.ToTable("TripFares");
        builder.HasKey(f => f.TripId);
        builder.Property(f => f.QuoteId).HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(f => f.Price).HasPrecision(18, 2);
        builder.Property(f => f.PaymentMethod).HasConversion<string>().HasMaxLength(10).IsUnicode(false);
        builder.Property(f => f.Status).HasConversion<string>().HasMaxLength(10).IsUnicode(false);
        builder.Property(f => f.Strategy).HasMaxLength(20).IsUnicode(false).IsRequired();   // ← THÊM

    }
}

public class TripDriverOfferConfiguration : IEntityTypeConfiguration<TripDriverOffer>
{
    public void Configure(EntityTypeBuilder<TripDriverOffer> builder)
    {
        builder.ToTable("TripDriverOffers");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(10).IsUnicode(false);
        builder.HasIndex(o => new { o.TripId, o.AttemptNo }).IsUnique();
        builder.HasIndex(o => new { o.DriverId, o.Status });

        builder.HasOne(o => o.Driver)
            .WithMany()
            .HasForeignKey(o => o.DriverId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class TripStatusHistoryConfiguration : IEntityTypeConfiguration<TripStatusHistory>
{
    public void Configure(EntityTypeBuilder<TripStatusHistory> builder)
    {
        builder.ToTable("TripStatusHistory");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.HasIndex(h => h.TripId);
    }
}

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
