using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Filters;
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
    [SwaggerRequestExample(typeof(CreateTripRequest), typeof(CreateTripRequestExample))]
    public async Task<ActionResult<CreateTripResponse>> Create(CreateTripRequest request)
    {
        var result = await tripService.CreateAsync(UserId, request);
        return StatusCode(StatusCodes.Status201Created, result);
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
    [SwaggerRequestExample(typeof(ConfirmTripRequest), typeof(ConfirmTripRequestExample))]
    public Task<ConfirmTripResponse> Confirm(Guid id, ConfirmTripRequest request)
        => tripService.ConfirmAsync(id, UserId, request);

    [HttpGet("{id:guid}")]
    public Task<TripDetailDto> GetById(Guid id)
        => tripService.GetByIdAsync(id, UserId, User.FindFirstValue("role")!);
}

public class CreateTripRequestExample : IExamplesProvider<CreateTripRequest>
{
    public CreateTripRequest GetExamples()
    {
        return new CreateTripRequest(
            new LocationDto(10.772, 106.699, "123 Lê Lợi, Q1"),
            new LocationDto(10.801, 106.750, "456 Trần Hưng Đạo, Q5"));
    }
}

public class ConfirmTripRequestExample : IExamplesProvider<ConfirmTripRequest>
{
    public ConfirmTripRequest GetExamples()
    {
        return new ConfirmTripRequest(true, "WALLET");
    }
}
