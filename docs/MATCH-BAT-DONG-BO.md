# Tìm tài xế qua RabbitMQ + Outbox

## Luồng đặt xe

1. Báo giá vẫn đi theo `QuoteRequested` → `QuoteCalculated` như bước trước.
2. Rider confirm accept=true: Trip lưu phương thức thanh toán, fare Accepted,
   trạng thái **FindingDriver**, history và **MatchRequested** cùng transaction.
   HTTP trả **202 Accepted**, chưa có offer.
3. TripOutboxPublisher phát MatchRequested lên RabbitMQ.
4. MatchRequestedConsumer dùng MatchAppService tìm tài xế Available gần nhất,
   bỏ qua danh sách tài xế đã mời. Không có tài xế là kết quả nghiệp vụ MatchFailed;
   lỗi DB/kết nối được throw để retry, không biến thành MatchFailed.
5. Matching lưu kết quả và Outbox nhúng trong **MongoDB `matchResults`**, một insert
   nguyên tử. `_id = MatchRequested.EventId`; hash request phát hiện việc tái sử dụng ID
   với payload khác. Duplicate giữ nguyên kết quả đầu tiên, không chọn lại tài xế.
6. MatchOutboxPublisher quét mỗi giây, batch tối đa 50, claim nguyên tử từng document
   bằng FindOneAndUpdate, lease 2 phút. Publish confirm xong mới đánh dấu PublishedAt.
7. Trip nhận DriverMatched, kiểm tra request gốc, TripId, CausationId, AttemptNo và
   danh sách loại trừ. Khóa dòng Trip và lưu offer + DriverOffered + history cùng transaction.
   MatchFailed hợp lệ chuyển FindingDriver thành Failed.
8. Client poll chi tiết chuyến; tài xế chỉ thấy offer sau khi Trip thành DriverOffered.

## Tài xế từ chối

`POST /trips/{id}/driver-reject` lưu offer Rejected. Nếu đã đạt MaxOfferAttempts,
Trip chuyển Failed và trả 200. Nếu còn lượt, Trip chuyển FindingDriver, lưu
MatchRequested mới trong cùng transaction và trả 202.

AttemptNo = số offer đã có + 1; ExcludeDriverIds chứa toàn bộ tài xế từng được mời.
Kết quả trùng cho lượt cũ chỉ được bỏ qua khi khớp offer đã lưu; không tạo offer mới,
không khôi phục offer Rejected hoặc chuyển trạng thái của lượt hiện tại.

Matching tìm **ứng viên**, chưa đặt tài xế Busy/reserve tài xế khi gửi lời mời.
Một tài xế vẫn có thể nhận nhiều offer; khóa chống nhận hai chuyến đồng thời trong
TripRepository ở bước driver-accept vẫn được giữ. TripAccepted mới cập nhật Matching Busy.

## Queue và metadata

| Message | Queue nhận | Dữ liệu |
| --- | --- | --- |
| MatchRequested | matching.match-requested | TripId, AttemptNo, PickupLat/Lng, ExcludeDriverIds |
| DriverMatched | trip.driver-matched | TripId, AttemptNo, DriverId, DriverName, DistanceKm |
| MatchFailed | trip.match-failed | TripId, AttemptNo, Reason |

CorrelationId = TripId. CausationId kết quả = EventId của MatchRequested.
EventId kết quả không đổi khi publisher gửi lại. Reason là NO_DRIVER_AVAILABLE hoặc
VALIDATION_ERROR; dữ liệu hồ sơ tài xế hỏng cần sửa và replay, không bị che thành thiếu tài xế.

## Chờ, hủy và phục hồi

- Rider có thể gọi confirm accept=false khi FindingDriver để hủy (200 Cancelled).
  Kết quả đến sau khi hủy không tạo lời mời.
- Matching offline: request đã vào queue chờ consumer trở lại. Broker offline:
  request/result còn trong Outbox chờ publish lại.
- Hết retry consumer: tin vào `_error`, cần sửa nguyên nhân và replay giữ EventId.
- Chưa có scheduler timeout riêng cho matching; FindingDriver không tự Failed chỉ vì
  hết thời gian chờ của client. Người dùng có thể hủy trong khi chờ.
- Lần triển khai đầu cần khởi động cả Trip và Matching để tạo các queue/binding
  trước khi nhận yêu cầu. Publisher confirm không có nghĩa consumer đã xử lý xong.

## Frontend và Postman

Frontend hiển thị FindingDriver, tự poll và cho phép hủy khi đang tìm xe.
Hai collection WALLET/CARD: 3.4 yêu cầu 202/FindingDriver; **3.5** poll đến DriverOffered
tối đa 30 lần, cách nhau 2 giây. Failed/Cancelled/hết thời gian thì dừng Runner.
Sau đó 4.1 lấy offer đúng TripId của tài xế đang đăng nhập.

Không cần SQL migration: status lưu dạng chuỗi, FindingDriver vừa độ dài hiện có.
Mongo tạo collection/index khi khởi động. Restart Trip + Matching và import lại collection.

TripAppService không còn phụ thuộc IMatchingClient; cả confirm và driver-reject đều dùng Outbox.
REST `/match` và client cũ còn trong source để tương thích, nhưng không còn tham gia workflow Trip.
REST liên service còn dùng cho truy vấn/đối soát; ADR-03 cần bỏ cả `/quote` và `/match`
khỏi danh sách lệnh gọi đồng bộ trong luồng đặt chuyến.
