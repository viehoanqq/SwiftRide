using SwiftRide.Contracts.Payments;

namespace TripService.Application.Interfaces;

public interface IPaymentClient
{
    // POST /payments: accept 201(new) and 200(replay). FAILED is a business result, not HTTP failure.
    // Timeout/5xx throw DownstreamException; they MUST NOT be converted into FAILED or null.
    Task<CreatePaymentResponse> CreateAsync(CreatePaymentRequest request, CancellationToken cancellationToken);
    // GET /payments/by-trip/{tripId} with service payments:read scope; 404 only => null.
    Task<PaymentDetailResponse?> GetByTripAsync(Guid tripId, CancellationToken cancellationToken);
}
