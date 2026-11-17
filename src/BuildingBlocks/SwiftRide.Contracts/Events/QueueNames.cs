namespace SwiftRide.Contracts.Events;

/// <summary>
/// Tên queue chuẩn cho từng Consumer. MassTransit sẽ tự động tạo thêm:
/// - {QueueName}_error   : chứa message vượt quá số lần retry hoặc lỗi dữ liệu không thể retry
/// - {QueueName}_skipped : chứa message gửi nhầm vào queue không có consumer đăng ký
/// </summary>
public static class QueueNames
{
    public const string PaymentUserRegistered = "payment.user-registered";
    public const string MatchingDriverRegistered = "matching.driver-registered";
    public const string MatchingTripAccepted = "matching.trip-accepted";
    public const string PaymentRideCompleted = "payment.ride-completed";
    public const string TripPaymentSettled = "trip.payment-settled";
    public const string TripPaymentFailed = "trip.payment-failed";
    public const string MatchingTripClosed = "matching.trip-closed";
}