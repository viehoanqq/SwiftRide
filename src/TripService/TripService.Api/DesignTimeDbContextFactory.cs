using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TripService.Infrastructure.Persistence;

namespace TripService.Api;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TripDbContext>
{
    public TripDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.GetDirectoryName(typeof(DesignTimeDbContextFactory).Assembly.Location)!)
            .AddJsonFile("appsettings.json")
            .AddEnvironmentVariables().Build();
        return new(new DbContextOptionsBuilder<TripDbContext>()
            .UseSqlServer(configuration.GetConnectionString("TripDb")).Options);
    }
}
