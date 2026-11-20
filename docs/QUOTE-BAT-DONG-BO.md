# Báo giá bất đồng bộ qua RabbitMQ

## Luồng

1. Rider gọi `POST /trips`. Trip lưu `Requested`, location, history và
   `QuoteRequested` trong SQL Outbox bằng cùng một lần `SaveChanges`.
2. API trả `202 Accepted`, `Location: /trips/{tripId}`, `status: Requested`.
   Các trường giá vẫn là `null`. API không gọi HTTP sang Matching để báo giá.
3. `TripOutboxPublisher` quét mỗi 5 giây, phát `QuoteRequested`.
4. `QuoteRequestedConsumer` tại Matching kiểm tra tài xế quanh điểm đón,
   tính khoảng cách, thời gian và giá bằng `QuoteAppService.CalculateAsync`.
5. Matching lưu kết quả trong collection MongoDB **`quoteResults`**:
   `_id = QuoteRequested.EventId`, hash payload, quote, payload sự kiện kết quả,
   trạng thái publish/claim. Đây là Outbox nhúng: quote và event được ghi nguyên tử
   bằng một insert document, không yêu cầu Mongo replica set/multi-document transaction.
6. `QuoteOutboxPublisher` quét mỗi giây, batch tối đa 50, claim từng document
   bằng `FindOneAndUpdate`, lease 2 phút. Publish confirm thành công mới đánh dấu đã gửi.
7. Trip nhận `QuoteCalculated`, kiểm tra `TripId`, correlation và causation khớp
   request gốc; khóa dòng Trip, lưu fare + `Quoted` + history trong cùng transaction.
   `QuoteFailed` hợp lệ chuyển `Requested` thành `Failed`.
8. Client poll `GET /trips/{tripId}`; chỉ gọi confirm accept=true khi `Quoted`.
   Bước confirm tiếp tục bằng [matching bất đồng bộ](MATCH-BAT-DONG-BO.md).

| Message | Queue |
| --- | --- |
| QuoteRequested | matching.quote-requested |
| QuoteCalculated | trip.quote-calculated |
| QuoteFailed | trip.quote-failed |

Tất cả message dùng metadata chuẩn: EventId, CorrelationId=TripId,
CausationId của kết quả bằng EventId yêu cầu, OccurredAt UTC, SchemaVersion=1.
Giá kết quả được làm tròn 2 chữ số, AwayFromZero, khớp SQL decimal(18,2).

## API và thao tác người dùng

Ví dụ phản hồi tạo chuyến:

```json
{
  "tripId": "...",
  "status": "Requested",
  "price": null,
  "distanceKm": null,
  "durationMin": null,
  "strategy": null
}
```

Frontend đã poll trang chi tiết chuyến và chỉ hiện form xác nhận khi `Quoted`.
Rider có thể hủy khi đang chờ bằng `POST /trips/{id}/cancel`, không cần body.
Kết quả báo giá đến sau khi hủy bị bỏ qua.

Hai collection WALLET/CARD: bước 3.2 kiểm tra 202/Requested, bước 3.3 poll
tối đa 30 lần cách nhau 2 giây. Failed, lỗi HTTP hoặc hết thời gian chờ sẽ dừng
Runner. Hết 60 giây chỉ là giới hạn chờ của Postman, không tự chuyển Trip thành Failed.

## Lỗi và tin trùng

- Không có tài xế hoặc dữ liệu báo giá không hợp lệ: lưu QuoteFailed vào Outbox.
- Lỗi MongoDB/kết nối: consumer throw để MassTransit retry có giới hạn theo cấu hình;
  hết retry vào `_error`. Không đổi lỗi hạ tầng thành kết quả nghiệp vụ thất bại.
- Broker dừng: Outbox chưa gửi vẫn tồn tại; publisher gửi lại khi kết nối phục hồi.
- Matching dừng: yêu cầu đã vào durable queue chờ Matching chạy lại.
- Cùng EventId và payload: dùng lại kết quả đã lưu, không tính một báo giá mới.
  Cùng EventId nhưng payload khác bị từ chối. Hai consumer insert đồng thời:
  unique `_id` chỉ cho một kết quả thắng.
- Crash sau publish trước đánh dấu: có thể gửi lại cùng event; Trip khóa dòng và
  kiểm tra fare/state để không thêm history trùng, không reset fare đã xác nhận.
- Chưa có scheduler timeout riêng cho báo giá. Requested tiếp tục chờ, Rider có thể hủy.
  Nếu message vào `_error`, cần sửa nguyên nhân và replay message.

Khi triển khai lần đầu, chạy Trip và Matching để MassTransit tạo queue/binding
trước khi nhận yêu cầu. Publisher confirm không chứng minh một consumer đã xử lý tin.

## Tương thích

Endpoint REST `/quote` và phương thức client cũ còn giữ cho tương thích, nhưng
`TripAppService.CreateAsync` không sử dụng chúng. Quote qua REST cũ lưu ở `quotes`;
quote của workflow mới nằm trong `quoteResults.quote` cùng Outbox kết quả.
Không cần migration SQL vì dùng trạng thái Requested và bảng Outbox sẵn có.
MongoDB tạo collection/index mới khi Matching khởi động.

ADR-03 cần sửa: REST dùng cho truy vấn đối soát Saga;
báo giá, tìm tài xế, tạo ví/hồ sơ, trạng thái tài xế, thu/hoàn tiền dùng RabbitMQ + Outbox.
Đổi lại, bước xem giá không còn trả kết quả ngay trong HTTP tạo chuyến.
