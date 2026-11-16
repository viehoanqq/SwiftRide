using PaymentService.Application.DTOs;
using PaymentService.Application.Interfaces;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Application.Services;

public class PaymentAppService : IPaymentAppService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IPaymentMethodFactory _paymentMethodFactory;
    private readonly ILedgerService _ledgerService;
    private readonly IUnitOfWork _unitOfWork;

    public PaymentAppService(
        IPaymentRepository paymentRepository,
        IAccountRepository accountRepository,
        IPaymentMethodFactory paymentMethodFactory,
        ILedgerService ledgerService,
        IUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
        _accountRepository = accountRepository;
        _paymentMethodFactory = paymentMethodFactory;
        _ledgerService = ledgerService;
        _unitOfWork = unitOfWork;
    }

    public async Task<PaymentCreationResult> CreateAsync(CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        return await _unitOfWork.ExecuteAsync(async ct =>
        {
            if (!string.IsNullOrEmpty(request.IdempotencyKey))
            {
                var existingPayment = await _paymentRepository.GetByIdempotencyKeyAsync(request.IdempotencyKey, ct);
                if (existingPayment != null)
                {
                    return new PaymentCreationResult(
                        new CreatePaymentResponse(existingPayment.Id, (PaymentState)existingPayment.Status),
                        true
                    );
                }
            }

            var payment = new Payment
            {
                Id = Guid.NewGuid(),
                TripId = request.TripId,
                RiderId = request.RiderId,
                DriverId = request.DriverId,
                Amount = request.Amount,
                Method = (PaymentMethodType)request.Method,
                IdempotencyKey = request.IdempotencyKey,
                CardToken = request.CardToken,
                Status = PaymentStatus.Pending,
                AttemptCount = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            payment.BeginAttempt();
            await _paymentRepository.AddAsync(payment, ct);

            var strategy = _paymentMethodFactory.GetStrategy(payment.Method);
            bool success = await strategy.ProcessPaymentAsync(payment, ct);

            if (success)
            {
                payment.MarkSucceeded();
            }
            else
            {
                string reason = payment.Method == PaymentMethodType.Wallet ? "INSUFFICIENT_FUNDS" : "CARD_DECLINED";
                payment.MarkFailed(reason);
            }

            return new PaymentCreationResult(
                new CreatePaymentResponse(payment.Id, (PaymentState)payment.Status),
                false
            );
        }, cancellationToken);
    }

    public async Task<PaymentDetailResponse> GetByIdAsync(Guid paymentId, Guid riderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);
        if (payment == null || payment.RiderId != riderId)
        {
            throw new KeyNotFoundException("Không tìm thấy thông tin thanh toán hoặc không có quyền truy cập.");
        }

        return await MapToDetailResponseAsync(payment, cancellationToken);
    }

    public async Task<PaymentDetailResponse> GetByTripAsync(Guid tripId, Guid callerId, string role, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.GetByTripIdAsync(tripId, cancellationToken);
        if (payment == null)
        {
            throw new KeyNotFoundException("Không tìm thấy giao dịch thanh toán cho chuyến đi này.");
        }

        return await MapToDetailResponseAsync(payment, cancellationToken);
    }

    public async Task<PaymentDetailResponse> GetByTripIdAsync(Guid tripId, Guid callerId, string role, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.GetByTripIdAsync(tripId, cancellationToken);
        if (payment == null)
        {
            throw new KeyNotFoundException("Không tìm thấy thông tin thanh toán cho chuyến đi này.");
        }

        return await MapToDetailResponseAsync(payment, cancellationToken);
    }

    public async Task<PaymentDetailResponse?> FindByTripInternalAsync(Guid tripId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.GetByTripIdAsync(tripId, cancellationToken);
        if (payment == null) return null;

        return await MapToDetailResponseAsync(payment, cancellationToken);
    }

    public async Task<RefundPaymentResponse> RefundAsync(Guid paymentId, RefundPaymentRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        return await _unitOfWork.ExecuteAsync(async ct =>
        {
            var payment = await _paymentRepository.GetByIdAsync(paymentId, ct);
            if (payment == null || payment.Status != PaymentStatus.Succeeded)
            {
                throw new InvalidOperationException("Giao dịch không tồn tại hoặc không đủ điều kiện để hoàn tiền.");
            }

            await _ledgerService.RecordRefundAsync(payment, ct);
            payment.MarkRefunded();

            return new RefundPaymentResponse(
                (PaymentState)payment.Status
            );
        }, cancellationToken);
    }

    private async Task<PaymentDetailResponse> MapToDetailResponseAsync(Payment payment, CancellationToken cancellationToken)
    {
        decimal driverAmount = 0m;
        decimal platformAmount = 0m;

        if (payment.Status == PaymentStatus.Succeeded)
        {
            var driverAccount = await _accountRepository.GetByOwnerAsync(
                payment.DriverId.ToString(),
                OwnerType.Driver,
                cancellationToken
            );

            if (driverAccount != null && payment.Entries != null && payment.Entries.Any())
            {
                driverAmount = payment.Entries
                    .Where(e => e.EntryType == (EntryType)LedgerEntryType.CHARGE && e.AccountId == driverAccount.Id)
                    .Sum(e => e.Amount);

                platformAmount = payment.Entries
                    .Where(e => e.EntryType == (EntryType)LedgerEntryType.CHARGE
                             && e.AccountId != driverAccount.Id
                             && e.Amount > 0)
                    .Sum(e => e.Amount);
            }

            if (driverAmount == 0m)
            {
                driverAmount = Math.Round(payment.Amount * 0.8m, 2);
                platformAmount = payment.Amount - driverAmount;
            }
        }

        return new PaymentDetailResponse(
            payment.Id,
            payment.TripId,
            payment.Amount,
            (PaymentMethod)payment.Method,
            (PaymentState)payment.Status,
            payment.AttemptCount,
            payment.FailureReason,
            driverAmount,
            platformAmount
        );
    }
}