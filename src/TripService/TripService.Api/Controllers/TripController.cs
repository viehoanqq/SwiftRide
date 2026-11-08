using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TripService.Application.DTOs;
using TripService.Application.Services;

namespace TripService.Api.Controllers;

[ApiController]
[Route("trips")]
[Authorize]
public class TripsController(TripAppService tripService) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue("sub")!);

    [HttpPost]
    [Authorize(Roles = "Rider")]
    public async Task<ActionResult<CreateTripResponse>> Create(CreateTripRequest request)
    {
        var result = await tripService.CreateAsync(UserId, request);
        return CreatedAtAction(nameof(GetById), new { id = result.TripId }, result);
    }

    [HttpGet("me/current")]
    [Authorize(Roles = "Rider")]
    public async Task<ActionResult<TripDetailDto>> GetCurrent()
    {
        var trip = await tripService.GetCurrentAsync(UserId);
        return trip is null ? NoContent() : trip;
    }

    [HttpPost("{id:guid}/confirm")]
    [Authorize(Roles = "Rider")]
    public Task<ConfirmTripResponse> Confirm(Guid id, ConfirmTripRequest request)
        => tripService.ConfirmAsync(id, UserId, request);

    [HttpGet("{id:guid}")]
    public Task<TripDetailDto> GetById(Guid id)
        => tripService.GetByIdAsync(id, UserId, User.FindFirstValue("role")!);

    [HttpPost("{id:guid}/driver-accept")]
    [Authorize(Roles = "Driver")]
    public Task<TripDriverActionResponse> AcceptOffer(Guid id, CancellationToken cancellationToken)
        => tripService.AcceptOfferAsync(id, UserId, cancellationToken);

    [HttpPost("{id:guid}/driver-reject")]
    [Authorize(Roles = "Driver")]
    public Task<TripDriverActionResponse> RejectOffer(Guid id, CancellationToken cancellationToken)
        => tripService.RejectOfferAsync(id, UserId, cancellationToken);

    [HttpPost("{id:guid}/en-route")]
    [Authorize(Roles = "Driver")]
    public Task<TripDriverActionResponse> MarkEnRoute(Guid id, CancellationToken cancellationToken)
        => tripService.MarkEnRouteAsync(id, UserId, cancellationToken);

    [HttpPost("{id:guid}/pickup")]
    [Authorize(Roles = "Driver")]
    public Task<TripDriverActionResponse> MarkPickedUp(Guid id, CancellationToken cancellationToken)
        => tripService.MarkPickedUpAsync(id, UserId, cancellationToken);

    [HttpPost("{id:guid}/dropoff")]
    [Authorize(Roles = "Driver")]
    public Task<TripDriverActionResponse> MarkDroppedOff(Guid id, CancellationToken cancellationToken)
        => tripService.MarkDroppedOffAsync(id, UserId, cancellationToken);
}
