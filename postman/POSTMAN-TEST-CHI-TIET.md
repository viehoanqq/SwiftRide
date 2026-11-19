# Kiểm thử SwiftRide bằng Postman

Import [collection chuyến đi](SwiftRide-EndToEnd.postman_collection.json), [collection refund](SwiftRide-RefundSaga.postman_collection.json) và [environment](SwiftRide-Gateway.postman_environment.json), chọn environment **SwiftRide - API Gateway (Local)**. Collection chuyến đi chỉ có folder `00`–`05` cho WALLET chuẩn; collection refund chạy riêng sau khi chuyến đã thanh toán.

## Chuẩn bị

Chạy RabbitMQ, SQL Server, PostgreSQL, MongoDB, bốn service và Gateway. Áp dụng migrations và seed. `GET {{gatewayUrl}}/health` phải trả 200 với cả bốn service Healthy. Seed mặc định: Rider `0900000001` (ví 500.000), Rider `0900000002` (ví 20.000), Driver `0910000001`, mật khẩu `123456`. Sửa biến environment nếu dữ liệu khác. Gateway mặc định `http://localhost:5000`.

Identity chỉ khi chạy ở **Development** sẽ tạo Admin mẫu `0990000000` / `123456` lúc khởi động (idempotent; không tạo lại nếu số điện thoại đã tồn tại). Khởi động lại Identity sau khi cập nhật code, import lại environment và collection refund, rồi chạy request `6.0 Login demo Admin`: Postman tự lưu JWT vào biến secret `adminToken`. Có thể xác nhận response `user.role = "Admin"`. API đăng ký công khai vẫn chỉ nhận Rider/Driver; môi trường Production không tạo Admin mẫu.

## Luồng WALLET thành công

| Bước | Gọi | Xác minh |
|---|---|---|
| 00 | `GET /health` | 200, tất cả Healthy |
| 1.1, 1.3 | Đăng nhập Rider và Driver | 200; tự lưu `riderToken`, `driverToken` và ID |
| 2.1 | Driver `POST /availability/online` | 200, vị trí gần điểm đón |
| 3.1 | Rider `GET /wallets/me` | Ghi lại số dư ban đầu |
| 3.2 | Rider `POST /trips` | 201, `Quoted`, `price > 0`; tự lưu `tripId` |
| 3.4 | Rider confirm với `{"accept":true,"paymentMethod":"WALLET"}` | 200, `DriverOffered`; không gửi `cardToken` |
| 4.1 | Driver `GET /drivers/me/offers` | Offer phải có đúng `tripId`; nếu không, đăng nhập Driver được mời và gọi lại |
| 4.2–4.5 | Accept → en-route → pickup | `DriverAccepted` → `EnRoute` → `PickedUp` |
| 4.6 | Driver dropoff | 200, thường `PaymentPending` |
| 5.1 | Rider `GET /trips/{{tripId}}` | Poll đến `Paid`, history có `PaymentPending` và `Paid` |
| 5.2 | Rider `GET /payments/by-trip/{{tripId}}` | Poll đến 200 `SUCCEEDED`; tự lưu `paymentId` |
| 5.3–5.5 | Xem payment, ví Rider, ví Driver | Rider bị trừ cước, Driver được cộng theo tỷ lệ cấu hình; xem ledger |

`4.6` chỉ ghi Trip, Saga và lệnh `ProcessPayment` vào Trip outbox. Publisher gửi lên `payment.process-payment`; Payment xử lý rồi phát `PaymentSettled`/`PaymentFailed`; Trip nhận kết quả và đóng chuyến. Vì vậy `PaymentPending` ngay sau dropoff và `404` ở bước 5.2 **trong lúc chờ** là bình thường. Poll cách nhau vài giây; 404 không phải kết quả cuối của ca thành công. `5.6` kiểm Gateway chặn `POST /payments` với 403 `INTERNAL_ENDPOINT_ONLY`.

## CARD giả lập: ba ca riêng

Chỉ chạy ở Development/Testing. Bật `Payment__FakeCard__Enabled=true` cho Payment Service, khởi động lại; tạo **chuyến mới cho mỗi token**. Ở bước 3.4, dùng body `{"accept":true,"paymentMethod":"CARD","cardToken":"test-card-approved"}` rồi đổi token theo bảng:

| Token | Trip / Payment cuối | Kiểm thêm |
|---|---|---|
| `test-card-approved` | `Paid` / `SUCCEEDED` | Ví Rider không bị trừ; ledger Driver/Platform ghi phần chia |
| `test-card-declined` | `PaymentFailed` / `FAILED` | Không có bút toán thu thành công; Matching giải phóng đúng Driver |
| `test-card-unknown` | `PaymentPending` / `PENDING` | Sau deadline mặc định 120 giây, Saga thành `PaymentUncertain`, không tự thất bại hoặc refund |

`PaymentUncertain` là trạng thái **nội bộ** trong `TripService.dbo.PaymentSagas`; `GET /trips/{id}` vẫn trả `PaymentPending`. Kiểm DB hoặc log Trip để xác nhận. Không chạy ca unknown trong Runner chung vì nó để lại chuyến đang chờ.

## Lỗi, tắt service và idempotency

- **Ví thiếu tiền:** đăng nhập Rider `0900000002`, tạo chuyến WALLET có `price` lớn hơn số dư rồi chạy từ 3.2. Poll đến Trip `PaymentFailed`, Payment `FAILED`. Không có khoản trừ ví. Kiểm giá trước confirm vì quy tắc giá có thể đổi.
- **Payment offline:** tắt Payment, giữ Trip/Matching/RabbitMQ chạy, dropoff. Trip ở `PaymentPending`; quá deadline Saga chuyển `PaymentUncertain`, Trip vẫn `PaymentPending`. Bật Payment lại và poll 5.1/5.2 đến kết quả đã xác minh. Timeout không tương đương thanh toán thất bại.
- **Dropoff lặp:** gọi lại 4.6 cùng `tripId`, rồi xem 5.2. Không tạo payment thứ hai; `paymentId` giữ nguyên.
- **Sai Driver:** bước 4.1 phải có offer đúng `tripId`. Collection không lấy offer khác để chạy tiếp; Driver không được mời phải bị từ chối.

## Refund Saga (collection riêng, folder 06)

Chỉ bắt đầu sau khi Trip `Paid` và Payment `SUCCEEDED`. Chạy `6.0 Login demo Admin` để lấy `adminToken`, rồi gọi:

```http
POST {{gatewayUrl}}/trips/{{tripId}}/refund-approval
Authorization: Bearer {{adminToken}}
Content-Type: application/json

{"reasonCode":"APPROVED_CUSTOMER_REFUND","evidenceReference":"case-postman-001"}
```

`reasonCode` chấp nhận `APPROVED_CUSTOMER_REFUND` hoặc `INVALID_TRIP_CHARGE`; evidence không rỗng, tối đa 200 ký tự. Lần đầu trả 200 `RefundPending`: Trip lưu quyết định duyệt, Saga, history và lệnh `RefundPayment` cùng transaction. Poll Trip đến `Refunded`, Payment đến `REFUNDED`; kiểm ledger hoàn theo giao dịch gốc. CARD hoàn qua gateway giả lập, WALLET hoàn về ví Rider.

Gọi lại **cùng reason/evidence khi còn RefundPending** trả `RefundPending` và không hoàn lần hai. Rider gọi endpoint trả 403; không có token trả 401. Nếu payment chưa thành công, Admin nhận 409 `REFUND_NOT_READY`. Khi refund thất bại xác định, Trip thành `RefundNeedsAttention`; Admin có thể thử lại cùng reason/evidence sau khi xử lý nguyên nhân. Nếu chỉ timeout, Saga tiếp tục đối soát; không kết luận refund thất bại.

`POST /payments/{id}/refund` trực tiếp bị Gateway chặn và không phải luồng Saga. Refund Saga đi qua duyệt tại Trip, queue `payment.refund-payment`, rồi kết quả ở `trip.payment-refunded` hoặc `trip.payment-refund-failed`.

## Chạy Runner và tra lỗi

Runner collection chuyến đi: đặt **delay 2000 ms**. Trước request 5.1, script chờ Trip thành `Paid` hoặc `PaymentFailed` bằng request nền, tối đa 30 lần cách nhau 2 giây. Request 5.1 chính chỉ pass với `Paid` trong ca WALLET chuẩn. Trước request 5.2, script chờ Payment xuất hiện tương tự; request 5.2 chính chỉ pass khi HTTP 200, đúng `tripId` và `SUCCEEDED`. Nếu hết thời gian vẫn là `404 PAYMENT_NOT_FOUND`, test sẽ fail; không còn báo pass cho 404. Bước 5.2 in `status`, `failureReason`, `paymentId` vào Postman Console. Chạy collection refund riêng với Admin sau khi chuyến `Paid`. Mỗi lần chạy tạo chuyến mới; bước 3.2 xóa `paymentId` cũ. Nếu 5.2 báo `FAILED`, đọc `failureReason`: ví Rider có thể đã bị trừ bởi các lần chạy trước, dù seed ban đầu có 500.000.

Tra log theo `tripId` (CorrelationId): Trip outbox → `payment.process-payment` → Payment checkout/outbox → `trip.payment-settled` hoặc `trip.payment-failed` → Trip Saga → `TripClosed` → Matching. Sau retry giới hạn, MassTransit đưa message lỗi vào queue `_error`; kiểm nguyên nhân trước khi phát lại.
