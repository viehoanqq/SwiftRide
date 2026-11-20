using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TripService.Application.DTOs;
using TripService.Application.Services;
using TripService.Application.Interfaces;
using TripService.Application.Exceptions;
using TripService.Application.Sagas;
namespace TripService.Api.Controllers;

[ApiController]
[Route("trips")]
[Authorize]
public class TripsController(TripAppService tripService, ITripDriverWorkflow tripWorkflow,
    TripRefundCoordinator refunds) : ControllerBase
{
    private Guid UserId => Guid.TryParse(User.FindFirstValue("sub"), out var id) && id != Guid.Empty
        ? id : throw new UnauthorizedException("INVALID_TOKEN_SUBJECT", "Token kh?ng c? ID ng??i d?ng h?p l?.");

    [HttpPost]
    [ProducesResponseType(typeof(CreateTripResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    [Authorize(Roles = "Rider")]
    public async Task<ActionResult<CreateTripResponse>> Create(CreateTripRequest request)
    {
        var result = await tripService.CreateAsync(UserId, request);
        return AcceptedAtAction(nameof(GetById), new { id = result.TripId }, result);
    }

    [HttpGet("me/current")]
    [ProducesResponseType(typeof(TripDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Authorize(Roles = "Rider")]
    public async Task<ActionResult<TripDetailDto>> GetCurrent()
    {
        var trip = await tripService.GetCurrentAsync(UserId);
        return trip is null ? NoContent() : Ok(trip);
    }

    [HttpPost("{id:guid}/confirm")]
    [ProducesResponseType(typeof(ConfirmTripResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ConfirmTripResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    [Authorize(Roles = "Rider")]
    public async Task<IActionResult> Confirm(Guid id, [FromBody] ConfirmTripRequest request, CancellationToken cancellationToken)
    {
        var result = await tripService.ConfirmAsync(id, UserId, request, cancellationToken);
        return result.Status == "FindingDriver"
            ? AcceptedAtAction(nameof(GetById), new { id }, result) : Ok(result);
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Rider")]
    [ProducesResponseType(typeof(CancelTripResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CancelTripResponse>> Cancel(Guid id)
        => Ok(await tripService.CancelAsync(id, UserId));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TripDetailDto>> GetById(Guid id)
    {
        var trip = await tripService.GetByIdAsync(id, UserId, User.FindFirstValue("role")!);
        return trip is null ? NotFound() : Ok(trip);
    }

    [HttpPost("{id:guid}/driver-accept")]
    [ProducesResponseType(typeof(TripDriverActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    [Authorize(Roles = "Driver")]
    public async Task<ActionResult<TripDriverActionResponse>> AcceptOffer(Guid id, CancellationToken cancellationToken)
    {
        var result = await tripWorkflow.AcceptOfferAsync(id, UserId, cancellationToken);
        return Ok(result); // Trả về 200 OK kèm body chuẩn
    }

    [HttpPost("{id:guid}/driver-reject")]
    [ProducesResponseType(typeof(TripDriverActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TripDriverActionResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    [Authorize(Roles = "Driver")]
    public async Task<ActionResult<TripDriverActionResponse>> RejectOffer(Guid id, CancellationToken cancellationToken)
    {
        var result = await tripWorkflow.RejectOfferAsync(id, UserId, cancellationToken);
        return result.Status == "FindingDriver" ? Accepted(result) : Ok(result);
    }

    [HttpPost("{id:guid}/en-route")]
    [ProducesResponseType(typeof(TripDriverActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Authorize(Roles = "Driver")]
    public async Task<ActionResult<TripDriverActionResponse>> MarkEnRoute(Guid id, CancellationToken cancellationToken)
    {
        var result = await tripWorkflow.MarkEnRouteAsync(id, UserId, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/pickup")]
    [ProducesResponseType(typeof(TripDriverActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Authorize(Roles = "Driver")]
    public async Task<ActionResult<TripDriverActionResponse>> MarkPickedUp(Guid id, CancellationToken cancellationToken)
    {
        var result = await tripWorkflow.MarkPickedUpAsync(id, UserId, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/dropoff")]
    [ProducesResponseType(typeof(TripDriverActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    [Authorize(Roles = "Driver")]
    public async Task<ActionResult<TripDriverActionResponse>> MarkDroppedOff(Guid id, CancellationToken cancellationToken)
    {
        var result = await tripWorkflow.MarkDroppedOffAsync(id, UserId, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/refund-approval")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(TripDriverActionResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<TripDriverActionResponse>> ApproveRefund(Guid id,
        ApproveTripRefundRequest request, CancellationToken cancellationToken)
    {
        var status = await refunds.ApproveAsync(id, UserId, request.ReasonCode,
            request.EvidenceReference, cancellationToken);
        return Ok(new TripDriverActionResponse(status));
    }
}
