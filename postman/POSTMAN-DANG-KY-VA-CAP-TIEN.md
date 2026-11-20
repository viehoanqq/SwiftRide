# Đăng ký, đặt số dư và chạy nghiệp vụ riêng

## Thứ tự chạy

1. **SwiftRide-Onboarding**: đăng ký Rider/Driver, login và chờ tạo ví/hồ sơ qua RabbitMQ. Chỉ chạy khi cần tạo tài khoản.
2. **SwiftRide-Wallet**: login Rider/Driver lấy ID, login Admin, đặt số dư cả hai ví và kiểm tra lại. Chỉ chạy khi muốn thay đổi số dư.
3. **EndToEnd / Card-EndToEnd / RefundSaga / CancelTrip / WalletExpiry30s**: bắt đầu từ login, không đăng ký hoặc sửa số dư tự động.

Dùng chung environment. Điền `riderPhone`, `driverPhone`, `defaultPassword`; thông tin Admin ở `adminPhone`, `adminPassword`. Số dư muốn đặt: `riderBalance` (mặc định 500000), `driverBalance` (mặc định 100000). Không còn các biến key cấp tiền.

Đổi số điện thoại mới thì chạy bước 1 và 2 trước. Dùng tài khoản đã có thì chạy thẳng collection nghiệp vụ nếu ví đủ tiền. Các ca sai quyền cần tài khoản `otherRiderPhone` riêng. RefundSaga bắt đầu login rồi tạo chuyến, thanh toán và hoàn tiền. Các ca tắt Matching vẫn cần thao tác thủ công; ca hết hạn cần backend timeout 30 giây.

## API Admin đặt số dư

```http
PUT /admin/wallets/{userId}/balance
Authorization: Bearer <adminToken>
Content-Type: application/json

{"balance":100000,"ownerType":"Driver"}
```

OwnerType là Rider hoặc Driver. Không có header Idempotency-Key. Balance bắt buộc, từ 0 đến 100 triệu, tối đa 2 số lẻ. API trả 200 với userId, ownerType, balance.

Ví đang 200000, đặt 100000 thì giảm còn 100000. Ví đang 0, đặt 100000 thì tăng thành 100000. Gọi lại cùng số dư khi không có giao dịch xen vào không cộng thêm tiền hoặc ghi thêm lịch sử. Nếu đã có giao dịch xen vào, gọi lại sẽ đặt về số dư được yêu cầu; đây là thao tác đặt số dư, không phải nạp thêm.

Chỉ Admin được gọi; Rider/Driver nhận 403. Ví chưa được tạo trả 404. Backend khóa ví và lưu số dư cùng lịch sử thay đổi trong một transaction. Lịch sử cũ vẫn giữ; amount trong wallet_credits là chênh lệch có dấu, balance_after là số dư sau thao tác. Trường key cũ trong database được backend tự sinh để tương thích lịch sử, không yêu cầu client quản lý.

Restart Payment và Gateway để nhận API mới và migration `AllowAdminBalanceAdjustments`. API POST /admin/wallets/{id}/credits cũ đã được thay bằng PUT /balance. Import lại collection và environment; không cần reset chuyến hoặc database.
