using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TripService.Application.DTOs;
using TripService.Application.Interfaces;
using TripService.Application.Exceptions;

namespace TripService.Api.Controllers;

[ApiController]
[Route("drivers/me")]
[Authorize(Roles = "Driver")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class DriverMeController(ITripDriverWorkflow tripWorkflow) : ControllerBase
{
    private Guid DriverId => Guid.TryParse(User.FindFirstValue("sub"), out var id) && id != Guid.Empty
        ? id : throw new UnauthorizedException("INVALID_TOKEN_SUBJECT", "Token kh?ng c? ID t?i x? h?p l?.");

    [HttpGet("offers")]
    [ProducesResponseType(typeof(IReadOnlyList<DriverOfferDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DriverOfferDto>>> GetOffers(
        CancellationToken cancellationToken)
    {
        var offers = await tripWorkflow.GetOffersAsync(DriverId, cancellationToken);
        return Ok(offers);
    }

    [HttpGet("current-trip")]
    [ProducesResponseType(typeof(TripDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
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