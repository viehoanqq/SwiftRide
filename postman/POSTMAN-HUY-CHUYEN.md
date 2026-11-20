# Kiểm thử API hủy chuyến

Import [SwiftRide-CancelTrip.postman_collection.json](SwiftRide-CancelTrip.postman_collection.json).
Collection bắt đầu login. Tạo tài khoản bằng Onboarding và đặt số dư bằng SwiftRide-Wallet trước nếu cần.
Các ca tắt Matching chạy riêng từng folder sau bước chuẩn bị; mỗi folder nghiệp vụ tự dừng cuối luồng.
Không ghép ca hủy vào collection thanh toán vì chuyến Cancelled không tiếp tục nhận tài xế/dropoff.

## Cấu hình

Chọn environment dùng chung có gatewayUrl `http://localhost:5000`. Các biến tài khoản nằm trong environment:

| Biến | Mặc định | Yêu cầu |
| --- | --- | --- |
| riderPhone | 0900000001 | Rider sở hữu chuyến; không có chuyến đang hoạt động khi tạo mới |
| otherRiderPhone | 0900000002 | Rider khác để kiểm tra 403 |
| driverPhone | 0910000001 | Driver không Busy, có thể bật Available |
| defaultPassword / otherRiderPassword | 123456 | Mật khẩu cặp chính / Rider dùng kiểm tra sai quyền |
| nonCancelableTripId | Rỗng | Chỉ cần cho folder 05 |

Đây là tài khoản seed để chạy local, không phải mật khẩu Admin. Không điền mật khẩu thật vào file commit.
Các token, cancelTripId và history được lưu ở **Collection Variables**, tách khỏi tripId/paymentId của collection E2E.
Không tạo Environment Variables trùng tên các biến runtime `cancel*Token`, `cancelTripId`.
Khi chuyển folder 03 sang 04, bật **Keep variable values** trong Runner nếu phiên bản Postman có tùy chọn này;
kiểm tra Collection Variables còn cancelTripId và cancelRiderToken trước khi chạy tiếp.

## 01 — Quoted, quyền truy cập và hủy lặp

Chạy tất cả service, broker và database. Run folder 01:

1. Đăng nhập Rider, Driver; bật Driver Available gần điểm đón.
2. Tạo chuyến mới: 202 Requested. Poll tối đa 30 lần cách nhau 2 giây đến Quoted.
3. Đăng nhập Rider khác: hủy trả 403. Driver hủy trả 403. Không token trả 401.
4. Hủy một UUID ngẫu nhiên không tồn tại: 404.
5. Đọc lại chuyến: vẫn Quoted; các yêu cầu sai quyền không được hủy chuyến.
6. Chủ chuyến gọi `POST /trips/{cancelTripId}/cancel`, **không body**: 200 Cancelled.
7. Kiểm tra history giữ nguyên phần trước, chỉ thêm đúng một dòng Quoted → Cancelled;
   fare chuyển Rejected theo QuotedState.
8. Gọi hủy lần nữa: 200, history giữ nguyên cả thời điểm chuyển trạng thái.

## 02 — Requested

Tắt **Matching Service** trước khi chạy folder 02; giữ RabbitMQ, Trip, Identity, Gateway và database chạy.
Không dùng bước health tổng hợp vì Matching đang chủ động tắt.

Folder tạo chuyến, kiểm tra đúng Requested rồi hủy. Expected 200 Cancelled,
history Requested → Cancelled đúng một lần, hủy lặp không thêm history.
Nếu Matching vẫn chạy và đã xử lý báo giá thì ca dừng/fail, không coi Quoted là Requested.

Sau đó bật Matching lại, chờ xử lý event và GET `/trips/{cancelTripId}` bằng token chủ chuyến:
chuyến phải vẫn Cancelled, không có dòng Quoted mới. Không chạy tạo chuyến mới trước khi ghi lại ID cần kiểm tra này.

## 03 + 04 — FindingDriver

1. Bật Matching; chạy folder 03 để tạo chuyến và chờ Quoted. Folder tự dừng ở đây.
2. **Tắt Matching**, giữ nguyên Collection Variables.
3. Chạy folder 04. Confirm WALLET trả 202 FindingDriver; chưa có offer.
4. Hủy trả 200 Cancelled; history FindingDriver → Cancelled chỉ xuất hiện một lần.
5. Bật Matching lại. Sau khi xử lý request cũ, Trip vẫn Cancelled, không được chuyển DriverOffered.

Không cần tiền trong ví cho ca này vì chưa tới dropoff. Không thu tiền và không phát refund.
Không chạy cả 03 và 04 liền nhau khi Matching online vì kết quả có thể đến trước thao tác hủy.

## 05 — Không cho hủy sau khi đã tạo offer

Dùng một chuyến hiện có thuộc riderPhone, đặt `nonCancelableTripId`.
Ví dụ chạy E2E đến bước 3.5 DriverOffered rồi dừng, lấy ID đó. Không cần tạo chuyến mới trong folder này.

Folder xác nhận trạng thái ngoài phạm vi hủy, gọi cancel phải trả **409**, rồi kiểm tra State/history không đổi.
Nếu chuyến không thuộc Rider đăng nhập thì GET trả 403: đó là sai dữ liệu chuẩn bị, không phải pass ca 409.
Không để client/tài xế khác tiến hành chuyến cùng lúc trong ca này vì sẽ làm history đổi hợp lệ.
Folder không tự hủy/xóa chuyến; tiếp tục xử lý chuyến bằng workflow bình thường sau khi kiểm tra.

## Khi ca dừng

- ACTIVE_TRIP_EXISTS: Rider còn chuyến cũ; xử lý chuyến đó trước, không xóa DB để vượt qua.
- DRIVER_BUSY: dùng Driver rảnh hoặc hoàn tất chuyến đang gán.
- Hết thời gian chờ Quoted: xem Trip Outbox, Matching consumer/outbox và các queue `_error`.
- 409 khi dự định hủy FindingDriver: đọc lại Trip; nếu đã DriverOffered thì API đang chặn đúng phạm vi.
- Chạy request bằng Send riêng: `setNextRequest` chỉ điều khiển Runner; tự thực hiện đúng thứ tự hướng dẫn.
