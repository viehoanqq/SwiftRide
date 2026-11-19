using PaymentService.Application.Exceptions;
using PaymentService.Application.Interfaces;
using SwiftRide.Contracts.Payments;

namespace PaymentService.Application.Services;

public sealed class PaymentMethodProcessorFactory(IEnumerable<IPaymentMethodProcessor> processors)
    : IPaymentMethodProcessorFactory
{
    private readonly IReadOnlyDictionary<PaymentMethod, IPaymentMethodProcessor> _processors =
        processors.ToDictionary(processor => processor.Method);

    public IPaymentMethodProcessor For(PaymentMethod method) =>
        _processors.TryGetValue(method, out var processor)
            ? processor
            : throw new ValidationException($"Payment method {method} is not configured.");
}
