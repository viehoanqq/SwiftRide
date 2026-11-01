using MongoDB.Bson;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;

namespace MatchingService.Infrastructure.Mongo;

public class MongoContext
{
    static MongoContext()
    {
        // Tên field dạng camelCase, enum lưu dạng chuỗi ("Available"), decimal lưu Decimal128.
        ConventionRegistry.Register("SwiftRide", new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new EnumRepresentationConvention(BsonType.String),
            new IgnoreExtraElementsConvention(true)
        }, _ => true);
        MongoDB.Bson.Serialization.BsonSerializer.TryRegisterSerializer(
            new MongoDB.Bson.Serialization.Serializers.DecimalSerializer(BsonType.Decimal128));
    }

    public MongoContext(string connectionString)
    {
        var url = new MongoUrl(connectionString);
        var database = new MongoClient(url).GetDatabase(url.DatabaseName ?? "MatchDb");
        Drivers = database.GetCollection<DriverDocument>("drivers");
        Quotes = database.GetCollection<QuoteDocument>("quotes");
    }

    public IMongoCollection<DriverDocument> Drivers { get; }
    public IMongoCollection<QuoteDocument> Quotes { get; }

    public async Task EnsureIndexesAsync()
    {
        await Drivers.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<DriverDocument>(Builders<DriverDocument>.IndexKeys.Geo2DSphere(d => d.Location)),
            new CreateIndexModel<DriverDocument>(Builders<DriverDocument>.IndexKeys.Ascending(d => d.Status))
        ]);
        await Quotes.Indexes.CreateOneAsync(
            new CreateIndexModel<QuoteDocument>(Builders<QuoteDocument>.IndexKeys.Ascending(q => q.TripId)));
    }
}
