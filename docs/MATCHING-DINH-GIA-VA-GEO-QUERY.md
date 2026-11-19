# Matching & Pricing Service: Chiến lược Định giá, Truy vấn Không gian `2dsphere` & Chống lệch Thứ tự Sự kiện

Tài liệu này mô tả thiết kế chi tiết của **Matching & Pricing Service (`:5002`)** trên nền tảng **.NET 10** và **MongoDB (`MatchDb :27018`)**.

---

## 1. Mô hình Định giá Toán học & Strategy Pattern

### 1.1. Tính khoảng cách cầu Haversine và thời gian ước tính
Từ tọa độ điểm đón `(PickupLat, PickupLng)` và điểm trả `(DropoffLat, DropoffLng)`:
- Quãng đường $d$ (km) được tính bằng công thức mặt cầu **Haversine** (bán kính Trái Đất $R = 6371\text{ km}$).
- Thời gian di chuyển ước tính $t$ (phút) tính theo vận tốc trung bình đô thị $v = 30\text{ km/h}$: $t = \frac{d}{30} \times 60$.

### 1.2. Công thức định giá tổng quát (`PricingCalculator`)
$$p = \bigl((c_d \cdot d + c_t \cdot t) \cdot s + c_m\bigr) \cdot \eta \cdot (1 + T)$$

| Tham số | Cấu hình mặc định | Ràng buộc hợp lệ | Ý nghĩa |
|---|---|---|---|
| `Cd` ($c_d$) | `10000` | $c_d \ge 0$ | Đơn giá theo km (VNĐ/km) |
| `Ct` ($c_t$) | `1000` | $c_t \ge 0$ | Đơn giá theo phút (VNĐ/phút) |
| `S` ($s$) | `1.0` | $s \ge 1$ | Hệ số nhân giá động giờ cao điểm (*Surge*) |
| `Cm` ($c_m$) | `5000` | $c_m \ge 0$ | Phụ phí mở cửa cố định (VNĐ) |
| `Eta` ($\eta$) | `1.0` | $0 \le \eta \le 1$ | Hệ số khuyến mãi (*Promo*, ví dụ `0.8` giảm 20%) |
| `Tax` ($T$) | `0.1` | $T \ge 0$ | Thuế suất / phí nền tảng (10%) |

### 1.3. Lựa chọn chiến lược tự động (`PricingStrategySelector`)
Tầng Domain cài đặt giao diện `IPricingStrategy` với 3 chiến lược độc lập, được `PricingStrategySelector` chọn theo thứ tự ưu tiên:
1. **`SurgePricing`**: kích hoạt khi `S > 1` (ưu tiên phản ánh tình trạng khan hiếm nguồn cung).
2. **`PromoPricing`**: kích hoạt khi `S == 1` và `Eta < 1` (áp dụng ưu đãi giảm giá).
3. **`StandardPricing`**: kích hoạt khi `S == 1` và `Eta == 1` (giá tiêu chuẩn).

Mỗi kết quả báo giá được lưu lại vào collection `quotes` kèm toàn bộ bộ tham số tại thời điểm tính giá để đảm bảo khả năng kiểm toán.

---

## 2. Truy vấn Không gian Bán kính Cầu (`$nearSphere`) trên MongoDB

- Collection `drivers` lưu vị trí tài xế theo chuẩn **GeoJSON `Point`** với thứ tự tọa độ **`[longitude, latitude]`** và khởi tạo tự động chỉ mục không gian cầu **`2dsphere`** trong `MongoContext.EnsureIndexesAsync()`.
- Khi `TripService` gọi `POST /quote` hoặc `POST /match`:
  - Hệ thống lọc trên MongoDB bằng toán tử `$nearSphere` quanh điểm đón với bán kính mặc định **`5.000 mét` (`5 km`)**.
  - Điều kiện lọc kết hợp: `Status == Available` và `_id NOT IN ExcludeDriverIds` (danh sách các tài xế đã từ chối ở các lượt mời trước của cùng chuyến đi).
  - MongoDB trả về tài xế khả dụng **gần điểm đón nhất** đã được sắp xếp theo khoảng cách cầu thực tế.
  - Nếu không có tài xế nào thỏa điều kiện tại bước `/quote`, dịch vụ trả mã `404 NO_DRIVER_NEARBY`.

---

## 3. Chống lệch Thứ tự Sự kiện Bất đồng bộ (`TripLifecycleHandler`)

`MatchingService` tiêu thụ hai sự kiện vòng đời chuyến đi từ hai hàng đợi RabbitMQ riêng biệt:
- `matching.trip-accepted` (`TripAccepted` $\rightarrow$ đặt tài xế sang `Busy`).
- `matching.trip-closed` (`TripClosed` $\rightarrow$ trả tài xế về `Available`).

Trong môi trường phân tán chạy song song (`PrefetchCount = 16`), thứ tự đến của thông điệp giữa hai queue không được đảm bảo tuyệt đối. `DriverDocument` và `TripLifecycleHandler` giải quyết triệt để cả hai kịch bản lệch thứ tự bằng bộ trường `StatusTripId` và `ClosedTripIds`:

1. **`TripClosed` đến trước `TripAccepted` của cùng một chuyến $T_1$** (ví dụ khách hủy chuyến ngay lập tức sau khi tài xế bấm nhận):
   - Khi `TripClosed(T_1)` đến trước, hệ thống ghi `T_1` vào mảng `ClosedTripIds`.
   - Khi `TripAccepted(T_1)` đến muộn ngay sau đó, handler kiểm tra thấy `ClosedTripIds.Contains(T_1)` nên **bỏ qua**, ngăn chặn lỗi khóa nhầm tài xế ở trạng thái `Busy` vĩnh viễn.
2. **`TripClosed` của chuyến cũ $T_1$ đến trễ khi tài xế đã nhận chuyến mới $T_2$**:
   - Lúc này `DriverDocument.StatusTripId` đang bằng `T_2`.
   - Khi `TripClosed(T_1)` đến muộn, do `StatusTripId != T_1`, hệ thống chỉ ghi nhận `T_1` vào `ClosedTripIds` mà **không đổi trạng thái của tài xế sang `Available`**, bảo vệ an toàn cho chuyến đang chạy $T_2$.
