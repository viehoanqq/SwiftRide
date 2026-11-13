using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TripService.Api.Controllers;
using TripService.Api.Middleware;
using TripService.Application.DTOs;
using TripService.Application.Interfaces;

namespace TripService.Tests.Api;

public class DriverEndpointTests
{
    [Fact]
    public async Task ConcurrencyException_Returns409WithConflictCode()
    {
        var middleware = new ExceptionMiddleware(
            _ => throw new DbUpdateConcurrencyException("Conflict"),
            NullLogger<ExceptionMiddleware>.Instance);
        var context = new DefaultHttpContext();
        await using var body = new MemoryStream();
        context.Response.Body = body;

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        body.Position = 0;
        using var json = await JsonDocument.ParseAsync(body);
        Assert.Equal("CONCURRENCY_CONFLICT", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetCurrentTrip_NoTrip_Returns204ForAuthenticatedDriver()
    {
        var driver = Guid.NewGuid();
        var workflow = new Mock<ITripDriverWorkflow>();
        var controller = Controller(workflow.Object, driver);

        var response = await controller.GetCurrentTrip(default);

        Assert.IsType<NoContentResult>(response.Result);
        workflow.Verify(w => w.GetCurrentTripAsync(driver, default), Times.Once);
    }

    [Fact]
    public async Task GetOffers_UsesAuthenticatedDriverAndReturns200()
    {
        var driver = Guid.NewGuid();
        var workflow = new Mock<ITripDriverWorkflow>();
        workflow.Setup(w => w.GetOffersAsync(driver, default))
            .ReturnsAsync(Array.Empty<DriverOfferDto>());
        var controller = Controller(workflow.Object, driver);

        var response = await controller.GetOffers(default);

        var result = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<DriverOfferDto>>(result.Value));
        workflow.Verify(w => w.GetOffersAsync(driver, default), Times.Once);
    }

    private static DriverMeController Controller(ITripDriverWorkflow workflow, Guid driver) => new(workflow)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim("sub", driver.ToString()), new Claim("role", "Driver") }, "Test"))
            }
        }
    };
}
