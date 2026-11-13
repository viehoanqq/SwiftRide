using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using IdentityService.Infrastructure.Persistence;

namespace IdentityService.Api;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<UserDbContext>
{
    public UserDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.GetDirectoryName(typeof(DesignTimeDbContextFactory).Assembly.Location)!)
            .AddJsonFile("appsettings.json")
            .AddEnvironmentVariables().Build();
        return new(new DbContextOptionsBuilder<UserDbContext>()
            .UseSqlServer(configuration.GetConnectionString("UserDb")).Options);
    }
}
