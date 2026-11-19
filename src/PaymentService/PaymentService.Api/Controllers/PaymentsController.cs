using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaymentService.Api.Security;
using PaymentService.Application.Interfaces;
using PaymentService.Application.Exceptions;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Api.Controllers;

[ApiController]
[Route("payments")]
[ProducesResponseType(401)]
[ProducesResponseType(403)]
[ProducesResponseType(404)]
public sealed class PaymentsController(IPaymentAppService payments) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = PaymentProtocol.CreatePolicy)]
    [ProducesResponseType(typeof(CreatePaymentResponse), 201)]
    [ProducesResponseType(typeof(CreatePaymentResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Create(CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        if (Request.Headers.TryGetValue(PaymentProtocol.IdempotencyHeader, out var key) && key.ToString() != request.IdempotencyKey)
            throw new ValidationException("Header and body idempotency keys must match.");
        var result = await payments.CreateAsync(request, cancellationToken);
        return StatusCode(result.IsReplay ? 200 : 201, result.Response);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PaymentSecurity.RiderPolicy)]
    [ProducesResponseType(typeof(PaymentDetailResponse), 200)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await payments.GetByIdAsync(id, User.GetUserId(), cancellationToken));

    [HttpPost("{id:guid}/refund")]
    [Authorize(Policy = PaymentProtocol.RefundPolicy)]
    [ProducesResponseType(typeof(RefundPaymentResponse), 200)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Refund(Guid id, RefundPaymentRequest request,
        [FromHeader(Name = PaymentProtocol.IdempotencyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken)
        => Ok(await payments.RefundAsync(id, request, idempotencyKey ?? "", cancellationToken));

    [HttpGet("by-trip/{tripId:guid}")]
    [Authorize(Policy = PaymentSecurity.ByTripPolicy)]
    [ProducesResponseType(typeof(PaymentDetailResponse), 200)]
    public async Task<IActionResult> ByTrip(Guid tripId, CancellationToken cancellationToken)
    {
        var internalRead = PaymentSecurity.IsService(User, PaymentProtocol.ReadScope);
        var result = internalRead
            ? await payments.FindByTripInternalAsync(tripId, cancellationToken)
            : await payments.GetByTripAsync(tripId, User.GetUserId(), User.FindFirst("role")!.Value, cancellationToken);
        return result is null ? NotFound(new { code = PaymentErrorCodes.PaymentNotFound, message = "Payment not found." }) : Ok(result);
    }

    [HttpGet("refunds/by-key/{key}")]
    [Authorize(Policy = PaymentProtocol.ReadInternalPolicy)]
    [ProducesResponseType(typeof(RefundReceiptStatusResponse), 200)]
    public async Task<IActionResult> RefundByKey(string key, CancellationToken cancellationToken)
    {
        var receipt = await payments.FindRefundByKeyInternalAsync(key, cancellationToken);
        return receipt is null ? NotFound() : Ok(receipt);
    }
}
