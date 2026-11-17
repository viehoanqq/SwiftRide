using MatchingService.Application.Interfaces;
using MatchingService.Domain.Entities;
using MatchingService.Domain.Enums;
using MatchingService.Infrastructure.Mongo;
using MongoDB.Driver;
using MongoDB.Driver.GeoJsonObjectModel;

namespace MatchingService.Infrastructure.Repositories;

public class DriverRepository(MongoContext context) : IDriverRepository
{
    public async Task RegisterAsync(string driverId, string name, CancellationToken cancellationToken)
    {
        // Insert-only registration: delayed/repeated deliveries must not reset an online driver.
        var update = Builders<DriverDocument>.Update
            .SetOnInsert(d => d.Name, name)
            .SetOnInsert(d => d.Status, DriverStatus.Offline)
            .SetOnInsert(d => d.UpdatedAt, DateTime.UtcNow);
        try
        {
            await context.Drivers.UpdateOneAsync(d => d.Id == driverId, update,
                new UpdateOptions { IsUpsert = true }, cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Another registration/online request inserted the same _id concurrently.
            if (!await context.Drivers.Find(d => d.Id == driverId).AnyAsync(cancellationToken))
                throw;
        }
    }

    public async Task<Driver?> GetByIdAsync(string driverId)
    {
        var doc = await context.Drivers.Find(d => d.Id == driverId).FirstOrDefaultAsync();
        return doc is null ? null : ToEntity(doc);
    }

    public async Task UpsertAsync(Driver driver)
    {
        var doc = new DriverDocument
        {
            Id = driver.Id,
            Name = driver.Name,
            Status = driver.Status,
            Location = driver.Location is null ? null :
                GeoJson.Point(GeoJson.Geographic(driver.Location.Lng, driver.Location.Lat)),
            UpdatedAt = driver.UpdatedAt
        };

        await context.Drivers.UpdateOneAsync(
            d => d.Id == driver.Id,
            Builders<DriverDocument>.Update
                .Set(d => d.Name, doc.Name)
                .Set(d => d.Status, doc.Status)
                .Set(d => d.Location, doc.Location)
                .Set(d => d.UpdatedAt, doc.UpdatedAt),
            new UpdateOptions { IsUpsert = true });
    }

    public async Task<bool> SetStatusAsync(string driverId, DriverStatus status)
    {
        var update = Builders<DriverDocument>.Update
            .Set(d => d.Status, status)
            .Set(d => d.UpdatedAt, DateTime.UtcNow);

        var result = await context.Drivers.UpdateOneAsync(d => d.Id == driverId, update);
        return result.MatchedCount > 0;
    }

    public async Task<bool> SetTripStatusAsync(string driverId, Guid tripId, DriverStatus status)
    {
        var f = Builders<DriverDocument>.Filter;
        var filter = f.Eq(d => d.Id, driverId);
        if (status == DriverStatus.Available)
        {
            // A delayed release for A must never clear Busy owned by B.
            // Null permits finishing trips accepted before this field was deployed.
            filter &= f.Eq(d => d.StatusTripId, tripId.ToString("D")) | f.Eq(d => d.StatusTripId, null);
        }
        var update = Builders<DriverDocument>.Update.Set(d => d.Status, status)
            .Set(d => d.StatusTripId, tripId.ToString("D")).Set(d => d.UpdatedAt, DateTime.UtcNow);
        var result = await context.Drivers.UpdateOneAsync(filter, update);
        return result.MatchedCount > 0 ||
            (status == DriverStatus.Available && await context.Drivers.Find(d => d.Id == driverId).AnyAsync());
    }

    public async Task ApplyTripAcceptedAsync(string driverId, Guid tripId, DateTime occurredAt,
        CancellationToken cancellationToken)
    {
        occurredAt = ToMongoPrecision(occurredAt);
        var f = Builders<DriverDocument>.Filter;
        var newer = f.Eq(d => d.LastTripEventAt, null) | f.Lt(d => d.LastTripEventAt, occurredAt);
        var unassigned = f.Ne(d => d.Status, DriverStatus.Busy);
        var filter = f.Eq(d => d.Id, driverId) & f.Ne("closedTripIds", tripId.ToString("D")) & newer &
                     (unassigned | f.Eq(d => d.StatusTripId, tripId.ToString("D")));
        var update = Builders<DriverDocument>.Update
            .Set(d => d.Status, DriverStatus.Busy)
            .Set(d => d.StatusTripId, tripId.ToString("D"))
            .Set(d => d.LastTripEventAt, occurredAt)
            .Set(d => d.UpdatedAt, DateTime.UtcNow);
        var result = await context.Drivers.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        if (result.MatchedCount > 0) return;

        var driver = await context.Drivers.Find(d => d.Id == driverId)
            .FirstOrDefaultAsync(cancellationToken);
        if (driver is null) throw new InvalidOperationException("TripAccepted references an unknown driver.");
        if (driver.ClosedTripIds.Contains(tripId.ToString("D"))) return;
        if (driver.LastTripEventAt >= occurredAt) return;
        throw new InvalidOperationException("Driver is already assigned to another trip.");
    }

    public async Task ApplyTripClosedAsync(string driverId, Guid tripId, DateTime occurredAt,
        CancellationToken cancellationToken)
    {
        occurredAt = ToMongoPrecision(occurredAt);
        var f = Builders<DriverDocument>.Filter;
        var newer = f.Eq(d => d.LastTripEventAt, null) | f.Lt(d => d.LastTripEventAt, occurredAt);
        var unassigned = f.Ne(d => d.Status, DriverStatus.Busy) &
                         f.Ne(d => d.StatusTripId, tripId.ToString("D"));
        var driverFilter = f.Eq(d => d.Id, driverId);

        // Keep a trip-specific tombstone even while another trip owns the driver.
        var recorded = await context.Drivers.UpdateOneAsync(driverFilter,
            Builders<DriverDocument>.Update.AddToSet(d => d.ClosedTripIds, tripId.ToString("D")),
            cancellationToken: cancellationToken);
        if (recorded.MatchedCount == 0)
            throw new InvalidOperationException("TripClosed references an unknown driver.");

        // Record a close that arrives before its accept. A later accept for this trip is then stale.
        var marked = await context.Drivers.UpdateOneAsync(driverFilter & newer & unassigned,
            Builders<DriverDocument>.Update.Set(d => d.LastTripEventAt, occurredAt),
            cancellationToken: cancellationToken);
        if (marked.MatchedCount > 0) return;

        // Releasing the assigned trip and advancing its event clock happen in one document update.
        var released = await context.Drivers.UpdateOneAsync(
            driverFilter & newer & f.Eq(d => d.StatusTripId, tripId.ToString("D")),
            Builders<DriverDocument>.Update
                .Set(d => d.Status, DriverStatus.Available)
                .Set(d => d.StatusTripId, null)
                .Set(d => d.LastTripEventAt, occurredAt)
                .Set(d => d.UpdatedAt, DateTime.UtcNow),
            cancellationToken: cancellationToken);
        if (released.MatchedCount > 0) return;

        var driver = await context.Drivers.Find(d => d.Id == driverId)
            .FirstOrDefaultAsync(cancellationToken);
        if (driver is null) throw new InvalidOperationException("TripClosed references an unknown driver.");
        if (driver.LastTripEventAt >= occurredAt || driver.StatusTripId != tripId.ToString("D")) return;
        throw new InvalidOperationException("TripClosed could not release its assigned driver.");
    }

    public async Task<Driver?> FindNearestAvailableAsync(GeoPoint pickup, double radiusKm, IReadOnlyCollection<string> excludeDriverIds)
    {
        var point = GeoJson.Point(GeoJson.Geographic(pickup.Lng, pickup.Lat));
        var builder = Builders<DriverDocument>.Filter;

        var filter = builder.Eq(d => d.Status, DriverStatus.Available)
                   & builder.NearSphere(d => d.Location, point, maxDistance: radiusKm * 1000);

        if (excludeDriverIds != null && excludeDriverIds.Count > 0)
        {
            filter &= builder.Nin(d => d.Id, excludeDriverIds);
        }

        var doc = await context.Drivers.Find(filter).FirstOrDefaultAsync();
        return doc is null ? null : ToEntity(doc);
    }

    public async Task<bool> AnyAvailableNearAsync(GeoPoint pickup, double radiusKm)
    {
        var point = GeoJson.Point(GeoJson.Geographic(pickup.Lng, pickup.Lat));
        var filter = Builders<DriverDocument>.Filter.Eq(d => d.Status, DriverStatus.Available)
                   & Builders<DriverDocument>.Filter.NearSphere(d => d.Location, point, maxDistance: radiusKm * 1000);

        return await context.Drivers.Find(filter).AnyAsync();
    }

    private static Driver ToEntity(DriverDocument doc) => new()
    {
        Id = doc.Id,
        Name = doc.Name,
        Status = doc.Status,
        Location = doc.Location is null ? null :
            new GeoPoint(doc.Location.Coordinates.Latitude, doc.Location.Coordinates.Longitude),
        UpdatedAt = doc.UpdatedAt
    };

    private static DateTime ToMongoPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, value.Kind);
}
