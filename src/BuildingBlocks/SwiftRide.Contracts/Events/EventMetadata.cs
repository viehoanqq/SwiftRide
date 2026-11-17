namespace SwiftRide.Contracts.Events;

/// <summary>
/// Metadata chuẩn bắt buộc cho mọi sự kiện tích hợp giữa các microservice (Đợt 6).
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }          // Khóa idempotency cho ProcessedMessages ở phía Consumer
    Guid CorrelationId { get; }    // Truy vết xuyên suốt 1 nghiệp vụ (thường là TripId hoặc UserId)
    Guid? CausationId { get; }     // EventId của sự kiện cha gây ra sự kiện này (null nếu từ HTTP request gốc)
    DateTime OccurredAt { get; }   // Thời điểm phát sinh sự kiện (UTC)
    int SchemaVersion { get; }     // Phiên bản schema payload (mặc định = 1)
}

public static class EventSchema
{
    public const int V1 = 1;
}