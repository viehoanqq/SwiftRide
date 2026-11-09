using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TripService.Application.DTOs;
using TripService.Application.Services;
using TripService.Application.Interfaces;
using TripService.Application.Exceptions;

namespace TripService.Api.Controllers;

[ApiController]
[Route("trips")]
[Authorize]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class TripsController(TripAppService tripService, ITripDriverWorkflow driverWorkflow) : ControllerBase
{
    private Guid UserId => Guid.TryParse(User.FindFirstValue("sub"), out var id) && id != Guid.Empty
        ? id : throw new UnauthorizedException("INVALID_TOKEN_SUBJECT", "Token kh?ng c? ID ng??i d?ng h?p l?.");

    [HttpPost]
    [ProducesResponseType(typeof(CreateTripResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    [Authorize(Roles = "Rider")]
    public async Task<ActionResult<CreateTripResponse>> Create(CreateTripRequest request)
    {
        var result = await tripService.CreateAsync(UserId, request);
        return CreatedAtAction(nameof(GetById), new { id = result.TripId }, result);
    }

    [HttpGet("me/current")]
    [ProducesResponseType(typeof(TripDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Authorize(Roles = "Rider")]
    public async Task<ActionResult<TripDetailDto>> GetCurrent()
    {
        var trip = await tripService.GetCurrentAsync(UserId);
        return trip is null ? NoContent() : trip;
    }

    [HttpPost("{id:guid}/confirm")]
    [ProducesResponseType(typeof(ConfirmTripResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    [Authorize(Roles = "Rider")]
    public Task<ConfirmTripResponse> Confirm(Guid id, ConfirmTripRequest request)
        => tripService.ConfirmAsync(id, UserId, request);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TripDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<TripDetailDto> GetById(Guid id)
        => tripService.GetByIdAsync(id, UserId, User.FindFirstValue("role")!);

    [HttpPost("{id:guid}/driver-accept")]
    [ProducesResponseType(typeof(TripDriverActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    [Authorize(Roles = "Driver")]
    public Task<TripDriverActionResponse> AcceptOffer(Guid id, CancellationToken cancellationToken)
        => driverWorkflow.AcceptOfferAsync(id, UserId, cancellationToken);

    [HttpPost("{id:guid}/driver-reject")]
    [ProducesResponseType(typeof(TripDriverActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    [Authorize(Roles = "Driver")]
    public Task<TripDriverActionResponse> RejectOffer(Guid id, CancellationToken cancellationToken)
        => driverWorkflow.RejectOfferAsync(id, UserId, cancellationToken);

    [HttpPost("{id:guid}/en-route")]
    [ProducesResponseType(typeof(TripDriverActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Authorize(Roles = "Driver")]
    public Task<TripDriverActionResponse> MarkEnRoute(Guid id, CancellationToken cancellationToken)
        => driverWorkflow.MarkEnRouteAsync(id, UserId, cancellationToken);

    [HttpPost("{id:guid}/pickup")]
    [ProducesResponseType(typeof(TripDriverActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Authorize(Roles = "Driver")]
    public Task<TripDriverActionResponse> MarkPickedUp(Guid id, CancellationToken cancellationToken)
        => driverWorkflow.MarkPickedUpAsync(id, UserId, cancellationToken);

    [HttpPost("{id:guid}/dropoff")]
    [ProducesResponseType(typeof(TripDriverActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    [Authorize(Roles = "Driver")]
    public Task<TripDriverActionResponse> MarkDroppedOff(Guid id, CancellationToken cancellationToken)
        => driverWorkflow.MarkDroppedOffAsync(id, UserId, cancellationToken);
}
