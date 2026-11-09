using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moq;
using TripService.Application.DTOs;
using TripService.Application.Exceptions;
using TripService.Application.Interfaces;
using TripService.Domain.Enums;

namespace TripService.Tests.Api;

public class TripApiFlowTests
{
    private static CreateTripRequest Request => new(
        new LocationDto(10.77, 106.69, "Pickup"), new LocationDto(10.78, 106.70, "Dropoff"));

    private static async Task<Guid> Create(HttpClient rider)
    {
        using var response = await rider.PostAsJsonAsync("/trips", Request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<CreateTripResponse>())!;
        Assert.EndsWith($"/trips/{body.TripId}", response.Headers.Location!.ToString());
        return body.TripId;
    }

    private static async Task<Guid> Offer(TripApiFactory factory, HttpClient rider)
    {
        var id = await Create(rider);
        using var response = await rider.PostAsJsonAsync($"/trips/{id}/confirm", new { accept = true, paymentMethod = "WALLET" });
        await AssertState(response, "DriverOffered");
        return id;
    }

    private static async Task AssertState(HttpResponseMessage response, string status)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(status, json.RootElement.GetProperty("status").GetString());
    }

    private static async Task Error(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task FullJourney_CreateConfirmAcceptEnRoutePickupDropoff()
    {
        using var app = new TripApiFactory();
        using var rider = app.Client(app.Rider, "Rider");
        using var driver = app.Client(app.Driver, "Driver");
        Assert.Equal(HttpStatusCode.NoContent, (await rider.GetAsync("/trips/me/current")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await driver.GetAsync("/drivers/me/current-trip")).StatusCode);
        Assert.Empty((await driver.GetFromJsonAsync<List<DriverOfferDto>>("/drivers/me/offers"))!);
        var id = await Offer(app, rider);
        var current = await rider.GetFromJsonAsync<TripDetailDto>("/trips/me/current");
        Assert.Equal(id, current!.Id);
        var offer = Assert.Single((await driver.GetFromJsonAsync<List<DriverOfferDto>>("/drivers/me/offers"))!);
        Assert.Equal(id, offer.TripId);
        Assert.Equal(50000m, offer.Price);
        Assert.Equal(1.5, offer.DistanceToPickupKm);
        await AssertState(await driver.PostAsync($"/trips/{id}/driver-accept", null), "DriverAccepted");
        Assert.Empty((await driver.GetFromJsonAsync<List<DriverOfferDto>>("/drivers/me/offers"))!);
        Assert.Equal(id, (await driver.GetFromJsonAsync<TripDetailDto>("/drivers/me/current-trip"))!.Id);
        await AssertState(await driver.PostAsync($"/trips/{id}/en-route", null), "EnRoute");
        await AssertState(await driver.PostAsync($"/trips/{id}/pickup", null), "PickedUp");
        await AssertState(await driver.PostAsync($"/trips/{id}/dropoff", null), "PaymentPending");
        var detail = (await rider.GetFromJsonAsync<TripDetailDto>($"/trips/{id}"))!;
        Assert.Equal(new[] { "Requested", "Quoted", "DriverOffered", "DriverAccepted", "EnRoute", "PickedUp", "DroppedOff", "PaymentPending" },
            detail.History.Select(h => h.To));
        Assert.Null(detail.History[0].From);
        for (var i = 1; i < detail.History.Count; i++) Assert.Equal(detail.History[i - 1].To, detail.History[i].From);
        Assert.Equal(app.Driver, detail.Driver!.Id);
        app.StatusClient.Verify(c => c.SetStatusAsync(app.Driver, DriverAvailabilityStatus.Busy, It.IsAny<CancellationToken>()), Times.Once);
        app.StatusClient.Verify(c => c.SetStatusAsync(app.Driver, DriverAvailabilityStatus.Available, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("PaymentPending", (await driver.GetFromJsonAsync<TripDetailDto>("/drivers/me/current-trip"))!.Status);
    }

    [Fact]
    public async Task Reject_ExcludesPreviousDriver_ThenFailsAtMaxAttempts()
    {
        using var app = new TripApiFactory();
        using var rider = app.Client(app.Rider, "Rider");
        using var driver = app.Client(app.Driver, "Driver");
        using var second = app.Client(app.SecondDriver, "Driver");
        var id = await Offer(app, rider);
        app.Matching.Setup(c => c.MatchAsync(It.Is<MatchDriverRequest>(r => r.ExcludeDriverIds != null && r.ExcludeDriverIds.Contains(app.Driver.ToString()))))
            .ReturnsAsync(new MatchDriverResult(app.SecondDriver.ToString(), "Second", "Available", 2, new GeoPointDto(10.77, 106.69)));
        var count = app.Store.Committed[id].History.Count;
        await AssertState(await driver.PostAsync($"/trips/{id}/driver-reject", null), "DriverOffered");
        Assert.Equal(count, app.Store.Committed[id].History.Count);
        Assert.Empty((await driver.GetFromJsonAsync<List<DriverOfferDto>>("/drivers/me/offers"))!);
        Assert.Single((await second.GetFromJsonAsync<List<DriverOfferDto>>("/drivers/me/offers"))!);
        await AssertState(await second.PostAsync($"/trips/{id}/driver-reject", null), "Failed");
        Assert.All(app.Store.Committed[id].Offers, o => Assert.Equal(OfferStatus.Rejected, o.Status));
        app.Matching.Verify(c => c.MatchAsync(It.IsAny<MatchDriverRequest>()), Times.Exactly(2));
        app.StatusClient.Verify(c => c.SetStatusAsync(It.IsAny<Guid>(), It.IsAny<DriverAvailabilityStatus>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reject_NoReplacementDriver_FailsTrip()
    {
        using var app = new TripApiFactory();
        using var rider = app.Client(app.Rider, "Rider");
        using var driver = app.Client(app.Driver, "Driver");
        var id = await Offer(app, rider);
        app.Matching.Setup(c => c.MatchAsync(It.IsAny<MatchDriverRequest>())).ReturnsAsync((MatchDriverResult?)null);
        await AssertState(await driver.PostAsync($"/trips/{id}/driver-reject", null), "Failed");
    }

    [Fact]
    public async Task RiderDeclinesQuote_CancelsWithoutMatching()
    {
        using var app = new TripApiFactory();
        using var rider = app.Client(app.Rider, "Rider");
        var id = await Create(rider);
        await AssertState(await rider.PostAsJsonAsync($"/trips/{id}/confirm", new { accept = false }), "Cancelled");
        Assert.Equal(HttpStatusCode.NoContent, (await rider.GetAsync("/trips/me/current")).StatusCode);
        app.Matching.Verify(c => c.MatchAsync(It.IsAny<MatchDriverRequest>()), Times.Never);
    }

    public static IEnumerable<object[]> Routes()
    {
        yield return new object[] { "POST", "/trips", "Rider" };
        yield return new object[] { "GET", "/trips/me/current", "Rider" };
        yield return new object[] { "POST", "/trips/{id}/confirm", "Rider" };
        yield return new object[] { "GET", "/trips/{id}", "Either" };
        yield return new object[] { "GET", "/drivers/me/offers", "Driver" };
        yield return new object[] { "GET", "/drivers/me/current-trip", "Driver" };
        foreach (var action in new[] { "driver-accept", "driver-reject", "en-route", "pickup", "dropoff" })
            yield return new object[] { "POST", "/trips/{id}/" + action, "Driver" };
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task EveryRoute_RequiresAuthenticationAndCorrectRole(string method, string path, string role)
    {
        using var app = new TripApiFactory();
        path = path.Replace("{id}", Guid.NewGuid().ToString());
        using var anonymous = app.CreateClient();
        await Error(await anonymous.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)), HttpStatusCode.Unauthorized, "UNAUTHORIZED");
        if (role == "Either") return;
        using var wrongRole = app.Client(Guid.NewGuid(), role == "Driver" ? "Rider" : "Driver");
        await Error(await wrongRole.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)), HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Theory]
    [InlineData("driver-accept")]
    [InlineData("driver-reject")]
    [InlineData("en-route")]
    [InlineData("pickup")]
    [InlineData("dropoff")]
    public async Task DriverAction_MissingTrip404_AndUnassignedDriver403(string action)
    {
        using var app = new TripApiFactory();
        using var driver = app.Client(app.SecondDriver, "Driver");
        await Error(await driver.PostAsync($"/trips/{Guid.NewGuid()}/{action}", null), HttpStatusCode.NotFound, "TRIP_NOT_FOUND");
        using var rider = app.Client(app.Rider, "Rider");
        var id = await Offer(app, rider);
        await Error(await driver.PostAsync($"/trips/{id}/{action}", null), HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Theory]
    [InlineData("driver-accept")]
    [InlineData("driver-reject")]
    [InlineData("pickup")]
    [InlineData("dropoff")]
    public async Task InvalidActionAfterAccept_Returns409WithoutMutation(string action)
    {
        using var app = new TripApiFactory();
        using var rider = app.Client(app.Rider, "Rider");
        using var driver = app.Client(app.Driver, "Driver");
        var id = await Offer(app, rider);
        await AssertState(await driver.PostAsync($"/trips/{id}/driver-accept", null), "DriverAccepted");
        var before = JsonSerializer.Serialize(app.Store.Committed[id]);
        await Error(await driver.PostAsync($"/trips/{id}/{action}", null), HttpStatusCode.Conflict, "INVALID_TRIP_STATE");
        Assert.Equal(before, JsonSerializer.Serialize(app.Store.Committed[id]));
    }

    [Fact]
    public async Task ValidationOwnershipAndDuplicateTrip_AreRejected()
    {
        using var app = new TripApiFactory();
        using var rider = app.Client(app.Rider, "Rider");
        using var stranger = app.Client(Guid.NewGuid(), "Rider");
        using var otherDriver = app.Client(app.SecondDriver, "Driver");
        var invalid = Request with { Pickup = new LocationDto(100, 106, "Invalid") };
        await Error(await rider.PostAsJsonAsync("/trips", invalid), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(HttpStatusCode.BadRequest, (await rider.PostAsync("/trips", new StringContent("{", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        var id = await Create(rider);
        await Error(await rider.PostAsJsonAsync("/trips", Request), HttpStatusCode.Conflict, "ACTIVE_TRIP_EXISTS");
        await Error(await rider.PostAsJsonAsync($"/trips/{id}/confirm", new { accept = true, paymentMethod = "CASH" }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await Error(await stranger.PostAsJsonAsync($"/trips/{id}/confirm", new { accept = false }), HttpStatusCode.Forbidden, "FORBIDDEN");
        await Error(await stranger.GetAsync($"/trips/{id}"), HttpStatusCode.Forbidden, "FORBIDDEN");
        await Error(await otherDriver.GetAsync($"/trips/{id}"), HttpStatusCode.Forbidden, "FORBIDDEN");
        await Error(await rider.GetAsync($"/trips/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "TRIP_NOT_FOUND");
    }

    [Theory]
    [InlineData(502)]
    [InlineData(504)]
    public async Task StatusClientFailure_ReturnsErrorButTripHasAlreadyCommitted(int status)
    {
        using var app = new TripApiFactory();
        using var rider = app.Client(app.Rider, "Rider");
        using var driver = app.Client(app.Driver, "Driver");
        var id = await Offer(app, rider);
        app.StatusClient.Setup(c => c.SetStatusAsync(app.Driver, DriverAvailabilityStatus.Busy, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DownstreamException(status, "MATCHING_FAILURE", "Test failure"));
        await Error(await driver.PostAsync($"/trips/{id}/driver-accept", null), (HttpStatusCode)status, "MATCHING_FAILURE");
        Assert.Equal(TripStatus.DriverAccepted, app.Store.Committed[id].Status);
    }

    [Fact]
    public async Task ConcurrencyFailure_Returns409AndDoesNotCallStatusClient()
    {
        using var app = new TripApiFactory();
        using var rider = app.Client(app.Rider, "Rider");
        using var driver = app.Client(app.Driver, "Driver");
        var id = await Offer(app, rider);
        app.Store.SaveFailure = new DbUpdateConcurrencyException("Simulated conflict");
        await Error(await driver.PostAsync($"/trips/{id}/driver-accept", null), HttpStatusCode.Conflict, "CONCURRENCY_CONFLICT");
        Assert.Equal(TripStatus.DriverOffered, app.Store.Committed[id].Status);
        app.StatusClient.Verify(c => c.SetStatusAsync(It.IsAny<Guid>(), It.IsAny<DriverAvailabilityStatus>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvalidExpiredAndMalformedSubjectTokens_Return401()
    {
        using var app = new TripApiFactory();
        using var invalid = app.CreateClient();
        invalid.DefaultRequestHeaders.Authorization = new("Bearer", "invalid");
        await Error(await invalid.GetAsync("/drivers/me/offers"), HttpStatusCode.Unauthorized, "TOKEN_INVALID");
        using var expired = app.Client(app.Driver, "Driver", DateTime.UtcNow.AddMinutes(-5));
        await Error(await expired.GetAsync("/drivers/me/offers"), HttpStatusCode.Unauthorized, "TOKEN_EXPIRED");
        using var badSubject = app.Client(app.Driver, "Driver", subject: "not-a-guid");
        await Error(await badSubject.GetAsync("/drivers/me/offers"), HttpStatusCode.Unauthorized, "INVALID_TOKEN_SUBJECT");
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("confirm")]
    public async Task NoDriverDuringQuoteOrConfirm_ReturnsFailed(string stage)
    {
        using var app = new TripApiFactory();
        using var rider = app.Client(app.Rider, "Rider");
        if (stage == "quote")
            app.Matching.Setup(c => c.GetQuoteAsync(It.IsAny<Guid>(), It.IsAny<GeoPointDto>(), It.IsAny<GeoPointDto>()))
                .ReturnsAsync((QuoteResult?)null);
        else
            app.Matching.Setup(c => c.MatchAsync(It.IsAny<MatchDriverRequest>())).ReturnsAsync((MatchDriverResult?)null);
        var id = await Create(rider);
        if (stage == "confirm")
            await AssertState(await rider.PostAsJsonAsync($"/trips/{id}/confirm", new { accept = true, paymentMethod = "CARD" }), "Failed");
        Assert.Equal(TripStatus.Failed, app.Store.Committed[id].Status);
        Assert.Equal(HttpStatusCode.NoContent, (await rider.GetAsync("/trips/me/current")).StatusCode);
    }

    [Theory]
    [InlineData("quote", 502)]
    [InlineData("quote", 504)]
    [InlineData("confirm", 502)]
    [InlineData("confirm", 504)]
    [InlineData("reject", 502)]
    [InlineData("reject", 504)]
    public async Task MatchingFailure_DoesNotPersistPartialWorkflow(string stage, int status)
    {
        using var app = new TripApiFactory();
        using var rider = app.Client(app.Rider, "Rider");
        using var driver = app.Client(app.Driver, "Driver");
        var id = stage == "quote" ? Guid.Empty : stage == "confirm" ? await Create(rider) : await Offer(app, rider);
        var before = JsonSerializer.Serialize(app.Store.Committed);
        var exception = new DownstreamException(status, "MATCHING_FAILURE", "Test failure");
        if (stage == "quote")
            app.Matching.Setup(c => c.GetQuoteAsync(It.IsAny<Guid>(), It.IsAny<GeoPointDto>(), It.IsAny<GeoPointDto>())).ThrowsAsync(exception);
        else app.Matching.Setup(c => c.MatchAsync(It.IsAny<MatchDriverRequest>())).ThrowsAsync(exception);
        using var response = stage switch
        {
            "quote" => await rider.PostAsJsonAsync("/trips", Request),
            "confirm" => await rider.PostAsJsonAsync($"/trips/{id}/confirm", new { accept = true, paymentMethod = "WALLET" }),
            _ => await driver.PostAsync($"/trips/{id}/driver-reject", null)
        };
        await Error(response, (HttpStatusCode)status, "MATCHING_FAILURE");
        Assert.Equal(before, JsonSerializer.Serialize(app.Store.Committed));
    }

    [Fact]
    public async Task RepeatedEnRoutePickupDropoff_DoesNotAddHistoryOrRepeatStatusUpdate()
    {
        using var app = new TripApiFactory();
        using var rider = app.Client(app.Rider, "Rider");
        using var driver = app.Client(app.Driver, "Driver");
        var id = await Offer(app, rider);
        await AssertState(await driver.PostAsync($"/trips/{id}/driver-accept", null), "DriverAccepted");
        foreach (var (action, status) in new[] { ("en-route", "EnRoute"), ("pickup", "PickedUp"), ("dropoff", "PaymentPending") })
        {
            await AssertState(await driver.PostAsync($"/trips/{id}/{action}", null), status);
            var before = JsonSerializer.Serialize(app.Store.Committed[id]);
            await Error(await driver.PostAsync($"/trips/{id}/{action}", null), HttpStatusCode.Conflict, "INVALID_TRIP_STATE");
            Assert.Equal(before, JsonSerializer.Serialize(app.Store.Committed[id]));
        }
        app.StatusClient.Verify(c => c.SetStatusAsync(app.Driver, DriverAvailabilityStatus.Available, It.IsAny<CancellationToken>()), Times.Once);
    }
}
