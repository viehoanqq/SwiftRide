# Đăng ký tài khoản và Admin cấp tiền ví

Import `SwiftRide-Onboarding.postman_collection.json` cùng environment Gateway đang dùng. Collection này dùng chung `riderPhone`, `driverPhone`, `riderToken`, `driverToken`, `riderId`, `driverId` với các collection chuyến đi.

## Chuẩn bị

Khởi động lại Payment để áp dụng migration `AddWalletCredits`, khởi động lại Gateway để nhận route mới. Identity, Matching, RabbitMQ và database phải hoạt động.

Điền environment:

| Biến | Giá trị |
| --- | --- |
| `gatewayUrl` | `http://localhost:5000` |
| `riderPhone` | Số điện thoại Rider, 10 chữ số bắt đầu bằng 0 |
| `driverPhone` | Số điện thoại Driver khác Rider |
| `defaultPassword` | Mật khẩu của cả hai tài khoản, ít nhất 6 ký tự |
| `riderFullName`, `driverFullName` | Tên đăng ký |
| `adminPhone`, `adminPassword` | Admin đã được tạo bởi Identity; API public không đăng ký được Admin |
| `walletCreditAmount` | Số tiền **cộng thêm**, mặc định 500000 |
| `walletCreditReason` | Lý do Admin cấp tiền |

Không ghi mật khẩu Admin thật vào environment chia sẻ/git. Environment local hiện có được giữ nguyên thông tin Admin.

## Chạy collection

1. Folder 01 đăng ký Rider và Driver, đăng nhập, lưu token/ID. Nếu số điện thoại đã tồn tại, vẫn phải đăng nhập đúng mật khẩu và đúng role. Không đổi mật khẩu hoặc reset tài khoản cũ.
2. Chờ tối đa khoảng 60 giây cho từng ví/hồ sơ được tạo từ `UserRegistered`/`DriverRegistered`. Không reset Busy hoặc chuyến đang chạy.
3. Folder 02 đăng nhập Admin, đọc số dư hiện tại, xác nhận Rider gọi API cấp tiền bị 403, Admin cấp tiền, gọi lại cùng key và kiểm tra tiền chỉ được cộng một lần. Không chạy thanh toán hoặc cấp tiền song song trong khi kiểm tra số dư.
4. Sau đó chạy WALLET E2E, CARD, cancel hoặc refund theo nhu cầu với cùng environment. Tài khoản mới cần chạy bước Driver online trong E2E trước khi tìm tài xế.

Chọn lưu giá trị biến sau Runner nếu phiên bản Postman có tùy chọn này. `walletCreditKey` được tự tạo và giữ lại cho cùng Rider. Chạy lại cùng key không cấp thêm, kể cả đã tiêu số tiền cũ. Muốn cấp thêm một lần mới: xóa riêng `walletCreditKey` rồi chạy folder 02. Đổi amount/reason mà giữ key sẽ báo 409. Đổi Rider sẽ sinh key mới cho Rider đó. Các test phân quyền dùng `otherRiderPhone` vẫn cần tài khoản phụ riêng.

## API cấp tiền thật bởi Admin

```http
POST /admin/wallets/{riderId}/credits
Authorization: Bearer <adminToken>
Idempotency-Key: <key-duy-nhat-cho-lan-cap>
Content-Type: application/json

{"amount":500000,"reason":"Admin duyệt cấp tiền"}
```

- Dùng được mọi môi trường, không phụ thuộc FakeCard hoặc Development.
- 201 khi cấp mới; 200 khi gửi lại cùng key, Admin và payload; 409 nếu key cũ có nội dung khác.
- 401 nếu chưa đăng nhập; 403 nếu không có role Admin; 404 `ACCOUNT_NOT_FOUND` nếu ví Rider chưa tồn tại; 400 nếu amount/key/reason không hợp lệ.
- Amount từ 0.01 đến 100 triệu, tối đa 2 số lẻ. Key tối đa 100 ký tự; reason tối đa 500 ký tự sau trim.
- PostgreSQL khóa key và khóa account. Cộng số dư và ghi `wallet_credits` cùng transaction; lỗi thì rollback cả hai. Khóa ví được dùng chung với checkout/refund để tránh ghi đè số dư.
- Phiếu lưu AdminId, RiderId, AccountId, amount, lý do, key, số dư ngay sau cấp và thời gian UTC. Số dư trên phiếu cũ không phải số dư hiện tại sau các giao dịch tiếp theo.

Admin xem phiếu bằng `GET /admin/wallet-credits/{id}`. Rider xem lịch sử cấp tiền của chính mình bằng `GET /wallets/me/credits?skip=0` (50 bản ghi/trang, mới nhất trước). API Rider không trả AdminId, lý do nội bộ hay key.

Đây là **khoản cấp tiền do Admin phê duyệt**, có số dư lưu thật trong database; không phải bằng chứng tiền đã chuyển từ ngân hàng. Nhật ký khoản cấp nằm ở `wallet_credits`; `LedgerEntries` hiện tại vẫn dành cho thu/hoàn tiền chuyến. Không tạo Payment giả, không dùng refund chuyến để đảo khoản cấp. Chưa có API thu hồi khoản cấp.

Test thiếu tiền/hết hạn Quoted 30s cần giá chuyến lớn hơn số dư thực tế. Sau khi cấp thêm tiền, phải điều chỉnh quãng đường của kịch bản đó nếu giá không còn lớn hơn số dư.
