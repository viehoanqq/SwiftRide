# WALLET thiếu tiền → Quoted → tự hủy sau 30 giây

Import [SwiftRide-WalletExpiry30s.postman_collection.json](SwiftRide-WalletExpiry30s.postman_collection.json).
Chạy **toàn collection**, Runner **Delay = 0 ms**, 1 iteration. Không áp dụng delay 2000 ms
của các collection E2E cũ vì ca này kiểm tra một hạn ngắn. Script đã tự chờ đúng bước.

## Chuẩn bị

Trong `src/TripService/TripService.Api/appsettings.json`, sửa:

```json
"Trip": {
  "MaxOfferAttempts": 3,
  "QuoteConfirmationTimeoutSeconds": 30,
  "QuoteExpiryPollSeconds": 5,
  "QuoteExpiryBatchSize": 50
}
```

**Restart Trip** sau khi sửa. Kiểm tra không có biến môi trường `Trip__QuoteConfirmationTimeoutSeconds`
ghi đè giá trị này. Đây là hạn xác nhận báo giá, không phải timeout thanh toán của Saga.
Collection không tự sửa cấu hình backend. File appsettings trong repo vẫn giữ mặc định 300 giây
cho đến khi bạn đổi thủ công.

Chạy Payment phiên bản có API kiểm tra ví, Matching, Identity, Gateway, RabbitMQ và database.
Không tắt Payment hoặc Matching trong ca này. Nếu Runner giới hạn thời gian script quá ngắn,
đặt script timeout tối thiểu 60000 ms (Newman: `--timeout-script 60000`).

## Biến collection

| Biến | Mặc định | Ý nghĩa |
| --- | --- | --- |
| gatewayUrl | http://localhost:5000 | Gateway đang chạy |
| expiryRiderPhone | 0900000002 | Rider không có chuyến hoạt động, có ví ít tiền |
| expiryRiderPassword | 123456 | Mật khẩu seed local, đổi theo dữ liệu thực tế |
| expiryDriverPhone | 0910000002 | Driver rảnh để báo giá thành công |
| expiryDriverPassword | 123456 | Mật khẩu seed local |
| expiryDropoffLat | 10.95 | Điểm đến xa để giá lớn hơn ví seed 20.000 |
| expiryDropoffLng | 106.82 | Kinh độ điểm đến |

Token, expiryTripId, balance và history lưu ở **Collection Variables**, không ghi đè
tripId/paymentId của collection thanh toán. Không tạo biến môi trường trùng tên biến runtime
`expiryRiderToken`, `expiryDriverToken`, `expiryTripId`, `expiryHistory`… vì chúng có thể che giá trị collection.
Không chạy giao dịch khác trên cùng ví trong khi kiểm thử.

Collection dùng số dư thật và giá thật từ server. Nếu ví vẫn đủ tiền thì bước kiểm tra điều kiện
**fail và dừng trước confirm**, không giả dữ liệu hoặc sửa DB. Chọn Rider ít tiền hơn hoặc chỉnh
điểm đến xa hơn rồi chạy lại; chuyến vừa tạo chưa confirm sẽ tự hết hạn.

## Các bước và kết quả

| Bước | Kiểm tra |
| --- | --- |
| 1–3 | Đăng nhập Rider/Driver; Driver Available gần điểm đón |
| 4 | Lưu số dư và ledger trước test |
| 5–6 | Tạo chuyến 202 Requested, chờ Quoted; xác minh fare > balance |
| 7–8 | Confirm WALLET lần 1 trả 409 INSUFFICIENT_FUNDS; vẫn Quoted/Pending, history không đổi |
| 9–10 | Khoảng giây 15 từ khi quan sát Quoted, confirm lần 2 vẫn thiếu tiền; history vẫn không đổi |
| 11 | Poll đến Cancelled, fare Rejected, driver null; history chỉ thêm Quoted → Cancelled |
| 12 | Confirm sau hủy trả 409 INVALID_TRIP_STATE |
| 13 | GET payment theo TripId trả 404 PAYMENT_NOT_FOUND |
| 14–15 | Ví và ledger không đổi; history không thêm dòng mới |

**Không request nào gọi `/cancel`.** Nếu Cancelled xuất hiện, ca đang kiểm tra worker tự hủy.
Thời gian được đối chiếu bằng timestamps của history server: Quoted → Cancelled từ 29 đến dưới
42 giây, cho phép độ lệch nhỏ và chu kỳ quét 5 giây. Confirm lần 2 khoảng giây 15 không được
kéo dài hạn đến giây 45. Script polling có giới hạn, không chờ vô hạn nếu backend vẫn cấu hình 300.

Kiểm tra ví trước confirm không giữ tiền. Ca này không đi đến matching hay checkout;
nó không thay thế ca kiểm tra số dư bị giảm sau một lần confirm thành công.

## Khi fail

- **ACTIVE_TRIP_EXISTS:** xử lý/chờ hết hạn chuyến cũ trước khi chạy lại.
- **DRIVER_BUSY:** dùng Driver rảnh; không reset tài xế đang có chuyến.
- **Ví đủ tiền:** chỉnh tài khoản/điểm đến; không mong INSUFFICIENT_FUNDS khi số dư đủ.
- **Confirm trả 502/504:** Payment chưa sẵn sàng hoặc không truy cập được; đây không phải thiếu tiền.
- **Confirm trả QUOTE_EXPIRED hoặc INVALID_TRIP_STATE sớm:** giảm Runner delay về 0, không chạy thủ công quá chậm.
- **Vẫn Quoted sau cửa sổ chờ:** kiểm tra cấu hình 30 giây, restart Trip, log QuoteExpiryWorker/Dispatcher.
- **Hủy muộn hơn 42 giây:** kiểm tra backlog worker/DB hoặc có logic gia hạn sai. Không nới assertion để che nguyên nhân.
- **Không có payment nhưng 404 khác mã:** không pass; bước 13 yêu cầu đúng PAYMENT_NOT_FOUND.

Khi không còn cần test nhanh, có thể đặt lại QuoteConfirmationTimeoutSeconds=300 và restart Trip.
Collection này được thiết kế riêng cho cấu hình 30 giây.
