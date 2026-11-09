using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Moq;
using TripService.Application.DTOs;
using TripService.Application.Interfaces;
using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Infrastructure.Outbox;

namespace TripService.Tests.Api;

public sealed class TripApiFactory : WebApplicationFactory<Program>
{
    private const string Key = "test-only-signing-key-not-used-by-real-services-123456789";
    public Guid Rider { get; } = Guid.NewGuid();
    public Guid Driver { get; } = Guid.NewGuid();
    public Guid SecondDriver { get; } = Guid.NewGuid();
    public Mock<IMatchingClient> Matching { get; } = new();
    public Mock<IDriverStatusClient> StatusClient { get; } = new();
    public TripStore Store { get; } = new();

    public TripApiFactory()
    {
        Matching.Setup(m => m.GetQuoteAsync(It.IsAny<Guid>(), It.IsAny<GeoPointDto>(), It.IsAny<GeoPointDto>()))
            .ReturnsAsync(new QuoteResult("quote-test", 5, 10, "Standard", 50000m));
        Matching.Setup(m => m.MatchAsync(It.IsAny<MatchDriverRequest>()))
            .ReturnsAsync(new MatchDriverResult(Driver.ToString(), "Driver", "Available", 1.5,
                new GeoPointDto(10.77, 106.69)));
        StatusClient.Setup(m => m.SetStatusAsync(It.IsAny<Guid>(), It.IsAny<DriverAvailabilityStatus>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "trip-api-tests", ["Jwt:Audience"] = "trip-api-tests",
            ["Jwt:Key"] = Key, ["Trip:MaxOfferAttempts"] = "2"
        }));
        builder.ConfigureTestServices(services =>
        {
            foreach (var registration in services.Where(s => s.ServiceType == typeof(IHostedService) &&
                s.ImplementationType == typeof(DriverRegistrationWorker)).ToList())
                services.Remove(registration);
            services.RemoveAll<ITripRepository>();
            services.RemoveAll<IMatchingClient>();
            services.RemoveAll<IDriverStatusClient>();
            services.AddSingleton(Store);
            services.AddScoped<ITripRepository, MemoryTripRepository>();
            services.AddSingleton(Matching.Object);
            services.AddSingleton(StatusClient.Object);
        });
    }

    public HttpClient Client(Guid userId, string role, DateTime? expires = null, string? subject = null)
    {
        var client = CreateClient();
        var token = new JwtSecurityToken("trip-api-tests", "trip-api-tests",
            new[] { new Claim("sub", subject ?? userId.ToString()), new Claim("role", role) },
            expires: expires ?? DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
                SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }
}

// A committed snapshot store, not EF: each request works with detached copies until SaveChanges.
public sealed class TripStore
{
    public Dictionary<Guid, Trip> Committed { get; } = new();
    public Exception? SaveFailure { get; set; }
    public static Trip Copy(Trip trip)
    {
        var copy = JsonSerializer.Deserialize<Trip>(JsonSerializer.Serialize(trip))!;
        typeof(Trip).GetProperty(nameof(Trip.Status))!.SetValue(copy, trip.Status);
        return copy;
    }
}

internal sealed class MemoryTripRepository(TripStore store) : ITripRepository
{
    private readonly Dictionary<Guid, Trip> tracked = new();
    private static bool Active(Trip t) => t.Status is not
        (TripStatus.Paid or TripStatus.Cancelled or TripStatus.Failed or TripStatus.PaymentFailed);
    private Trip Track(Trip t)
    {
        if (!tracked.TryGetValue(t.Id, out var copy)) tracked[t.Id] = copy = TripStore.Copy(t);
        return copy;
    }
    public Task AddAsync(Trip trip) { tracked[trip.Id] = trip; return Task.CompletedTask; }
    public void AddOffer(TripDriverOffer offer) { }
    public Task<Trip?> GetByIdAsync(Guid id) => Task.FromResult(
        store.Committed.TryGetValue(id, out var trip) ? Track(trip) : null);
    public Task<Trip?> GetActiveByRiderAsync(Guid riderId) => Task.FromResult(
        store.Committed.Values.Where(t => t.RiderId == riderId && Active(t)).Select(Track).FirstOrDefault());
    public Task<IReadOnlyList<Trip>> GetTripsWithPendingOffersForDriverAsync(Guid driverId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Trip>>(store.Committed.Values.Where(t => t.Status == TripStatus.DriverOffered &&
            t.Offers.Any(o => o.DriverId == driverId && o.Status == OfferStatus.Pending)).Select(Track).ToList());
    public Task<Trip?> GetActiveByDriverAsync(Guid driverId, CancellationToken ct) => Task.FromResult(
        store.Committed.Values.Where(t => Active(t) && t.Offers.Any(o => o.DriverId == driverId &&
            o.Status == OfferStatus.Accepted)).Select(Track).FirstOrDefault());
    public Task SaveChangesAsync()
    {
        if (store.SaveFailure is not null) throw store.SaveFailure;
        foreach (var trip in tracked.Values)
        {
            foreach (var offer in trip.Offers)
                offer.Driver = new User { Id = offer.DriverId, FullName = "Driver" };
            for (var i = 0; i < trip.History.Count; i++) trip.History[i].Id = i + 1;
            store.Committed[trip.Id] = TripStore.Copy(trip);
        }
        return Task.CompletedTask;
    }
}
