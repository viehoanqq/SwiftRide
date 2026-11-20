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
        QuoteResults = database.GetCollection<QuoteResultDocument>("quoteResults");
        MatchResults = database.GetCollection<MatchResultDocument>("matchResults");
    }

    public IMongoCollection<DriverDocument> Drivers { get; }
    public IMongoCollection<QuoteDocument> Quotes { get; }
    public IMongoCollection<QuoteResultDocument> QuoteResults { get; }
    public IMongoCollection<MatchResultDocument> MatchResults { get; }

    public async Task EnsureIndexesAsync()
    {
        await MatchResults.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<MatchResultDocument>(Builders<MatchResultDocument>.IndexKeys
                .Ascending(x => x.PublishedAt).Ascending(x => x.ClaimedUntil).Ascending(x => x.CreatedAt)),
            new CreateIndexModel<MatchResultDocument>(Builders<MatchResultDocument>.IndexKeys.Ascending(x => x.TripId))
        ]);
        await QuoteResults.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<QuoteResultDocument>(Builders<QuoteResultDocument>.IndexKeys
                .Ascending(x => x.PublishedAt).Ascending(x => x.ClaimedUntil).Ascending(x => x.CreatedAt)),
            new CreateIndexModel<QuoteResultDocument>(Builders<QuoteResultDocument>.IndexKeys.Ascending(x => x.TripId))
        ]);
        await Drivers.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<DriverDocument>(Builders<DriverDocument>.IndexKeys.Geo2DSphere(d => d.Location)),
            new CreateIndexModel<DriverDocument>(Builders<DriverDocument>.IndexKeys.Ascending(d => d.Status))
        ]);
        await Quotes.Indexes.CreateOneAsync(
            new CreateIndexModel<QuoteDocument>(Builders<QuoteDocument>.IndexKeys.Ascending(q => q.TripId)));
    }
}
