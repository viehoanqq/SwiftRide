using Microsoft.EntityFrameworkCore;
using Npgsql;
using PaymentService.Infrastructure.Persistence;

namespace PaymentService.Tests;

internal sealed class PaymentConsumerDatabase : IAsyncDisposable
{
    private readonly string adminConnection;
    private readonly string connection;
    private readonly string name = "swiftride_consumer_tests_" + Guid.NewGuid().ToString("N");

    private PaymentConsumerDatabase()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SWIFTRIDE_TEST_POSTGRES"));
        adminConnection = new NpgsqlConnectionStringBuilder(builder.ConnectionString) { Database = "postgres" }.ConnectionString;
        builder.Database = name;
        connection = builder.ConnectionString;
    }

    public PayDbContext Context() => new(new DbContextOptionsBuilder<PayDbContext>()
        .UseNpgsql(connection).UseSnakeCaseNamingConvention().Options);

    public static async Task<PaymentConsumerDatabase> CreateAsync()
    {
        var database = new PaymentConsumerDatabase();
        try
        {
            await using var db = database.Context();
            await db.Database.MigrateAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!name.StartsWith("swiftride_consumer_tests_", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected test database name.");
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(adminConnection);
        await conn.OpenAsync();
        await using (var terminate = conn.CreateCommand())
        {
            terminate.CommandText = "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @name AND pid <> pg_backend_pid();";
            terminate.Parameters.AddWithValue("name", name);
            await terminate.ExecuteNonQueryAsync();
        }
        await using var drop = conn.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{name}\";";
        await drop.ExecuteNonQueryAsync();
    }
}
