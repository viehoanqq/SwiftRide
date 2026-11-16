using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaymentService.Api.Security;
using PaymentService.Application.Interfaces;
using PaymentService.Application.Exceptions;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Api.Controllers;

[ApiController]
[Route("payments")]
public sealed class PaymentsController(IPaymentCheckoutService payments) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = PaymentProtocol.CreatePolicy)]
    public async Task<IActionResult> Create(CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        if (Request.Headers.TryGetValue(PaymentProtocol.IdempotencyHeader, out var key) && key.ToString() != request.IdempotencyKey)
            throw new ValidationException("Header and body idempotency keys must match.");
        var result = await payments.CreateAsync(request, cancellationToken);
        return StatusCode(result.IsReplay ? 200 : 201, result.Response);
    }

    [HttpGet("by-trip/{tripId:guid}")]
    [Authorize(Policy = PaymentSecurity.ByTripPolicy)]
    public async Task<IActionResult> ByTrip(Guid tripId, CancellationToken cancellationToken)
    {
        var internalRead = PaymentSecurity.IsService(User, PaymentProtocol.ReadScope);
        var result = await payments.FindByTripAsync(tripId, internalRead ? null : User.GetUserId(),
            internalRead ? null : User.FindFirst("role")?.Value, cancellationToken);
        return result is null ? NotFound(new { code = PaymentErrorCodes.PaymentNotFound, message = "Payment not found." }) : Ok(result);
    }
}
