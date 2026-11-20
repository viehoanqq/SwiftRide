# Kiểm thử SwiftRide bằng Postman

Import [collection WALLET](SwiftRide-EndToEnd.postman_collection.json), [collection CARD](SwiftRide-Card-EndToEnd.postman_collection.json), [collection refund](SwiftRide-RefundSaga.postman_collection.json) và [environment](SwiftRide-Gateway.postman_environment.json), chọn environment **SwiftRide - API Gateway (Local)**. Mỗi collection chạy riêng; refund chỉ bắt đầu sau khi chuyến đã thanh toán.

## Chuẩn bị

**Confirm WALLET kiểm tra ví tại backend:** thiếu tiền trả 409 INSUFFICIENT_FUNDS và giữ Quoted;
không chuyển sang FindingDriver. Báo giá chưa confirm thành công sẽ tự hủy sau 300 giây,
theo cấu hình Trip trong appsettings. Xem [kiểm tra ví và hết hạn](../docs/WALLET-CONFIRM-VA-HET-HAN-BAO-GIA.md).

API hủy chuyến có [collection riêng](SwiftRide-CancelTrip.postman_collection.json) và
[hướng dẫn từng ca](POSTMAN-HUY-CHUYEN.md): Requested, Quoted, FindingDriver, quyền truy cập,
hủy lặp không ghi trùng history và 409 ngoài phạm vi. Chạy riêng từng folder, không ghép vào luồng thanh toán.

Chạy RabbitMQ, SQL Server, PostgreSQL, MongoDB, bốn service và Gateway. Áp dụng migrations và seed. `GET {{gatewayUrl}}/health` phải trả 200 với cả bốn service Healthy. Seed mặc định: Rider `0900000001` (ví 500.000), Rider `0900000002` (ví 20.000), Driver `0910000001`, mật khẩu `123456`. Sửa biến environment nếu dữ liệu khác. Gateway mặc định `http://localhost:5000`.

Để cấp Admin thật, đặt `Identity__AdminBootstrap__Phone` và `Identity__AdminBootstrap__Password` (tối thiểu 12 ký tự) trong cấu hình bảo mật của Identity, rồi khởi động lại. Bootstrap tạo tài khoản một lần ở mọi môi trường sau migration; nếu số điện thoại đã thuộc Rider/Driver, startup từ chối nâng quyền. Sau khi tạo, bỏ hai biến bootstrap khỏi cấu hình và khởi động lại; tài khoản Admin đã lưu trong DB. Không commit mật khẩu vào Git. Tài khoản mẫu cũ `0990000000 / 123456`, nếu đã được tạo bởi phiên bản trước, sẽ bị vô hiệu hóa; có thể dùng cùng số điện thoại với mật khẩu bootstrap mới để thay thế. Nhập cùng số điện thoại/mật khẩu vào biến Postman `adminPhone` và `adminPassword` (secret), chạy `6.0 Login Admin`; response phải có `user.role = "Admin"` và script tự lưu `adminToken`. API đăng ký công khai vẫn chỉ nhận Rider/Driver.

Postman **không tự đọc** `.env` của service. Nếu muốn import sẵn thông tin Admin vào Postman, chạy `python postman/build_local_environment.py` rồi import `postman/SwiftRide-Gateway.local.postman_environment.json`; file này được Git bỏ qua. Chọn environment **SwiftRide - API Gateway (Local private)** trước khi chạy refund. Nếu `6.0` trả 401, kiểm tra Identity đã được khởi động lại sau khi đặt bootstrap và tài khoản Admin đã tồn tại.

## Luồng WALLET thành công

| Bước | Gọi | Xác minh |
|---|---|---|
| 00 | `GET /health` | 200, tất cả Healthy |
| 1.1, 1.3 | Đăng nhập Rider và Driver | 200; tự lưu `riderToken`, `driverToken` và ID |
| 2.1 | Driver `POST /availability/online` | 200, vị trí gần điểm đón |
| 3.1 | Rider `GET /wallets/me` | Ghi lại số dư ban đầu |
| 3.2 | Rider `POST /trips` | 202, `Requested`, giá null; tự lưu `tripId` |
| 3.3 | Rider `GET /trips/{{tripId}}` | Poll tối đa 30 lần cách nhau 2 giây đến `Quoted`, có fare; dừng nếu Failed/hết thời gian |
| 3.4 | Rider confirm với `{"accept":true,"paymentMethod":"WALLET"}` | 202, `FindingDriver`; không gửi `cardToken` |
| 3.5 | Rider `GET /trips/{{tripId}}` | Poll đến `DriverOffered`, tối đa 30 lần cách nhau 2 giây; dừng nếu Failed/Cancelled/hết thời gian |
| 4.1 | Driver `GET /drivers/me/offers` | Offer phải có đúng `tripId`; nếu không, đăng nhập Driver được mời và gọi lại |
| 4.2–4.5 | Accept → en-route → pickup | `DriverAccepted` → `EnRoute` → `PickedUp` |
| 4.6 | Driver dropoff | 200, thường `PaymentPending` |
| 5.1 | Rider `GET /trips/{{tripId}}` | Poll đến `Paid`, history có `PaymentPending` và `Paid` |
| 5.2 | Rider `GET /payments/by-trip/{{tripId}}` | Poll đến 200 `SUCCEEDED`; tự lưu `paymentId` |
| 5.3–5.5 | Xem payment, ví Rider, ví Driver | Rider bị trừ cước, Driver được cộng theo tỷ lệ cấu hình; xem ledger |

`4.6` chỉ ghi Trip, Saga và lệnh `ProcessPayment` vào Trip outbox. Publisher gửi lên `payment.process-payment`; Payment xử lý rồi phát `PaymentSettled`/`PaymentFailed`; Trip nhận kết quả và đóng chuyến. Vì vậy `PaymentPending` ngay sau dropoff và `404` ở bước 5.2 **trong lúc chờ** là bình thường. Poll cách nhau vài giây; 404 không phải kết quả cuối của ca thành công. `5.6` kiểm Gateway chặn `POST /payments` với 403 `INTERNAL_ENDPOINT_ONLY`.

**Không chạy riêng folder 04/05 với `tripId` rỗng.** Bước 3.2 phải trả `202` và lưu `tripId`; bước 3.3 phải chờ `Quoted`; bước 3.4 phải trả `202/FindingDriver`; bước 3.5 phải chờ `DriverOffered`; bước 4.1 phải có offer đúng chuyến cho Driver đang đăng nhập. Collection dừng Runner ngay khi một điều kiện này không đạt. `GET /drivers/me/current-trip` có thể trả một chuyến cũ của Driver, vì vậy không dùng ID đó để thay cho `tripId` của chuyến mới.

Báo giá dùng RabbitMQ + Outbox, xem [luồng và xử lý lỗi](../docs/QUOTE-BAT-DONG-BO.md). Matching ngừng thì Trip tiếp tục Requested; hết thời gian chờ Postman không tự biến chuyến thành Failed.

**Hủy chuyến riêng:** gọi `POST {{gatewayUrl}}/trips/{{tripId}}/cancel`, Bearer `{{riderToken}}`, không cần body.
Chỉ chủ chuyến có quyền hủy ở Requested, Quoted hoặc FindingDriver. Thành công trả 200 `{"status":"Cancelled"}`;
gọi lại vẫn trả 200 và không ghi thêm history. Rider khác trả 403, không tìm thấy chuyến trả 404,
DriverOffered trở đi trả 409. Nếu kết quả matching vừa cập nhật đồng thời, đọc lại trạng thái sau 409.
Chạy ca hủy riêng; không tiếp tục các bước nhận chuyến/thanh toán của collection với chuyến đã hủy.

## CARD giả lập: ba ca riêng

Collection CARD đã có đủ chuỗi tạo chuyến → confirm CARD → nhận chuyến → dropoff → kiểm Payment. Gateway CARD hiện tại là **giả lập**, chỉ được đăng ký ở Development/Testing. Bật `Payment__FakeCard__Enabled=true` cho Payment Service, khởi động lại; tạo **chuyến mới cho mỗi token**. Trong environment, đặt `cardToken`, `expectedTripStatus`, `expectedPaymentStatus` theo bảng rồi chạy collection CARD:

Collection CARD mặc định dùng Rider 02 (`0900000002`) và Driver 02 (`0910000002`) qua `cardRiderPhone`/`cardDriverPhone`. Điều này giúp chạy ca mới khi Rider 01/Driver 01 còn gắn với chuyến `PaymentPending` từ lượt thử cũ. Collection cũng có giá trị mặc định `test-card-approved` và chặn confirm nếu `cardToken` thiếu hoặc sai; không gửi nguyên văn `{{cardToken}}` vào event. Nếu hai tài khoản này cũng đang có chuyến, phải đối soát chuyến đó trước khi chạy lại.

Nếu `Payment__FakeCard__Enabled=false`, consumer không tạo Payment: Trip sẽ kẹt `PaymentPending`, `GET /payments/by-trip` trả 404 và message lỗi có thể vào `payment.process-payment_error`. Sau khi bật cấu hình và khởi động lại Payment, tạo **chuyến mới** để kiểm tra; lần chạy cũ không tự trở thành giao dịch CARD thành công. Collection dùng `await pm.sendRequest` để chờ từng kết quả, thay cho callback chạy nền khiến Runner kiểm tra quá sớm.

| `cardToken` | `expectedTripStatus` | `expectedPaymentStatus` | Kiểm thêm |
|---|---|---|---|
| `test-card-approved` | `Paid` | `SUCCEEDED` | Ví Rider không bị trừ; ledger Driver/Platform ghi phần chia |
| `test-card-declined` | `PaymentFailed` | `FAILED` | Không có bút toán thu thành công; Matching giải phóng đúng Driver |
| `test-card-unknown` | `PaymentPending` | `PENDING` | Sau deadline mặc định 120 giây, Saga thành `PaymentUncertain`, không tự thất bại hoặc refund |

`PaymentUncertain` là trạng thái **nội bộ** trong `TripService.dbo.PaymentSagas`; `GET /trips/{id}` vẫn trả `PaymentPending`. Kiểm DB hoặc log Trip để xác nhận. Ca unknown để lại chuyến đang chờ, vì thế chỉ chạy khi chủ động kiểm tra timeout. CARD trên Production cần adapter cổng thanh toán thật; collection này kiểm tra hợp đồng và gateway giả lập hiện có, không chứng minh đã tích hợp ngân hàng.

## Lỗi, tắt service và idempotency

- **Ví thiếu tiền trước confirm:** tạo chuyến có giá lớn hơn số dư rồi confirm WALLET: 409 INSUFFICIENT_FUNDS, Trip vẫn Quoted, không phát yêu cầu tìm tài xế. Sau hạn 5 phút Trip tự Cancelled. Checkout sau dropoff vẫn có thể thất bại nếu số dư giảm sau khi confirm; đây là ca khác với thiếu tiền ngay từ đầu.
- **Payment offline:** tắt Payment, giữ Trip/Matching/RabbitMQ chạy, dropoff. Trip ở `PaymentPending`; quá deadline Saga chuyển `PaymentUncertain`, Trip vẫn `PaymentPending`. Bật Payment lại và poll 5.1/5.2 đến kết quả đã xác minh. Timeout không tương đương thanh toán thất bại.
- **Dropoff lặp:** gọi lại 4.6 cùng `tripId`, rồi xem 5.2. Không tạo payment thứ hai; `paymentId` giữ nguyên.
- **Sai Driver:** bước 4.1 phải có offer đúng `tripId`. Collection không lấy offer khác để chạy tiếp; Driver không được mời phải bị từ chối.

## Refund Saga (collection riêng, folder 06)

Chỉ bắt đầu sau khi Trip `Paid` và Payment `SUCCEEDED` ở WALLET hoặc CARD. Chạy `6.0 Login Admin` để lấy `adminToken`, rồi gọi:

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
