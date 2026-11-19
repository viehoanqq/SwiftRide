# Giao diện Frontend (React + Vite) & Kịch bản Trình diễn End-to-End

Tài liệu này hướng dẫn cấu trúc ứng dụng **Frontend (`frontend/`)**, danh sách tài khoản dữ liệu mẫu (Seed Data) và kịch bản chạy demo thực tế toàn bộ tính năng của **SwiftRide**.

---

## 1. Cấu trúc Phân hệ Frontend (`http://localhost:5173`)

Giao diện người dùng được xây dựng bằng **React + Vite** theo phong cách biểu mẫu tối giản, giao tiếp duy nhất qua **API Gateway (`http://localhost:5000`)** và phân quyền truy cập tuyến đường qua `ProtectedRoute`:

| Đường dẫn UI | Vai trò truy cập | Chức năng chính |
|---|---|---|
| `/login`, `/register` | Công khai | Đăng nhập, đăng ký tài khoản `Rider` hoặc `Driver`, tự động điều hướng theo vai trò. |
| `/rider` | `Rider` | Trang tổng quan hành khách, kiểm tra chuyến đang hoạt động, xem nhanh số dư ví và lịch sử đặt gần đây. |
| `/rider/book` | `Rider` | Chọn địa điểm đón/trả mẫu quanh TP.HCM hoặc nhập tọa độ tùy chỉnh, nhận báo giá tức thời (`Quoted`), chọn thanh toán `WALLET` hoặc `CARD` (3 token giả lập) và xác nhận đặt xe. |
| `/rider/trip/:id` | `Rider` | Theo dõi tự động trạng thái chuyến đi theo thời gian thực, hủy chuyến trước khi lên xe, đối chiếu kết quả quyết toán `Payment` / `Refund`. |
| `/driver` | `Driver` | Bật/tắt nhận chuyến (`Online`/`Offline`), cập nhật tọa độ tài xế, tự động làm mới danh sách lời mời (`Pending Offers`), chấp nhận hoặc từ chối chuyến. |
| `/driver/trip/:id` | `Driver` | Điều khiển tuần tự các bước hành trình: `EnRoute` $\rightarrow$ `PickedUp` $\rightarrow$ `DroppedOff` và theo dõi trạng thái quyết toán doanh thu `80%`. |
| `/wallet` | `Rider`, `Driver` | Xem số dư ví `Account` hiện tại và bảng sao kê chi tiết từng dòng bút toán sổ cái (`CHARGE`, `REFUND`). |
| `/admin` | `Admin` | Tra cứu bất kỳ chuyến đi & giao dịch thanh toán nào theo `TripId`, kiểm tra trạng thái `/health` của 4 microservice và thực hiện phê duyệt bồi thường hoàn tiền (`POST /trips/{id}/refund-approval`). |

---

## 2. Danh sách Tài khoản Dữ liệu Mẫu (Seed Accounts)

Khi khởi chạy hệ thống lần đầu, các cơ sở dữ liệu tự động tạo sẵn bộ tài khoản mẫu đồng bộ ID giữa `UserDb`, `MatchDb` và `PayDb` (**mật khẩu chung cho tất cả tài khoản seed: `123456`**):

### Hành khách (`Rider`)
| Số điện thoại | Họ tên | Số dư ví khởi tạo | Mục đích kiểm thử gợi ý |
|---|---|---|---|
| `0900000001` | Nguyễn Văn An | **500.000 đ** | Demo đặt xe & thanh toán ví `WALLET` thành công (`Paid`). |
| `0900000002` | Trần Thị Bình | **20.000 đ** | Demo đặt chuyến cước `> 20.000 đ` để kiểm chứng nhánh thiếu số dư (`PaymentFailed`). |

### Tài xế (`Driver` — khởi tạo sẵn ở trạng thái `Available`, ví `0 đ`)
| Số điện thoại | Họ tên | Vị trí khởi tạo (TP.HCM) | Tọa độ `(Lat, Lng)` |
|---|---|---|---|
| `0910000001` | Lê Văn Cường | Chợ Bến Thành (Q.1) | `10.772500, 106.698000` |
| `0910000002` | Phạm Minh Dũng | Nhà thờ Đức Bà (Q.1) | `10.779800, 106.699000` |
| `0910000003` | Hoàng Văn Em | Tòa nhà Bitexco (Q.1) | `10.771600, 106.704400` |
| `0910000004` | Võ Thị Phương | Dinh Độc Lập (Q.1) | `10.777000, 106.695300` |
| `0910000005` | Đặng Quốc Gia | Đại học Sài Gòn (Q.5) | `10.759800, 106.682300` |

### Quản trị viên (`Admin`) & Bật Cổng thẻ Giả lập
Trước khi chạy kịch bản Thẻ giả lập (`CARD`) và kịch bản Hoàn tiền (`Refund Saga`), mở file `.env` ở thư mục gốc và bật cấu hình:
```dotenv
Payment__FakeCard__Enabled='true'
Identity__AdminBootstrap__Phone='0999999999'
Identity__AdminBootstrap__Password='AdminPassword123!'
```
Khởi động lại `IdentityService` và `PaymentService` để hệ thống cấp tài khoản Admin (`0999999999` / `AdminPassword123!`) và mở 3 token kiểm thử thẻ.

---

## 3. Năm Kịch bản Demo End-to-End Chuẩn

1. **Kịch bản 1 — Luồng chuẩn thanh toán Ví (`WALLET`) & chia doanh thu `80/20`:**
   - Mở Tab 1 (thường) đăng nhập Rider `0900000001` (`123456`), đặt chuyến từ **Chợ Bến Thành** đến **Đại học Sài Gòn**, xác nhận thanh toán `WALLET`.
   - Hệ thống tự ghép tài xế gần nhất **Lê Văn Cường** (`0910000001`). Mở Tab 2 (ẩn danh) đăng nhập `0910000001`, bấm **Nhận chuyến** $\rightarrow$ **Đang đến đón** $\rightarrow$ **Đã đón khách** $\rightarrow$ **Trả khách**.
   - Chuyến tự động chuyển `PaymentPending` $\rightarrow$ `Paid`. Vào mục **Ví (`/wallet`)** ở cả hai tab để kiểm chứng: ví Rider trừ đúng giá cước $A$, ví Driver cộng đúng $0.8A$.

2. **Kịch bản 2 — Tài xế từ chối & tự động mời tài xế gần kế tiếp (`< k` lần):**
   - Khi chuyến đang ở `DriverOffered` cho `0910000001`, tài xế này bấm **Từ chối**.
   - Hệ thống tự động loại trừ `0910000001` và mời tài xế gần tiếp theo (ví dụ Võ Thị Phương `0910000004` hoặc Hoàng Văn Em `0910000003`). Nếu từ chối đủ 3 lượt (`MaxOfferAttempts = 3`), chuyến tự chuyển sang `Failed`.

3. **Kịch bản 3 — Ví không đủ số dư (`PaymentFailed`):**
   - Đăng nhập Rider `0900000002` (ví chỉ có `20.000 đ`), đặt chuyến đi dài có giá cước `> 20.000 đ` bằng `WALLET`.
   - Sau khi tài xế bấm **Trả khách**, Payment từ chối do `INSUFFICIENT_FUNDS`, chuyến chuyển sang `PaymentFailed` và tài xế tự động được giải phóng về `Available`.

4. **Kịch bản 4 — Thanh toán Thẻ giả lập (`CARD`: `approved` / `declined` / `unknown`):**
   - Tại màn hình xác nhận chuyến, chọn phương thức `CARD` và chọn lần lượt 1 trong 3 mã token:
     - `test-card-approved`: thu tiền thành công qua tài khoản `GATEWAY` (số dư ví Rider không bị trừ, ví Driver vẫn nhận đủ `80%`).
     - `test-card-declined`: cổng thẻ từ chối $\rightarrow$ `PaymentFailed`.
     - `test-card-unknown`: cổng thẻ treo kết quả $\rightarrow$ giữ `PaymentPending`, kích hoạt nhánh `PaymentUncertain` và đối soát định kỳ của Saga.

5. **Kịch bản 5 — Admin phê duyệt Bồi thường Hoàn tiền (`Refund Saga`):**
   - Copy `TripId` của một chuyến đã ở trạng thái `Paid`.
   - Đăng nhập tài khoản Admin (`0999999999` / `AdminPassword123!`) tại trang `/admin`, nhập `TripId`, chọn lý do chuẩn (`APPROVED_CUSTOMER_REFUND` hoặc `INVALID_TRIP_CHARGE`), nhập mã hồ sơ bằng chứng (ví dụ `TICKET-2026-001`) và bấm phê duyệt.
   - Chuyến chuyển sang `RefundPending` $\rightarrow$ `Refunded`; kiểm tra lại trang **Ví** của Rider và Driver sẽ thấy xuất hiện thêm dòng bút toán `REFUND` đảo ngược chính xác số tiền của dòng `CHARGE` ban đầu.
