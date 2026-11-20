# Các chiến lược tính giá

`QuoteAppService` gọi `PricingStrategySelector.Select(parameters)`, sau đó gọi
`Calculate` trên strategy được chọn. Kết quả và bộ tham số được lưu vào quote.

## Chọn strategy

Các tham số trong section `Pricing` được đọc khi Matching khởi động và đăng ký
singleton trong `Program.cs`:

| Điều kiện | Strategy |
| --- | --- |
| `S > 1` | Surge |
| `S = 1`, `Eta < 1` | Promo |
| `S = 1`, `Eta = 1` | Standard |

Mặc định `S = 1`, `Eta = 1` nên dùng Standard. Đổi `Pricing:S` thành `1.5`
để dùng Surge; đặt `S = 1`, `Eta = 0.8` để dùng Promo giảm 20%.
Khởi động lại Matching sau khi sửa cấu hình. Biến môi trường tương ứng là
`Pricing__S` và `Pricing__Eta`, có độ ưu tiên cao hơn appsettings.

Hiện chưa có quy tắc tự tăng giá theo giờ, cung/cầu hoặc áp dụng mã khuyến mãi
cho từng khách. Cấu hình này áp dụng chung cho các quote mới của instance.
Quote đã lưu không được tính lại chỉ vì thay đổi cấu hình.

## Công thức riêng

Gọi `B = Cd × distanceKm + Ct × durationMin`:

| Strategy | Công thức |
| --- | --- |
| Standard | `(B + Cm) × (1 + Tax)` |
| Promo | `(B + Cm) × Eta × (1 + Tax)` |
| Surge | `(B × S + Cm) × Eta × (1 + Tax)` |

`Cd` là phí/km, `Ct` là phí/phút, `Cm` là phí cố định, `S` là hệ số tăng giá,
`Eta` là tỷ lệ giá còn lại sau giảm, `Tax` là thuế suất.

Surge được ưu tiên khi cả tăng giá và giảm giá cùng bật. Nó vẫn áp dụng `Eta`
để giữ quy tắc kết hợp hiện có. Phí cố định không bị nhân hệ số tăng giá.
Standard không áp dụng `S`/`Eta`; Promo không áp dụng `S`.

`PricingCalculator` chỉ kiểm tra dữ liệu và tính `B`. Mỗi strategy tự áp dụng
các hệ số của mình, không còn ba lớp gọi cùng một công thức hoàn chỉnh.

Với `Cd = 10000`, `Ct = 1000`, `Cm = 5000`, `Tax = 0.1`, chuyến 5 km / 10 phút:

| Cấu hình | Strategy | Giá |
| --- | --- | --- |
| S=1, Eta=1 | Standard | 71.500 |
| S=1.5, Eta=1 | Surge | 104.500 |
| S=1, Eta=0.8 | Promo | 57.200 |
| S=1.5, Eta=0.8 | Surge | 83.600 |
