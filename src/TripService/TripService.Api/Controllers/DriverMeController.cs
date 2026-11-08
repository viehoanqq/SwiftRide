using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TripService.Application.DTOs;
using TripService.Application.Interfaces;

namespace TripService.Api.Controllers;

[ApiController]
[Route("drivers/me")]
[Authorize(Roles = "Driver")]
public class DriverMeController(ITripDriverWorkflow tripWorkflow) : ControllerBase
{
    private Guid DriverId => Guid.Parse(User.FindFirstValue("sub")!);

    [HttpGet("offers")]
    public async Task<ActionResult<IReadOnlyList<DriverOfferDto>>> GetOffers(
        CancellationToken cancellationToken)
    {
        var offers = await tripWorkflow.GetOffersAsync(DriverId, cancellationToken);
        return Ok(offers);
    }

    [HttpGet("current-trip")]
    public async Task<ActionResult<TripDetailDto>> GetCurrentTrip(
        CancellationToken cancellationToken)
    {
        var trip = await tripWorkflow.GetCurrentTripAsync(DriverId, cancellationToken);
        return trip is null ? NoContent() : Ok(trip);
    }

    [HttpPost("offers/{tripId:guid}/accept")]
    public async Task<ActionResult<TripDriverActionResponse>> AcceptOffer(
        Guid tripId, 
        CancellationToken cancellationToken)
    {
        var result = await tripWorkflow.AcceptOfferAsync(tripId, DriverId, cancellationToken);
        return Ok(result);
    }

    [HttpPost("offers/{tripId:guid}/reject")]
    public async Task<ActionResult<TripDriverActionResponse>> RejectOffer(
        Guid tripId, 
        CancellationToken cancellationToken)
    {
        var result = await tripWorkflow.RejectOfferAsync(tripId, DriverId, cancellationToken);
        return Ok(result);
    }
}