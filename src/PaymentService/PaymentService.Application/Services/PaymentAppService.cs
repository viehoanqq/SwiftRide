using PaymentService.Application.DTOs;
using PaymentService.Application.Interfaces;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;

namespace PaymentService.Application.Services;

public class PaymentAppService(IPaymentRepository repository, decimal defaultRiderBalance = 500_000m)
{
    private const int MaxAttempts = 3;

    public async Task<PaymentResponse> ProcessPaymentAsync(ProcessPaymentRequest request, CancellationToken ct = default)
    {
        var idempotencyKey = request.TripId.ToString();
        var existing = await repository.GetPaymentByIdempotencyKeyAsync(idempotencyKey, ct);
        if (existing is not null)
        {
            return MapResponse(existing);
        }

        var method = ParseMethod(request.Method);
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TripId = request.TripId,
            IdempotencyKey = idempotencyKey,
            RiderId = request.RiderId,
            DriverId = request.DriverId,
            Amount = request.Amount,
            Method = method,
            CardToken = request.CardToken,
            Status = PaymentStatus.Pending,
            AttemptCount = 0,
            FailureReason = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            payment.AttemptCount = attempt;
            payment.UpdatedAt = DateTime.UtcNow;

            try
            {
                var rider = await repository.GetAccountByOwnerAsync(request.RiderId.ToString(), OwnerType.Rider, ct)
                    ?? await repository.AddAccountAsync(new Account
                    {
                        Id = Guid.NewGuid(),
                        OwnerId = request.RiderId.ToString(),
                        OwnerType = OwnerType.Rider,
                        Balance = defaultRiderBalance
                    }, ct);

                var driver = await repository.GetAccountByOwnerAsync(request.DriverId.ToString(), OwnerType.Driver, ct)
                    ?? await repository.AddAccountAsync(new Account
                    {
                        Id = Guid.NewGuid(),
                        OwnerId = request.DriverId.ToString(),
                        OwnerType = OwnerType.Driver,
                        Balance = 0m
                    }, ct);

                var processor = PaymentMethodFactory.Create(method);
                if (!processor.CanProcess(rider, request.Amount, request.CardToken))
                {
                    payment.Status = PaymentStatus.Failed;
                    payment.FailureReason = method == PaymentMethodType.Wallet ? "Rider balance is insufficient." : "Card token is invalid or missing.";
                    payment.UpdatedAt = DateTime.UtcNow;
                    await repository.AddPaymentAsync(payment, ct);
                    await repository.SaveChangesAsync(ct);
                    return MapResponse(payment);
                }

                rider.Balance -= request.Amount;
                driver.Balance += request.Amount;
                payment.Entries = LedgerService.CreateEntries(payment.Id, rider.Id, driver.Id, request.Amount, EntryType.Charge);
                payment.Status = PaymentStatus.Succeeded;
                payment.FailureReason = null;
                payment.UpdatedAt = DateTime.UtcNow;

                await repository.AddPaymentAsync(payment, ct);
                await repository.UpdateAccountAsync(rider, ct);
                await repository.UpdateAccountAsync(driver, ct);
                await repository.SaveChangesAsync(ct);
                return MapResponse(payment);
            }
            catch (TimeoutException) when (attempt < MaxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), ct);
            }
            catch (InvalidOperationException) when (attempt < MaxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), ct);
            }
            catch (Exception ex) when (attempt == MaxAttempts)
            {
                payment.Status = PaymentStatus.Failed;
                payment.FailureReason = ex.Message;
                payment.UpdatedAt = DateTime.UtcNow;
                try
                {
                    await repository.AddPaymentAsync(payment, ct);
                    await repository.SaveChangesAsync(ct);
                }
                catch
                {
                    // Swallow save failure after a final transient error to keep API resilient.
                }

                return MapResponse(payment);
            }
        }

        payment.Status = PaymentStatus.Failed;
        payment.FailureReason = "Payment processing failed after retries.";
        payment.UpdatedAt = DateTime.UtcNow;
        await repository.AddPaymentAsync(payment, ct);
        await repository.SaveChangesAsync(ct);
        return MapResponse(payment);
    }

    public async Task<PaymentDetailResponse?> GetPaymentAsync(Guid paymentId, CancellationToken ct = default)
    {
        var payment = await repository.GetPaymentByIdAsync(paymentId, ct);
        return payment is null ? null : MapDetail(payment);
    }

    public async Task<PaymentDetailResponse?> GetPaymentByTripIdAsync(Guid tripId, CancellationToken ct = default)
    {
        var payment = await repository.GetPaymentByTripIdAsync(tripId, ct);
        return payment is null ? null : MapDetail(payment);
    }

    public async Task<PaymentDetailResponse> RefundPaymentAsync(Guid paymentId, Guid riderId, Guid driverId, decimal amount, CancellationToken ct = default)
    {
        var payment = await repository.GetPaymentByIdAsync(paymentId, ct)
            ?? throw new KeyNotFoundException($"Payment {paymentId} not found.");

        if (payment.Status == PaymentStatus.Refunded)
        {
            return MapDetail(payment);
        }

        var rider = await repository.GetAccountByOwnerAsync(riderId.ToString(), OwnerType.Rider, ct)
            ?? await repository.AddAccountAsync(new Account
            {
                Id = Guid.NewGuid(),
                OwnerId = riderId.ToString(),
                OwnerType = OwnerType.Rider,
                Balance = 0m
            }, ct);

        var driver = await repository.GetAccountByOwnerAsync(driverId.ToString(), OwnerType.Driver, ct)
            ?? await repository.AddAccountAsync(new Account
            {
                Id = Guid.NewGuid(),
                OwnerId = driverId.ToString(),
                OwnerType = OwnerType.Driver,
                Balance = 0m
            }, ct);

        rider.Balance += amount;
        driver.Balance -= amount;

        payment.Status = PaymentStatus.Refunded;
        payment.UpdatedAt = DateTime.UtcNow;
        payment.Entries ??= [];
        payment.Entries.AddRange(LedgerService.CreateRefundEntries(payment.Id, rider.Id, driver.Id, amount));

        await repository.UpdateAccountAsync(rider, ct);
        await repository.UpdateAccountAsync(driver, ct);
        await repository.SaveChangesAsync(ct);

        return MapDetail(payment);
    }

    private static PaymentMethodType ParseMethod(string method)
    {
        return method.Trim().ToUpperInvariant() switch
        {
            "WALLET" => PaymentMethodType.Wallet,
            "CARD" => PaymentMethodType.Card,
            _ => throw new ArgumentException("Payment method must be WALLET or CARD.", nameof(method))
        };
    }

    private static PaymentResponse MapResponse(Payment payment)
    {
        return new PaymentResponse(
            payment.Id,
            payment.TripId,
            payment.RiderId,
            payment.DriverId,
            payment.Amount,
            payment.Method.ToString(),
            payment.Status.ToString(),
            payment.FailureReason,
            payment.AttemptCount,
            payment.CreatedAt);
    }

    private static PaymentDetailResponse MapDetail(Payment payment)
    {
        return new PaymentDetailResponse(
            payment.Id,
            payment.TripId,
            payment.RiderId,
            payment.DriverId,
            payment.Amount,
            payment.Method.ToString(),
            payment.Status.ToString(),
            payment.FailureReason,
            payment.AttemptCount,
            payment.CreatedAt,
            payment.Entries
                .Select(e => new LedgerEntryDto(e.AccountId, e.Amount, e.EntryType.ToString(), e.CreatedAt))
                .ToList());
    }
}
