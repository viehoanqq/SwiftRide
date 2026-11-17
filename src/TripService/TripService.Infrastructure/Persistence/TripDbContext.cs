using Microsoft.EntityFrameworkCore;
using TripService.Domain.Entities;
using TripService.Infrastructure.Outbox;

namespace TripService.Infrastructure.Persistence;

public class TripDbContext(DbContextOptions<TripDbContext> options) : DbContext(options)
{
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<TripLocation> TripLocations => Set<TripLocation>();
    public DbSet<TripFare> TripFares => Set<TripFare>();
    public DbSet<TripDriverOffer> TripDriverOffers => Set<TripDriverOffer>();
    public DbSet<TripStatusHistory> TripStatusHistory => Set<TripStatusHistory>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedPaymentResultMessage> ProcessedPaymentResults => Set<ProcessedPaymentResultMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TripDbContext).Assembly);
    }
}
