using MatchingService.Application.Interfaces;
using MatchingService.Domain.Entities;
using MatchingService.Domain.Enums;
using MongoDB.Driver;
using MongoDB.Driver.GeoJsonObjectModel;

namespace MatchingService.Infrastructure.Mongo;

public class DriverRepository(MongoContext context) : IDriverRepository
{
    public async Task<Driver?> GetByIdAsync(string driverId)
    {
        var document = await context.Drivers
            .Find(x => x.Id == driverId)
            .FirstOrDefaultAsync();

        return document is null ? null : Map(document);
    }

    public async Task UpsertAsync(Driver driver)
    {
        var document = new DriverDocument
        {
            Id = driver.Id,
            Name = driver.Name,
            Status = driver.Status,
            Location = GeoJson.Point(GeoJson.Geographic(driver.Location.Lng, driver.Location.Lat)),
            UpdatedAt = DateTime.UtcNow
        };

        await context.Drivers.ReplaceOneAsync(
            x => x.Id == driver.Id,
            document,
            new ReplaceOptions { IsUpsert = true });
    }

    public async Task<bool> SetStatusAsync(string driverId, DriverStatus status)
    {
        var result = await context.Drivers.UpdateOneAsync(
            x => x.Id == driverId,
            Builders<DriverDocument>.Update
                .Set(x => x.Status, status)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        return result.ModifiedCount > 0 || result.MatchedCount > 0;
    }

    public async Task<Driver?> FindNearestAvailableAsync(GeoPoint pickup, double radiusKm, IReadOnlyCollection<string> excludeDriverIds)
    {
        var filter = Builders<DriverDocument>.Filter.Eq(x => x.Status, DriverStatus.Available);

        if (excludeDriverIds.Count > 0)
        {
            filter = filter & Builders<DriverDocument>.Filter.Nin(x => x.Id, excludeDriverIds);
        }

        var candidates = await context.Drivers
            .Find(filter)
            .ToListAsync();

        var nearest = candidates
            .Select(doc => new
            {
                Driver = Map(doc),
                DistanceKm = new GeoPoint(doc.Location.Coordinates.Latitude, doc.Location.Coordinates.Longitude)
                    .DistanceKmTo(pickup)
            })
            .Where(x => x.DistanceKm <= radiusKm)
            .OrderBy(x => x.DistanceKm)
            .FirstOrDefault();

        return nearest?.Driver;
    }

    public async Task<bool> AnyAvailableNearAsync(GeoPoint pickup, double radiusKm)
    {
        var candidates = await context.Drivers
            .Find(x => x.Status == DriverStatus.Available)
            .ToListAsync();

        return candidates.Any(doc =>
        {
            var lat = doc.Location.Coordinates.Latitude;
            var lng = doc.Location.Coordinates.Longitude;
            return new GeoPoint(lat, lng).DistanceKmTo(pickup) <= radiusKm;
        });
    }

    private static Driver Map(DriverDocument document) => new()
    {
        Id = document.Id,
        Name = document.Name,
        Status = document.Status,
        Location = new GeoPoint(document.Location.Coordinates.Latitude, document.Location.Coordinates.Longitude),
        UpdatedAt = document.UpdatedAt
    };
}
