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
}
