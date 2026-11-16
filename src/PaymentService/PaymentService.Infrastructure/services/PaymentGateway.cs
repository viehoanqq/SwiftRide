using PaymentService.Application.Services;

namespace PaymentService.Infrastructure.Services;

public class PaymentGateway : IPaymentGateway
{
    public Task<bool> ChargeCardAsync(
        Guid riderId,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        // Giả lập thẻ bị từ chối khi số tiền vượt quá hạn mức 10,000,000 để test case CARD_DECLINED
        if (amount > 10_000_000m)
        {
            return Task.FromResult(false);
        }

        // Các giao dịch hợp lệ trả về true (cổng thanh toán trừ tiền thẻ thành công)
        return Task.FromResult(true);
    }
}