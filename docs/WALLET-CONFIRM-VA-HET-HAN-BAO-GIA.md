# Kiểm tra ví trước confirm và tự hủy báo giá hết hạn

## Cấu hình trong TripService.Api/appsettings.json

```json
"Trip": {
  "MaxOfferAttempts": 3,
  "QuoteConfirmationTimeoutSeconds": 300,
  "QuoteExpiryPollSeconds": 5,
  "QuoteExpiryBatchSize": 50
}
```

300 giây = 5 phút để xác nhận từ `TripFare.QuotedAt`. QuotedAt được ghi khi Trip
nhận và áp dụng QuoteCalculated, không tính thời gian event chờ trong queue.
Mọi phương thức WALLET/CARD dùng cùng hạn báo giá. Sửa cấu hình rồi restart Trip.
Các giá trị phải dương; startup kiểm tra cấu hình. Biến môi trường `Trip__...` có ưu tiên cao hơn appsettings.

## Confirm

Trip xác minh Rider sở hữu chuyến và trạng thái Quoted, kiểm tra thời hạn,
đọc phương thức thanh toán. Với WALLET, Trip gọi API nội bộ Payment:

```http
GET /internal/wallets/{riderId}/balance
Authorization: Bearer <trip-service token có scope payments:read>
```

API không đọc toàn bộ ledger, chỉ đọc balance tài khoản Rider từ DB Payment.
Token Rider/Driver không được gọi API này. Trip dùng RiderId và giá đã lưu trong Trip,
không nhận số dư/giá từ client để quyết định.

| Kết quả | Confirm | Trip / Outbox |
| --- | --- | --- |
| Số dư >= fare | 202 FindingDriver | Fare Accepted + MatchRequested cùng transaction |
| Số dư < fare | 409 INSUFFICIENT_FUNDS | Giữ Quoted/Pending; không phát MatchRequested |
| Chưa có ví | 409 WALLET_NOT_READY | Giữ Quoted |
| Payment mất kết nối/lỗi | 502 | Giữ Quoted |
| Payment không trả lời trong 3 giây | 504 PAYMENT_TIMEOUT | Giữ Quoted |
| Báo giá hết hạn khi confirm | 409 QUOTE_EXPIRED nếu request này thực hiện hủy | Cancelled qua State Pattern |

Nếu worker đã hủy trước đó, confirm trả 409 INVALID_TRIP_STATE vì Trip đã Cancelled.
Thiếu tiền không cập nhật QuotedAt, không bắt đầu lại thời hạn.
Sau khi đọc ví, Trip kiểm tra hạn lần nữa trước khi confirm.
CARD không kiểm tra số dư ví Rider. Kiểm tra này không hold/trừ tiền; checkout sau dropoff
vẫn khóa tài khoản và kiểm tra số dư như trước. Ví có thể thay đổi giữa hai thời điểm.

## Worker hết hạn

QuoteExpiryWorker chạy nền theo QuoteExpiryPollSeconds. QuoteExpiryDispatcher chỉ lấy
Trip có State=Quoted và QuotedAt đã quá hạn, batch tối đa QuoteExpiryBatchSize.
SQL dùng UPDLOCK/READPAST/ROWLOCK trong transaction để nhiều instance không xử lý trùng.
Mỗi Trip gọi `trip.Cancel()` → `QuotedState.Cancel()` → fare Rejected + history Cancelled,
rồi commit một lần. Không cần gọi HTTP `/cancel`, không phát refund hay giải phóng Driver
vì chưa bắt đầu matching.

FindingDriver, DriverOffered và các trạng thái khác không bị worker này hủy.
RowVersion của Trip chống ghi đè khi confirm và worker chạy đồng thời. Nếu xung đột,
client đọc lại trạng thái sau 409. Worker chạy lại sau restart, deadline không mất
vì dựa trên QuotedAt trong DB. Không cần SQL migration mới.

Worker mặc định quét 5 giây một lần: khi không có backlog, chuyến thường chuyển Cancelled
trong lần quét đầu sau mốc 5 phút; không phải timer chính xác tới từng mili giây.
Confirm đã hết hạn vẫn bị chặn dù worker chưa kịp quét.

## Kiểm tra bằng Postman

1. Tạo chuyến, chờ Quoted. Ghi lại history và fare.
2. Với ví thiếu tiền, confirm WALLET: 409 INSUFFICIENT_FUNDS. GET Trip phải vẫn Quoted,
   không có FindingDriver trong history. Có thể chọn CARD hoặc nạp ví rồi thử lại trước hạn.
3. Không confirm thành công, chờ hơn 300 giây cộng khoảng quét; GET Trip phải Cancelled,
   fare Rejected và chỉ thêm một dòng Quoted → Cancelled.
4. Thử confirm hết hạn: phải 409, không tạo offer.
5. Chuyến đủ ví confirm thành FindingDriver trước hạn: sau 5 phút worker này không hủy chuyến đó.
6. Tắt Payment khi Trip Quoted, confirm WALLET: lỗi kết nối/timeout, không phải thiếu tiền.

Restart **Payment và Trip** sau khi cập nhật để có API đọc ví và worker mới.
