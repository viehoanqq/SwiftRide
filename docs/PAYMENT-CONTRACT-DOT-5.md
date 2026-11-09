# Contract Payment – đợt 5

## Cập nhật: ánh xạ kết quả sang Trip

`PaymentResultMapping.Apply(trip, response.Status)` ở Trip.Application đã triển khai:
SUCCEEDED → MarkPaid(), FAILED → MarkPaymentFailed() qua State pattern. Chỉ PaymentPending
được chuyển sang kết quả cuối; mỗi lần chuyển thêm đúng một history. Kết quả cuối giống nhau
gửi lặp không thay đổi history/UpdatedAt; kết quả trái ngược bị chặn. PENDING không hạ trạng thái
cuối. REFUNDED giữ nguyên Trip Paid; nếu Trip chưa Paid thì từ chối kết quả không nhất quán.

Mapper chưa nối vào dropoff vì PaymentClient/workflow HTTP chưa triển khai. Người tích hợp phải
xác minh kết quả thuộc đúng Trip, gọi mapper rồi SaveChanges. Không chuyển timeout/HTTP error
thành kết quả FAILED. Đây là hoàn thiện quy tắc Domain/ánh xạ, không phải hoàn thiện API thu tiền.

Contract được chốt dựa trên entity/schema hiện tại. Đây là chữ ký và quy ước triển khai,
không phải thông báo các API Payment đã hoạt động.

## Entity và ánh xạ

- Ví là `Account`, không tạo thêm entity Wallet. OwnerId là GUID dạng D của user hoặc
  `PLATFORM` / `GATEWAY`; dùng cả OwnerId và OwnerType để xác định tài khoản.
- Domain PaymentStatus giữ nguyên Pending/Succeeded/Failed/Refunded.
- JSON trả PENDING/SUCCEEDED/FAILED/REFUNDED; không dùng PAID cho Payment.
- Trip ánh xạ SUCCEEDED → Paid, FAILED → PaymentFailed. PENDING hoặc lỗi truyền tải:
  giữ PaymentPending và xác minh lại. REFUNDED không tự đổi Trip vì Trip chưa có state refund.
- Domain Method Wallet/Card → JSON WALLET/CARD; EntryType Charge/Refund → CHARGE/REFUND.
- DTO HTTP dùng chung tại `SwiftRide.Contracts/Payments/PaymentContracts.cs`; không copy DTO
  sang Trip với kiểu/enum khác. WalletResponse và metadata IsReplay nằm ở Payment.Application/DTOs.
- Dùng decimal, precision 18,2, tiền VND; Amount > 0 và không quá 2 chữ số thập phân.
  Driver nhận Round(amount * 0.8m, 2, AwayFromZero), Platform = amount - driverAmount.
- WALLET ghi ba bút toán: Rider -amount, Driver +driverAmount, Platform +platformAmount.
  CARD giả lập dùng GATEWAY làm tài khoản đối ứng -amount, không trừ ví Rider.
  Chốt implementation giả lập CardToken trước khi bật CARD; token thiếu/không hợp lệ trả 400.
  TripFare hiện chưa lưu CardToken: không tự suy ra token từ PaymentMethod.Card.
- DriverAmount/PlatformAmount trong response là tổng CHARGE thực tế của tài khoản tương ứng;
  trả 0 nếu chưa ghi CHARGE. Khi REFUNDED vẫn giữ giá trị CHARGE gốc, FE phải xét status.
- failureReason là mã ổn định (INSUFFICIENT_FUNDS/CARD_DECLINED) hoặc null, không chứa lỗi DB.
- AttemptCount đếm lần xử lý thanh toán thực tế. Replay/GET/refund không tăng bộ đếm.

## HTTP

### Quy tắc Domain đã triển khai

- `MoneyRules.Split(amount, driverRate)` trả Total/DriverAmount/PlatformAmount. Reject tiền âm, bằng 0,
  quá numeric(18,2), hoặc có hơn 2 chữ số lẻ; không âm thầm làm tròn số tiền đầu vào.
- Ví dụ 50.000 → Driver 40.000, Platform 10.000; 10,03 → 8,02 + 2,01.
  Với số tiền rất nhỏ (0,01), phần Platform có thể là 0 do đơn vị làm tròn.
- `Payment.CompleteWalletAttempt(balance)` kiểm tra toàn bộ Amount, rồi ghi Succeeded hoặc
  Failed/INSUFFICIENT_FUNDS. Balance bằng Amount vẫn thành công. Phương thức này chỉ quyết định
  kết quả, KHÔNG sửa Account hoặc ghi ledger. Application phải khóa Account trước khi gọi và
  lưu outcome + số dư + ledger cùng transaction.
- CARD dùng BeginAttempt rồi MarkSucceeded hoặc MarkFailed(CARD_DECLINED) sau kết quả provider.
  Lỗi timeout không phải failureReason nghiệp vụ: giữ Pending để đối soát.
- BeginAttempt chỉ một lần trong đợt này; gọi lặp/replay do Application đọc kết quả đã lưu,
  không gọi Domain lại. Pending có attempt chưa rõ kết quả phải đối soát trước, không thu lại.
- `MarkRefunded()` chỉ cho Succeeded → Refunded và không tăng AttemptCount. Nó không tự hoàn tiền:
  workflow refund phải kiểm tra số dư, đảo CHARGE và lưu receipt trong cùng transaction.
- Entity vẫn giữ setter hiện có để tương thích EF/mã nhóm; code nghiệp vụ phải dùng các method,
  không gán trực tiếp Status/AttemptCount để bỏ qua kiểm tra.

| Payment Domain / JSON | Trip cần chuyển tới | Ghi chú |
|---|---|---|
| Pending / PENDING | PaymentPending | Chưa có kết quả cuối, không coi là Failed |
| Succeeded / SUCCEEDED | Paid | Chỉ sau khi giao dịch Payment commit thành công |
| Failed / FAILED | PaymentFailed | Có failureReason nghiệp vụ |
| Refunded / REFUNDED | Không đổi tự động | Trip chưa có state Refunded; UI đọc trạng thái Payment |

PaymentRuleException được API ánh xạ VALIDATION_ERROR → 400; INVALID_PAYMENT_STATE,
PAYMENT_ALREADY_ATTEMPTED và REFUND_NOT_ALLOWED → 409. Thiếu tiền được lưu thành Payment FAILED,
không ném exception HTTP. Unit test quy tắc nằm ở `tests/PaymentService.Tests/PaymentRulesTests.cs`.

| Endpoint | Request | Thành công | Quyền |
|---|---|---|---|
| POST /payments | CreatePaymentRequest | 201 mới, 200 replay; CreatePaymentResponse | service payments:create |
| GET /payments/{id} | — | 200 PaymentDetailResponse | Rider có sub == payment.RiderId |
| GET /payments/by-trip/{tripId} | — | 200 PaymentDetailResponse | Rider chủ chuyến / Driver đúng payment.DriverId; hoặc service payments:read |
| POST /payments/{id}/refund | RefundPaymentRequest + Idempotency-Key | 200 RefundPaymentResponse, status REFUNDED | service payments:refund |
| GET /wallets/me | — | 200 WalletResponse | Rider/Driver, theo sub và role |

POST tạo payment thiếu tiền vẫn tạo Payment FAILED và trả 201 (replay trả 200).
GET không có payment trả 404 PAYMENT_NOT_FOUND. Client nội bộ chỉ chuyển 404 đó thành null,
không chuyển 401/403/5xx/timeout thành null.
Wallet entries sắp CreatedAt DESC rồi Id DESC; Amount giữ dấu, không đổi số tiền trừ thành dương.
Account không tồn tại trả 404 ACCOUNT_NOT_FOUND; không tự cấp số dư. Seed/provision ví thuộc phần nền tảng.
CardToken không bao giờ trả về trong response, log hoặc lỗi.

## Idempotency và transaction

- Payment.IdempotencyKey đã giới hạn 100 ký tự, unique; TripId cũng unique: mỗi chuyến một Payment.
- Trip tạo key bằng PaymentProtocol.TripPaymentKey(tripId), giữ nguyên qua retry/restart.
- Cùng key + cùng TripId/RiderId/DriverId/Amount/Method/CardToken: trả bản ghi cũ, không thu lại tiền,
  kể cả bản ghi FAILED. Cùng key khác payload: 409 IDEMPOTENCY_CONFLICT.
- Khác key nhưng cùng TripId: 409 TRIP_PAYMENT_EXISTS. Đợt này không tạo payment lần hai cho chuyến thất bại.
- Unique constraints là hàng rào cuối cùng cho request đồng thời, không chỉ SELECT kiểm tra trước INSERT.
- IUnitOfWork dùng chung một scoped DbContext với mọi repository. Callback cập nhật entity,
  stage Payment/Ledger/RefundReceipt; UnitOfWork SaveChanges rồi commit; lỗi rollback toàn bộ.
- Khóa Account theo thứ tự Id cố định; kiểm tra số dư sau khi khóa. Account.Version/xmin là lớp bảo vệ bổ sung.
- Không gọi HTTP gateway/thẻ trong transaction và không tự retry toàn callback có side effect.
- Sau unique/concurrency conflict: rollback, bỏ tracking cũ rồi đọc lại kết quả đã commit;
  so payload trước khi trả replay, không biến mọi lỗi unique thành thành công.
- Thiếu tiền: lưu FAILED, AttemptCount và FailureReason; không thay đổi số dư/ledger.
- Refund đảo dấu đúng CHARGE đã lưu, không tính lại tỷ lệ. Cùng key/payment/reason trả kết quả cũ;
  cùng key khác payment/reason → 409 IDEMPOTENCY_CONFLICT. Key khác với payment đã refunded → 409 REFUND_NOT_ALLOWED.
- Refund khi tài khoản cần trả lại tiền không đủ số dư → 409 REFUND_INSUFFICIENT_FUNDS,
  không hoàn một phần (GATEWAY là tài khoản clearing đối ứng, được phép âm).
- Entity hiện chưa có nơi lưu key/reason refund. Người làm DB cần bảng RefundReceipt riêng:
  unique IdempotencyKey (100), unique PaymentId, Reason (1–500 ký tự sau trim), response và CreatedAt.
  RefundReceipt, payment REFUNDED, số dư và REFUND ledger phải commit cùng transaction.
  KHÔNG dùng ProcessedMessage (dành cho message broker) để thay bảng refund idempotency.

## Mã lỗi

| HTTP | Code |
|---|---|
| 400 | VALIDATION_ERROR: GUID rỗng, amount/method/token/key/reason không hợp lệ |
| 401 | UNAUTHORIZED: thiếu/sai/hết hạn token |
| 403 | FORBIDDEN: sai chủ giao dịch, role hoặc scope |
| 404 | PAYMENT_NOT_FOUND, ACCOUNT_NOT_FOUND |
| 409 | IDEMPOTENCY_CONFLICT, TRIP_PAYMENT_EXISTS, CONCURRENCY_CONFLICT, REFUND_NOT_ALLOWED, REFUND_INSUFFICIENT_FUNDS |
| 500 | INTERNAL_ERROR: lỗi bất ngờ, không lộ chi tiết DB |
| 502 tại Trip | PAYMENT_UNAVAILABLE, PAYMENT_INVALID_RESPONSE |
| 504 tại Trip | PAYMENT_TIMEOUT |

Error body: `{ "code": "...", "message": "..." }`.

## Xác thực và Gateway

- Dùng JWT được xác thực issuer/audience/signature/expiry như các service hiện tại.
- CreatePolicy PaymentCreate: sub=trip-service + scope=payments:create.
- ReadInternalPolicy PaymentReadInternal: sub=trip-service + scope=payments:read.
- RefundPolicy PaymentRefund: sub=trip-service + scope=payments:refund.
- Các scope là claim riêng, không suy ra quyền nội bộ từ role Rider/Driver.
- PaymentClient phải dùng handler token service Payment, không forward token Rider/Driver.
  Handler hiện tại của Trip chỉ có drivers:register/drivers:status: CHƯA đủ quyền gọi Payment.
- Swagger refund cần token service có scope refund; token Rider/Driver không được phép.
- Gateway chỉ expose GET /payments/{id}, GET /payments/by-trip/{tripId}, GET /wallets/me.
  Không expose POST /payments và POST /payments/{id}/refund.
- GET by-trip cho service giúp xác minh kết quả khi POST timeout; vẫn cần kiểm tra policy ở Payment API.

## Tích hợp dropoff cho người phụ trách Trip

Hiện `MarkDroppedOffAsync` chỉ chuyển PaymentPending, lưu và gọi Matching Available.
Chưa có PaymentClient implementation hay HTTP call Payment.

Luồng cần triển khai sau contract:
1. Xác thực đúng Driver, Domain chuyển PickedUp → DroppedOff → PaymentPending, lưu history.
2. Tạo request từ Trip.Id, RiderId, offer Accepted.DriverId, Fare.Price, Fare.PaymentMethod,
   key ổn định. Không nhận amount/riderId/driverId tùy ý từ body của API dropoff.
3. Gọi IPaymentClient.CreateAsync; đọc SUCCEEDED/FAILED để chuyển Trip Paid/PaymentFailed và lưu.
4. Sau kết quả cuối cùng, gọi status client Available; lỗi Matching không đảo ngược thanh toán đã commit.
5. Nếu POST timeout/mất kết nối: tra GetByTripAsync hoặc POST lại cùng key. Chưa rõ kết quả thì
   giữ PaymentPending; cần đường resume riêng cho chuyến này, không gọi lại Domain.MarkDroppedOff().
6. Gọi lặp sau Paid/PaymentFailed không tạo charge/history mới. Có thể trả trạng thái hiện tại theo contract workflow.

Chưa thay đổi runtime/auth/schema trong bước chốt contract. Các việc tiếp theo:
Hoàng triển khai policy + migration/provision ví; Đạt triển khai repository/UoW/client + Trip states;
Minh triển khai Domain/payment workflow; Phong triển khai query/refund; Hiển nối FE; Mai kiểm thử.

## C?u h?nh t? l? chia ti?n

`Payment:DriverRate` trong appsettings.json l? 0.8 cho ??t 5. PaymentOptions ???c bind v? validate khi kh?i ??ng (b?t bu?c, t? 0 ??n 1). Application inject PaymentOptions v? g?i `MoneyRules.Split(payment.Amount, options.DriverRate)`. Domain kh?ng ??c IConfiguration. Kh?ng c?u h?nh PlatformRate ri?ng: Platform lu?n nh?n ph?n c?n l?i. Thay c?u h?nh c?n kh?i ??ng l?i service. Payment ?? x? l? v? refund d?ng ledger g?c, kh?ng t?nh l?i theo t? l? m?i.
