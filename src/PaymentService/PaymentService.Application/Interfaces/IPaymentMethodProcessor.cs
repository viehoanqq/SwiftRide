using PaymentService.Domain.Entities;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Application.Interfaces;

public interface IPaymentMethodProcessor
{
    PaymentMethod Method { get; }
    Task SettleAsync(Payment payment, CreatePaymentRequest request, CancellationToken cancellationToken);
}

public interface IPaymentMethodProcessorFactory
{
    IPaymentMethodProcessor For(PaymentMethod method);
}
