using SwiftRide.Contracts.Payments;
using TripService.Application.Exceptions;
using TripService.Domain.Entities;
using TripService.Domain.Enums;

namespace TripService.Application.Services;

public static class PaymentResultMapping
{
    // Called only after verifying the Payment response belongs to this Trip.
    // Does not perform HTTP, persist changes, or release a driver.
    public static void Apply(Trip trip, PaymentState status)
    {
        ArgumentNullException.ThrowIfNull(trip);
        switch (status)
        {
            case PaymentState.SUCCEEDED:
                trip.MarkPaid();
                break;
            case PaymentState.FAILED:
                trip.MarkPaymentFailed();
                break;
            case PaymentState.PENDING when trip.Status is TripStatus.PaymentPending or TripStatus.Paid or TripStatus.PaymentFailed:
                // Never downgrade a final result on a delayed Pending response.
                break;
            case PaymentState.REFUNDED when trip.Status == TripStatus.Paid:
                // Refund belongs to Payment; no invented Trip status or duplicate history.
                break;
            default:
                throw new DownstreamException(502, "PAYMENT_INVALID_RESPONSE",
                    "Kết quả thanh toán không phù hợp với trạng thái chuyến.");
        }
    }
}
