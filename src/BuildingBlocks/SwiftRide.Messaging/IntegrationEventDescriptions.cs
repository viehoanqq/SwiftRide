using SwiftRide.Contracts.Events;

namespace SwiftRide.Messaging;

internal static class IntegrationEventDescriptions
{
    public static string Name(IIntegrationEvent message) => message switch
    {
        QuoteRequested => "Trip yêu cầu Matching tính giá chuyến đi",
        QuoteCalculated => "Matching đã tính giá; Trip cập nhật báo giá",
        QuoteFailed => "Matching từ chối báo giá; Trip đánh dấu thất bại",
        UserRegistered => "Người dùng đã đăng ký; Payment tạo ví",
        DriverRegistered => "Tài xế đã đăng ký; Matching tạo hồ sơ tài xế",
        TripAccepted => "Tài xế đã nhận chuyến; Matching đánh dấu bận",
        RideCompleted => "Chuyến đã trả khách; Payment bắt đầu thanh toán",
        PaymentSettled => "Thanh toán thành công; Trip đánh dấu đã thanh toán",
        PaymentFailed => "Thanh toán thất bại; Trip đánh dấu thanh toán thất bại",
        TripClosed => "Chuyến đã đóng; Matching giải phóng tài xế của chuyến",
        ProcessPayment => "Trip yêu cầu Payment thu tiền chuyến",
        ProcessPaymentTimedOut => "Đến hạn chờ thanh toán; Trip đối soát kết quả",
        RefundPayment => "Trip yêu cầu Payment hoàn khoản thu đã duyệt",
        PaymentRefunded => "Payment đã hoàn tiền; Trip cập nhật chuyến",
        PaymentRefundFailed => "Payment từ chối hoàn tiền; Trip cần hỗ trợ xử lý",
        _ => message.GetType().Name
    };

    public static string Subject(IIntegrationEvent message, Guid? tripId, Guid? userId) =>
        tripId is Guid trip ? $"chuyến {trip:D}" :
        userId is Guid user ? $"người dùng {user:D}" :
        $"luồng {message.CorrelationId:D}";
}
