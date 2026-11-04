namespace TripService.Domain.Exceptions;

// Ném ra khi chuyển trạng thái không hợp lệ (vd: hủy chuyến đã Cancelled). Middleware đổi thành 409.
public class InvalidTripStateException(string message) : Exception(message);
