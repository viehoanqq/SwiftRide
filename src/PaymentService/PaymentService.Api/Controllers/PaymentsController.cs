using Microsoft.AspNetCore.Mvc;
using PaymentService.Application.DTOs;
using PaymentService.Application.Services;

namespace PaymentService.Api.Controllers;

[ApiController]
[Route("payments")]
public sealed class PaymentsController(PaymentAppService paymentService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PaymentResponse>> ProcessPayment([FromBody] ProcessPaymentRequest request, CancellationToken ct)
    {
        var result = await paymentService.ProcessPaymentAsync(request, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PaymentDetailResponse>> GetById(Guid id, CancellationToken ct)
    {
        var payment = await paymentService.GetPaymentAsync(id, ct);
        return payment is null ? NotFound() : Ok(payment);
    }

    [HttpGet("by-trip/{tripId:guid}")]
    public async Task<ActionResult<PaymentDetailResponse>> GetByTripId(Guid tripId, CancellationToken ct)
    {
        var payment = await paymentService.GetPaymentByTripIdAsync(tripId, ct);
        return payment is null ? NotFound() : Ok(payment);
    }

    [HttpPost("{id:guid}/refund")]
    public async Task<ActionResult<PaymentDetailResponse>> Refund(Guid id, [FromBody] RefundPaymentRequest request, CancellationToken ct)
    {
        var payment = await paymentService.RefundPaymentAsync(id, request.RiderId, request.DriverId, request.Amount, ct);
        return Ok(payment);
    }
}
