using Microsoft.AspNetCore.Mvc;
using PaymentService.Application.Interfaces;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Api.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentAppService _paymentAppService;

    public PaymentsController(IPaymentAppService paymentAppService)
    {
        _paymentAppService = paymentAppService;
    }

    [HttpPost]
    public async Task<IActionResult> CreatePayment([FromBody] CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await _paymentAppService.CreateAsync(request, cancellationToken);

        if (result.IsReplay)
        {
            // Gọi lặp cùng IdempotencyKey -> trả HTTP 200
            return Ok(result.Response);
        }

        return StatusCode(StatusCodes.Status201Created, result.Response);
    }
    [HttpPost("internal")]
    public async Task<IActionResult> CreateInternalPayment([FromBody] CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await _paymentAppService.CreateAsync(request, cancellationToken);

        if (result.IsReplay)
        {
            return Ok(result.Response);
        }

        return StatusCode(StatusCodes.Status201Created, result.Response);
    }

    ///  chi tiết thanh toán theo PaymentId
    [HttpGet("{paymentId:guid}")]
    public async Task<IActionResult> GetById(Guid paymentId, [FromQuery] Guid riderId, CancellationToken cancellationToken)
    {
        var response = await _paymentAppService.GetByIdAsync(paymentId, riderId, cancellationToken);
        return Ok(response);
    }

    [HttpPost("{paymentId:guid}/refund")]
    public async Task<IActionResult> Refund(Guid paymentId, [FromBody] RefundPaymentRequest request, [FromHeader(Name = "X-Idempotency-Key")] string idempotencyKey, CancellationToken cancellationToken)
    {
        var response = await _paymentAppService.RefundAsync(paymentId, request, idempotencyKey, cancellationToken);
        return Ok(response);
    }

    /// Lấy chi tiết thanh toán của chuyến đi của /rider/trips/{tripId}
    [HttpGet("trip/{tripId:guid}")]
    public async Task<IActionResult> GetPaymentByTripId(Guid tripId, CancellationToken cancellationToken)
    {
        // Lấy callerId (UserId) và role từ token xác thực hiện tại
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value?? User.FindFirst("sub")?.Value;
        var roleClaim = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value?? User.FindFirst("role")?.Value?? "Rider";

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var callerId))
        {
            return Unauthorized("Không thể xác thực thông tin người dùng từ token.");
        }

        var response = await _paymentAppService.GetByTripIdAsync(tripId, callerId, roleClaim, cancellationToken);
        return Ok(response);
    }
}