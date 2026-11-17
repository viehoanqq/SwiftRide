using SwiftRide.Contracts.Events;

namespace SwiftRide.Messaging;

internal static class IntegrationEventDescriptions
{
    public static string Name(IIntegrationEvent message) => message switch
    {
        UserRegistered => "Người dùng đã đăng ký; Payment tạo ví",
        DriverRegistered => "Tài xế đã đăng ký; Matching tạo hồ sơ tài xế",
        TripAccepted => "Tài xế đã nhận chuyến; Matching đánh dấu bận",
        RideCompleted => "Chuyến đã trả khách; Payment bắt đầu thanh toán",
        PaymentSettled => "Thanh toán thành công; Trip đánh dấu đã thanh toán",
        PaymentFailed => "Thanh toán thất bại; Trip đánh dấu thanh toán thất bại",
        TripClosed => "Chuyến đã đóng; Matching giải phóng tài xế của chuyến",
        _ => message.GetType().Name
    };

    public static string Subject(IIntegrationEvent message, Guid? tripId, Guid? userId) =>
        tripId is Guid trip ? $"chuyến {trip:D}" :
        userId is Guid user ? $"người dùng {user:D}" :
        $"luồng {message.CorrelationId:D}";
}
