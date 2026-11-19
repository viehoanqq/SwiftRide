# Identity Service, Mô hình Bảo mật Đa lớp & Khởi tạo Quản trị viên (`AdminBootstrapper`)

Tài liệu này mô tả kiến trúc xác thực, phân quyền liên dịch vụ và quy trình cấp tài khoản `Admin` an toàn trong **SwiftRide**.

---

## 1. Trách nhiệm của Identity Service (`:5004` / `UserDb :1435`)

Sau khi tách khỏi `TripService` (xem migration lịch sử `ExtractIdentity` và script `scripts/split-identity.ps1`), **Identity Service** là nguồn sự thật duy nhất về định danh người dùng:
- **`POST /auth/register`**: Chỉ cho phép tự đăng ký vai trò `Rider` hoặc `Driver` (chặn cứng việc tự tạo `Admin`). Mật khẩu được băm bằng `BCrypt.Net-Next`, lưu `Users` và ghi nguyên tử sự kiện `UserRegistered` (kèm `DriverRegistered` nếu là tài xế) vào `UserDb.OutboxMessages`.
- **`POST /auth/login`**: Kiểm tra trạng thái `IsActive`, đối chiếu BCrypt và phát hành **User JWT** (`HMAC-SHA256`, mặc định 120 phút) gồm các claim `sub` (`UserId`), `unique_name` (`FullName`), `phone` và `role` (`Rider | Driver | Admin`).
- **`GET /auth/me`**: Trả về hồ sơ người dùng hiện hành từ chữ ký token.
- **`IdentityOutboxPublisher`**: Quét bảng `OutboxMessages` mỗi `10s` (lô 50 bản ghi) để phát sự kiện lên RabbitMQ, đồng thời tự động **backfill** sự kiện cho các tài khoản seed chưa từng phát tin để đảm bảo mọi tài khoản luôn có ví tại `PaymentService` và hồ sơ tại `MatchingService`.

---

## 2. Cơ chế Khởi tạo Quản trị viên An toàn (`AdminBootstrapper`)

Để vận hành luồng phê duyệt bồi thường hoàn tiền (`POST /trips/{id}/refund-approval`), hệ thống cần tài khoản có vai trò `Admin`. Thay vì tạo sẵn tài khoản Admin với mật khẩu cố định dễ bị khai thác, `AdminBootstrapper` thực thi quy tắc sau khi `IdentityService` khởi động:

1. **Khi KHÔNG cấu hình bootstrap:** Nếu phát hiện tài khoản demo cũ trong CSDL còn đang bật (`IsActive == true` với mật khẩu mẫu), hệ thống tự động vô hiệu hóa (`IsActive = false`).
2. **Khi cấu hình bootstrap trong `.env`:**
   ```dotenv
   Identity__AdminBootstrap__Phone='0999999999'
   Identity__AdminBootstrap__Password='AdminStrongPassword2026'
   ```
   - Yêu cầu số điện thoại đúng định dạng 10 chữ số bắt đầu bằng `0` (`^0\d{9}$`) và mật khẩu đủ độ dài an toàn.
   - Hệ thống tạo mới (hoặc nâng cấp mật khẩu mới một cách idempotent) tài khoản `Role = Admin` (`FullName = "Quản trị viên"`).
   - Sau khi tài khoản Admin đã được lưu vào `UserDb`, có thể xóa hai dòng biến môi trường này khỏi `.env` mà tài khoản Admin vừa tạo vẫn tiếp tục hoạt động bình thường.

---

## 3. Bảo mật Theo chiều sâu (Defense-in-Depth) trên Toàn Hệ thống

SwiftRide áp dụng 4 lớp kiểm soát bảo mật độc lập:

### Lớp 1: Hàng rào biên tại YARP API Gateway (`:5000`)
- Chặn trực tiếp mọi request `POST /payments` và `POST /payments/{id}/refund` từ bên ngoài bằng mã `403 Forbidden` (`INTERNAL_ENDPOINT_ONLY`).
- Đối với `PaymentService`, cấu hình YARP chỉ mở duy nhất phương thức `GET` cho `/wallets/me`, `/payments/{id}` và `/payments/by-trip/{tripId}`.
- Các API nội bộ (`/quote`, `/match`, `/drivers/{id}/register`, `/internal/accounts/register`, `/payments/refunds/by-key/{key}`) hoàn toàn không được ánh xạ trên Gateway.

### Lớp 2: Tách biệt User JWT (Role) và Service JWT (Scope)
- **User JWT:** Dùng cho tác nhân con người (`Rider`, `Driver`, `Admin`), chuyển tiếp qua Gateway hoặc qua `ForwardRequestHeadersHandler` (khi `TripService` gọi `/quote`, `/match`).
- **Internal Service JWT:** Phát sinh tự động bởi `ServiceAuthorizationHandler` với hạn sống ngắn **2 phút**, mang claim định danh dịch vụ gọi và claim `scope` tối thiểu:
  - `payments:create`: tạo giao dịch nội bộ.
  - `payments:read`: `PaymentSagaReconciler` hoặc `TripRefundCoordinator` đối soát trạng thái thanh toán/biên lai hoàn tiền.
  - `payments:refund`: thực thi hoàn tiền nội bộ.
  - `wallets:register` / `drivers:register`: đồng bộ tài khoản nội bộ.

### Lớp 3: Kiểm soát Quyền sở hữu Tài nguyên (Resource Ownership / Anti-IDOR)
- Ngay cả khi người dùng có JWT hợp lệ, tầng Application vẫn kiểm tra chặt chẽ quyền sở hữu:
  - Chỉ `RiderId` chủ chuyến, `DriverId` được gán/mời trên chuyến đó, hoặc `Admin` mới được đọc `GET /trips/{id}` và `GET /payments/by-trip/{tripId}`.
  - Chỉ đúng `DriverId` đang có lời mời `Pending` mới gọi được `driver-accept` / `driver-reject`; chỉ đúng tài xế đã `Accepted` mới gọi được `en-route` / `pickup` / `dropoff`.

### Lớp 4: Quản lý Bí mật & Vệ sinh Nhật ký (Secret & Log Hygiene)
- Mọi chuỗi kết nối, mật khẩu CSDL/RabbitMQ và `Jwt__Key` được nạp từ `.env` qua `DotEnvLoader` (tham khảo mẫu `.env.example`).
- Tuyệt đối không ghi `Password`, `PasswordHash` hay `CardToken` vào log ứng dụng hoặc trả ngược ra DTO công khai.
