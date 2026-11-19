using PaymentService.Application.DTOs;
using PaymentService.Application.Exceptions;
using PaymentService.Application.Interfaces;
using PaymentService.Application.Options;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Domain.Rules;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Application.Services;

public sealed class PaymentCheckoutService(
    IPaymentRepository payments, IAccountRepository accounts, IUnitOfWork unitOfWork,
    PaymentOptions options, IPaymentMethodProcessorFactory? processors = null) : IPaymentCheckoutService
{
    public Task<PaymentCreationResult> CreateAsync(CreatePaymentRequest request, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(ct => CreateInsideTransactionAsync(request, ct), cancellationToken);

    // Used by RideCompletedConsumer inside its transaction, so checkout and event bookkeeping commit together.
    public async Task<PaymentCreationResult> CreateInsideTransactionAsync(
        CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        if (request.TripId == Guid.Empty || request.RiderId == Guid.Empty || request.DriverId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100 ||
            !Enum.IsDefined(request.Method) || request.CardToken?.Length > 100)
            throw new ValidationException("Invalid payment request.");
        MoneyRules.ValidateAmount(request.Amount);

        var ct = cancellationToken;
        await payments.LockRequestAsync(request.IdempotencyKey, request.TripId, ct);
        var previous = await payments.GetByIdempotencyKeyAsync(request.IdempotencyKey, ct);
        if (previous is not null)
        {
            if (previous.TripId != request.TripId || previous.RiderId != request.RiderId ||
                previous.DriverId != request.DriverId || previous.Amount != request.Amount ||
                (int)previous.Method != (int)request.Method || previous.CardToken != request.CardToken)
                throw new ConflictException(PaymentErrorCodes.IdempotencyConflict, "Key was used with a different payload.");
            return new PaymentCreationResult(new(previous.Id, State(previous.Status)), true);
        }
        if (await payments.GetByTripIdAsync(request.TripId, ct) is not null)
            throw new ConflictException(PaymentErrorCodes.TripPaymentExists, "Trip already has a payment.");

        var now = DateTime.UtcNow;
        var payment = new Payment
        {
            Id = Guid.NewGuid(), TripId = request.TripId, RiderId = request.RiderId,
            DriverId = request.DriverId, Amount = request.Amount, Method = (PaymentMethodType)request.Method,
            CardToken = request.CardToken,
            IdempotencyKey = request.IdempotencyKey, Status = PaymentStatus.Pending,
            CreatedAt = now, UpdatedAt = now
        };
        var factory = processors ?? new PaymentMethodProcessorFactory([new WalletPaymentProcessor(accounts, options)]);
        await factory.For(request.Method).SettleAsync(payment, request, ct);
        await payments.AddAsync(payment, ct);
        return new PaymentCreationResult(new(payment.Id, State(payment.Status)), false);
    }

    public async Task<PaymentDetailResponse?> FindByTripAsync(Guid tripId, Guid? callerId, string? role, CancellationToken cancellationToken)
    {
        var payment = await payments.GetByTripIdAsync(tripId, cancellationToken);
        if (payment is null) return null;
        if (callerId.HasValue && !((role == "Rider" && payment.RiderId == callerId) ||
            (role == "Driver" && payment.DriverId == callerId)))
            throw new ForbiddenException(PaymentErrorCodes.Forbidden, "Payment belongs to another user.");
        var driver = await accounts.GetByOwnerAsync(payment.DriverId.ToString("D"), OwnerType.Driver, cancellationToken);
        var platform = await accounts.GetByOwnerAsync(Account.PlatformOwnerId, OwnerType.Platform, cancellationToken);
        decimal Received(Guid? id) => payment.Entries.Where(e => e.EntryType == EntryType.Charge && e.AccountId == id).Sum(e => e.Amount);
        return new(payment.Id, payment.TripId, payment.Amount, (PaymentMethod)payment.Method,
            State(payment.Status), payment.AttemptCount, payment.FailureReason, Received(driver?.Id), Received(platform?.Id));
    }

    private static PaymentState State(PaymentStatus status) => status switch {
        PaymentStatus.Pending => PaymentState.PENDING, PaymentStatus.Succeeded => PaymentState.SUCCEEDED,
        PaymentStatus.Failed => PaymentState.FAILED, PaymentStatus.Refunded => PaymentState.REFUNDED,
        _ => throw new InvalidOperationException("Unknown payment status.") };
}
