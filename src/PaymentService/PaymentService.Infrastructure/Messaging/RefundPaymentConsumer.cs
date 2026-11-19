using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using PaymentService.Application.Exceptions;
using PaymentService.Application.Interfaces;
using PaymentService.Application.Services;
using PaymentService.Domain.Exceptions;
using PaymentService.Infrastructure.Persistence;
using SwiftRide.Contracts.Events;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Infrastructure.Messaging;

public sealed class RefundPaymentConsumer(
    RefundAppService refunds,
    IRefundRepository refundRepository,
    IUnitOfWork unitOfWork,
    PayDbContext db) : IConsumer<RefundPayment>
{
    public async Task Consume(ConsumeContext<RefundPayment> context)
    {
        var command = context.Message;
        if (command.EventId == Guid.Empty || command.TripId == Guid.Empty ||
            command.PaymentId == Guid.Empty || command.CorrelationId != command.TripId ||
            command.OccurredAt.Kind != DateTimeKind.Utc ||
            command.SchemaVersion != EventSchema.V1 || command.Amount <= 0 ||
            command.IdempotencyKey != PaymentProtocol.TripRefundKey(command.TripId) ||
            command.ReasonCode is not (SagaRefundReasonCodes.InvalidTripCharge or
                SagaRefundReasonCodes.ApprovedCustomerRefund))
            throw new ArgumentException("Invalid RefundPayment command.");

        try
        {
            await unitOfWork.ExecuteAsync(async ct =>
            {
                // Serialize redeliveries before checking the checkpoint.
                await refundRepository.LockRequestAsync(command.IdempotencyKey, command.PaymentId, ct);
                if (await AlreadyProcessedAsync(command.EventId, ct)) return 0;

                var payment = await db.Payments.AsNoTracking()
                    .SingleOrDefaultAsync(p => p.Id == command.PaymentId, ct);
                if (payment is null)
                {
                    RecordOutcome(new PaymentRefundFailed(Guid.NewGuid(), command.CorrelationId,
                        command.EventId, DateTime.UtcNow, command.TripId, command.PaymentId,
                        SagaRefundFailureCodes.PaymentNotFound));
                    return 0;
                }
                if (payment.TripId != command.TripId || payment.Amount != command.Amount)
                {
                    RecordOutcome(new PaymentRefundFailed(Guid.NewGuid(), command.CorrelationId,
                        command.EventId, DateTime.UtcNow, command.TripId, command.PaymentId,
                        SagaRefundFailureCodes.PaymentMismatch));
                    return 0;
                }

                await refunds.RefundInsideTransactionAsync(command.PaymentId,
                    new RefundPaymentRequest(command.ReasonCode), command.IdempotencyKey, ct);
                RecordOutcome(new PaymentRefunded(Guid.NewGuid(), command.CorrelationId,
                    command.EventId, DateTime.UtcNow, command.TripId, command.PaymentId,
                    command.Amount, command.ReasonCode));
                return 0;
            }, context.CancellationToken);
        }
        catch (PaymentRuleException ex) when (ex.Code is
            PaymentErrorCodes.RefundNotAllowed or PaymentErrorCodes.RefundInsufficientFunds)
        {
            // The attempted refund has rolled back. Commit only a definitive failure.
            await RecordFailureAsync(command, ex.Code, context.CancellationToken);
        }
        catch (NotFoundException ex) when (ex.Code == PaymentErrorCodes.PaymentNotFound)
        {
            await RecordFailureAsync(command, SagaRefundFailureCodes.PaymentNotFound,
                context.CancellationToken);
        }
    }

    private async Task RecordFailureAsync(RefundPayment command, string failureCode,
        CancellationToken cancellationToken)
    {
        await unitOfWork.ExecuteAsync(async ct =>
        {
            await refundRepository.LockRequestAsync(command.IdempotencyKey, command.PaymentId, ct);
            if (await AlreadyProcessedAsync(command.EventId, ct)) return 0;
            RecordOutcome(new PaymentRefundFailed(Guid.NewGuid(), command.CorrelationId,
                command.EventId, DateTime.UtcNow, command.TripId, command.PaymentId, failureCode));
            return 0;
        }, cancellationToken);
    }

    private Task<bool> AlreadyProcessedAsync(Guid messageId, CancellationToken cancellationToken) =>
        db.ProcessedMessages.AnyAsync(m => m.MessageId == messageId, cancellationToken);

    private void RecordOutcome(IIntegrationEvent outcome)
    {
        db.ProcessedMessages.Add(new ProcessedMessage
        {
            MessageId = outcome.CausationId!.Value,
            ProcessedAt = DateTime.UtcNow
        });
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = outcome.EventId,
            Type = outcome.GetType().Name,
            Payload = JsonSerializer.Serialize(outcome, outcome.GetType()),
            CorrelationId = outcome.CorrelationId,
            OccurredAt = outcome.OccurredAt
        });
    }
}
