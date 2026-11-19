# Contract Card giả lập của Payment Service

## Adapter gateway: tra cứu và refund

`FakeCardAuthorizationGateway` lưu kết quả authorization theo `IdempotencyKey` trong
`fake_card_authorizations`. `FindAuthorizationAsync` tra cứu kết quả đã lưu sau khi service
khởi động lại. Cùng key và payload trả kết quả cũ; key cũ với token hoặc số tiền khác
trả `IDEMPOTENCY_CONFLICT`.

Refund Card gọi `RefundAsync` của adapter trong cùng transaction với
`PaymentSettlement.Refund` và `RefundReceipt`. Adapter chỉ nhận authorization `Approved`
với đúng số tiền gốc, lưu một bản ghi `fake_card_refunds` theo refund key.
Cùng refund key trả kết quả cũ; một authorization không thể có hai refund key.
Adapter không tự sửa số dư hoặc ledger; toàn bộ đây là giao dịch giả lập.

Card giả lập dùng **ba token cố định** để kiểm tra luồng thanh toán trong `Development` và `Testing`. Nó không kết nối ngân hàng, không nhận số thẻ/CVV và không thực hiện thu hoặc hoàn tiền thật.

## Bật tính năng

`Payment:FakeCard:Enabled=true` trong `src/PaymentService/PaymentService.Api/appsettings.Development.json`. `Program.cs` từ chối cấu hình bật tính năng ngoài `Development`/`Testing`; `DependencyInjection.cs` chỉ đăng ký `FakeCardAuthorizationGateway` và `CardPaymentProcessor` trong hai môi trường này. `PaymentOptions.FakeCard.Enabled` mặc định `false`. Trong Production, `CARD` bị từ chối; `WALLET` tiếp tục hoạt động.

## Input và kết quả

`POST /payments` dùng `CreatePaymentRequest`: `Method="CARD"`, `CardToken` và `IdempotencyKey`. Luồng Trip nhận `CardToken` khi xác nhận chuyến, lưu vào `TripFare` và truyền tiếp trong `RideCompleted` lúc dropoff.

| `CardToken` | Kết quả giả lập | Payment | Ledger/số dư | Kết quả cho Trip |
| --- | --- | --- | --- | --- |
| `test-card-approved` | `Approved` | `SUCCEEDED`, `AttemptCount=1` | GATEWAY `-Amount`, Driver `+DriverAmount`, Platform `+PlatformAmount`; Rider wallet không đổi | `PaymentSettled` nếu đi qua consumer |
| `test-card-declined` | `Declined` | `FAILED`, `FailureReason=CARD_DECLINED`, `AttemptCount=1` | Không thay đổi | `PaymentFailed` nếu đi qua consumer |
| `test-card-unknown` | `Unknown` | `PENDING`, `AttemptCount=1` | Không thay đổi | **Không phát kết quả cuối**; cần đối soát trước khi quyết định |
| token thiếu/sai | Không gọi settlement | Không lưu payment mới | Không thay đổi | `400 VALIDATION_ERROR` khi gọi API; event lỗi đi qua cơ chế `_error` |

`Unknown` **không phải** `Declined`: không được gọi `MarkFailed("CARD_DECLINED")`, không được tự chạy lại attempt để tránh thu hai lần khi thay provider thật. Trong bản giả lập, `RideCompletedConsumer` lưu payment `PENDING` và dấu đã nhận event, nhưng không lưu `PaymentSettled`/`PaymentFailed` vào outbox. Hiện **chưa có worker đối soát hoặc timeout tự chuyển trạng thái**; Trip sẽ ở `PaymentPending` cho tới khi có workflow đối soát riêng.

## Idempotency và refund

`PaymentCheckoutService` giữ khóa request theo `IdempotencyKey` và `TripId` trước khi chọn processor. Cùng key và cùng payload, kể cả token, trả lại payment đã lưu, không gọi Card giả lập thêm lần nữa; khác payload trả `409 IDEMPOTENCY_CONFLICT`. Key khác cho cùng TripId trả `409 TRIP_PAYMENT_EXISTS`. `Unknown` cũng giữ key và payment `PENDING` để replay không đổi token thành `Approved`.

Refund dùng `RefundAppService` và `PaymentSettlement.Refund` hiện có. Chỉ payment `SUCCEEDED` được refund; refund đảo **đúng ba CHARGE gốc**, không tính lại tỷ lệ chia, lưu receipt theo refund idempotency key. Payment Card `FAILED`/`PENDING` không được refund. Đây là hoàn tiền trong sổ cái giả lập, không gọi provider bên ngoài.

## Factory và Domain

```text
PaymentCheckoutService
  → IPaymentMethodProcessorFactory.For(Method)
      → WalletPaymentProcessor → PaymentSettlement.ChargeWallet(...)
      → CardPaymentProcessor   → ICardAuthorizationGateway
                                → PaymentSettlement.ChargeCard(...)
  → UnitOfWork lưu Payment + Account + Ledger trong PostgreSQL
```

Factory chỉ **chọn** processor. Cả hai processor dùng `PaymentSettlement`, `MoneyRules.Split` và `PaymentOptions.DriverRate` đang có. Không có `LedgerService` mới hoặc tỷ lệ chia `80/20` hardcode. `FakeCardAuthorizationGateway` lưu quyết định và refund giả lập; nó không sửa balance/ledger. Domain `PaymentSettlement.ChargeCard` chia tiền và ghi ba bút toán. `PaymentSettlement.Refund` xử lý hoàn tiền cho cả Wallet và Card.

## Vị trí code

- Contract token/kết quả: `PaymentService.Application/DTOs/CardAuthorizationResult.cs`.
- Cổng giả lập: `PaymentService.Application/Interfaces/ICardAuthorizationGateway.cs` và `PaymentService.Infrastructure/Messaging/FakeCardAuthorizationGateway.cs`.
- Factory/processor: `PaymentService.Application/Interfaces/IPaymentMethodProcessor.cs`, `Services/PaymentMethodProcessorFactory.cs`, `Services/WalletPaymentProcessor.cs`, `Services/CardPaymentProcessor.cs`.
- Khóa idempotency/transaction: `PaymentService.Application/Services/PaymentCheckoutService.cs`, `PaymentService.Infrastructure/Repositories/UnitOfWork.cs`.
- Quy tắc tiền/refund: `PaymentService.Domain/Services/PaymentSettlement.cs`, `PaymentService.Domain/Rules/MoneyRules.cs`, `PaymentService.Application/Services/RefundAppService.cs`.
