# Contract Saga và bồi thường sau thanh toán

**Phạm vi:** chuyến đã `DroppedOff`, Payment thu tiền bất đồng bộ qua `RideCompleted`. Tài liệu này chốt hành vi cần triển khai ở bước Saga tiếp theo; các mục ghi **Cần triển khai** chưa có trong code hiện tại.

## Trước khi có tài xế: giữ tiền khác với thu tiền

Ở nhiều ứng dụng, khoản tiền hiển thị “đang xử lý” trước chuyến là **authorization hold** (giữ hạn mức), chưa phải charge cuối. Khi chuyến bị hủy, hệ thống hủy/release hold; sau khi hoàn tất mới capture khoản thực thu. Đây là một mô hình khác với việc charge trước rồi refund.

SwiftRide hiện **chưa có authorization hold/reservation**. Rider chọn phương thức ở bước confirm, nhưng Payment chỉ thu tiền khi Trip phát `RideCompleted` sau dropoff. Vì vậy tài xế hủy **trước dropoff** thì không có khoản charge của chuyến để refund. Cần cập nhật trạng thái Trip và giải phóng tài xế đúng chuyến, nhưng không gọi `RefundAppService`. Nếu dự án sau này chọn mô hình giữ tiền trước khi tìm tài xế, phải bổ sung contract `Authorize/Release/Capture` riêng; hủy chuyến sẽ `Release`, còn `Refund` chỉ dùng khi đã `Capture`/charge thành công. Không mô phỏng việc giữ tiền bằng `PaymentStatus.SUCCEEDED`.

## 1. Luồng đang có và điểm có thể thất bại

```mermaid
flowchart LR
    A[Trip: DroppedOff] --> B[Trip: PaymentPending + RideCompleted trong outbox]
    B --> C[Payment: checkout + ledger + result trong outbox]
    C --> D[Trip nhận PaymentSettled hoặc PaymentFailed]
    D --> E[Trip: Paid hoặc PaymentFailed + TripClosed trong outbox]
    E --> F[Matching giải phóng driver]
```

**Ranh giới Payment Saga:** Trip theo dõi yêu cầu thu tiền từ `RideCompleted` đến khi nhận `PaymentSettled` hoặc `PaymentFailed`. Nếu quá hạn, Saga tra Payment theo `TripId` rồi retry/đối soát; timeout không tự biến thành lỗi thanh toán. Nếu có quyết định hoàn hợp lệ sau khi đã thu tiền, Saga theo dõi `RefundPayment` đến `PaymentRefunded` hoặc `PaymentRefundFailed`. Việc Trip phát `TripClosed` và Matching giải phóng tài xế vẫn chạy theo event hiện có, ngoài phạm vi Saga này.

| Điểm lỗi | Trạng thái tiền | Quyết định |
| --- | --- | --- |
| Trip outbox chưa phát `RideCompleted`, RabbitMQ/Payment tạm ngừng | Chưa biết đã thu hay chưa; kiểm tra Payment theo `TripId` | Retry phát/nhận, giữ Trip `PaymentPending`; không tự refund. |
| Gateway trả `Unknown` | Payment `PENDING`, chưa ghi charge trong ledger nội bộ | Giữ Trip `PaymentPending`, đối soát authorization; không coi là thất bại và không refund khi chưa biết kết quả. |
| Wallet thiếu tiền hoặc Card `Declined` | Payment `FAILED`, không có charge | Trip `PaymentFailed`; không refund vì chưa thu tiền. |
| Payment `SUCCEEDED` nhưng `PaymentSettled` chưa đến Trip | Đã thu tiền, Trip vẫn `PaymentPending` | Retry outbox/consumer và đối soát theo `TripId`; không tự refund vì chuyến đã hoàn tất. |
| Trip nhận `PaymentSettled` nhưng DB tạm lỗi | Đã thu tiền, Trip chưa `Paid` | Retry cùng event; chống xử lý trùng bằng `EventId`. Không refund. |
| Trip từ chối `PaymentSettled` vì dữ liệu không khớp chuyến | Có thể đã thu tiền sai | Dừng tự động, đưa vào hàng đối soát. Chỉ refund sau khi xác minh giao dịch không hợp lệ. |
| Trip đã `Paid` nhưng chưa phát `TripClosed`, hoặc Matching tạm lỗi | Đã thu tiền đúng | Retry `TripClosed`; không refund vì lỗi giải phóng driver không làm chuyến mất hiệu lực. |

**Quy tắc chính:** lỗi mạng, consumer, outbox hoặc Matching **không phải điều kiện refund**. Không lấy việc một message vào `_error` làm bằng chứng phải hoàn tiền. Cần kiểm tra trạng thái Payment và sự kiện gốc trước khi quyết định.

## 2. Khi nào được bồi thường bằng refund?

Chỉ tạo yêu cầu refund khi thỏa **tất cả** điều kiện:

1. Payment của đúng `TripId` là `SUCCEEDED`, có đủ ba dòng `CHARGE` gốc và chưa `REFUNDED`.
2. Có kết luận nghiệp vụ rằng khoản thu **không còn hợp lệ**: chuyến bị xác minh sai sau khi thu tiền, khoản thu sai được đối soát xác nhận, hoặc yêu cầu hoàn được người có quyền duyệt theo quy trình hỗ trợ.
3. `PaymentId`, `TripId`, số tiền, Rider, Driver và phương thức khớp bản ghi gốc. Một Trip không được có hai compensation đang chạy.
4. Có `RefundReasonCode` ổn định và `IdempotencyKey` cố định cho cùng một quyết định bồi thường. Retry phải dùng lại key; không tạo key mới.

**Không refund** khi Payment là `PENDING`/`FAILED`, khi chỉ có lỗi hạ tầng, hoặc khi chưa xác minh được charge. Với Card `Unknown`, tra cứu authorization trước; không tự suy diễn `Unknown = Declined` hay `Unknown = Approved`.

Mã lý do dùng trong Saga (không chứa mô tả tự do):

| `RefundReasonCode` | Điều kiện xác minh | Quyền duyệt |
| --- | --- | --- |
| `INVALID_TRIP_CHARGE` | Chuyến/giá cước không hợp lệ nhưng Payment đã `SUCCEEDED`; có kết luận đối soát gắn `TripId` và `PaymentId` | Nhân sự hỗ trợ được cấp quyền refund |
| `APPROVED_CUSTOMER_REFUND` | Chuyến hợp lệ nhưng yêu cầu hoàn của khách đã được duyệt và ghi lý do trong hồ sơ hỗ trợ | Nhân sự hỗ trợ được cấp quyền refund |

Không thêm mã “lỗi Matching”, “RabbitMQ timeout” hay “consumer vào `_error`”: các tình huống đó cần retry/đối soát kỹ thuật. Mỗi quyết định lưu người duyệt, thời điểm, reason code và tham chiếu bằng chứng ở hồ sơ nội bộ; event chỉ mang reason code.

Refund hiện có ở `RefundAppService` + `PaymentSettlement.Refund`: khóa payment/refund key và các tài khoản, đảo **đúng ledger CHARGE gốc**, lưu `RefundReceipt`; Card ghi thêm refund ở gateway giả lập. Không dùng `DriverRate` hiện tại để tính lại phần chia. Nếu Driver/Platform không đủ số dư để đảo ledger, refund hiện trả `REFUND_INSUFFICIENT_FUNDS`; Saga giữ yêu cầu ở trạng thái chờ xử lý, không báo đã hoàn.

## 3. Trạng thái Trip sau bồi thường — Cần triển khai

Hiện `TripStatus` mới có `PaymentPending`, `Paid`, `PaymentFailed`; **chưa có trạng thái bồi thường**. Chốt bổ sung:

| Trip status | Ý nghĩa | Trạng thái Payment tương ứng | Người dùng thấy |
| --- | --- | --- | --- |
| `PaymentPending` | Chưa có kết quả thanh toán cuối | `PENDING` hoặc chưa thấy Payment | “Chuyến đã kết thúc, đang xử lý thanh toán.” |
| `Paid` | Đã thu tiền, chưa có quyết định refund | `SUCCEEDED` | “Đã thanh toán.” |
| `PaymentFailed` | Thu tiền thất bại, không có charge | `FAILED` | “Thanh toán thất bại; chưa bị trừ tiền.” |
| `RefundPending` | Đã chấp thuận bồi thường, đang hoàn/đối soát | Thường vẫn `SUCCEEDED`; chưa khẳng định đã hoàn | “Đang xử lý hoàn tiền.” |
| `Refunded` | Payment đã `REFUNDED` và Trip đã nhận kết quả | `REFUNDED` | “Đã hoàn tiền.” Hiển thị số tiền và mã tham chiếu. |

Các chuyển trạng thái hợp lệ: `Paid → RefundPending → Refunded`; trường hợp Payment đã `SUCCEEDED` nhưng Trip chưa nhận được kết quả và khoản thu được xác minh sai: `PaymentPending → RefundPending → Refunded`. Refund lỗi tạm thời giữ `RefundPending` để retry/đối soát. Không chuyển sang `PaymentFailed` sau một khoản thu thành công vì trạng thái đó nói rằng chưa thu được tiền.

Khi Trip ở `RefundPending`, API đọc chuyến cần trả trạng thái đó cùng thông tin thanh toán đủ để UI hiển thị “đang hoàn”, **không trả `CardToken`**. Chỉ hiển thị “đã hoàn” sau khi Payment commit refund và Trip nhận xác nhận. Các trường hợp chờ lâu cần hiển thị mã tham chiếu `TripId`/`PaymentId` để hỗ trợ tra cứu; không hứa thời gian hoàn tiền khi chưa có SLA được cấu hình.

### Bảng chuyển trạng thái Payment Saga

Quy tắc thuần đã được định nghĩa trong `TripService.Application/Sagas/PaymentSagaTransitions.cs`. Saga persistence và consumer timeout đã gọi quy tắc này; dropoff chưa khởi tạo Saga trong runtime. `Completed` nghĩa là Trip đã ghi `Paid` cùng history/outbox, không đợi Matching giải phóng tài xế. `Failed` chỉ dùng khi Payment xác nhận chưa thu được tiền. `NeedsAttention` giữ nguyên trạng thái Trip đã biết và thêm cờ cần đối soát; nó không tự biến khoản thu thành thất bại.

| Saga hiện tại | Tín hiệu | Saga tiếp theo | Điều kiện và thao tác cần lưu cùng transaction |
| --- | --- | --- | --- |
| `AwaitingPayment` | `PaymentSettled` | `Completed` | Đối chiếu Trip/Payment/EventId; ghi Trip `Paid`, history, `TripClosed`, checkpoint. |
| `AwaitingPayment` | `PaymentDeclined` | `Failed` | Payment xác nhận `FAILED`; ghi Trip `PaymentFailed`, history, `TripClosed`, checkpoint. |
| `AwaitingPayment` | `PaymentOutcomeUnknown` | `PaymentUncertain` | Timeout hoặc Card `Unknown`; tra Payment theo `TripId`, chưa kết luận thất bại. |
| `PaymentUncertain` | `PaymentStillPending` hoặc `PaymentOutcomeUnknown` | `PaymentUncertain` | Giữ yêu cầu gốc, lên lịch đối soát lại; không tạo payment key mới. |
| `PaymentUncertain` | `PaymentSettled`/`VerifiedPaymentSettled` | `Completed` | Kết quả đến muộn hoặc đối soát xác nhận đã charge; ghi Trip `Paid`. |
| `PaymentUncertain` | `PaymentDeclined`/`VerifiedPaymentDeclined` | `Failed` | Chỉ khi xác nhận không có charge; ghi Trip `PaymentFailed`. |
| `AwaitingPayment`/`PaymentUncertain` | `ManualReviewRequired` | `NeedsAttention` | Sai TripId, PaymentId, amount hoặc không thể đối soát; không refund tự động. |
| `Completed` | `ApprovedRefundForVerifiedCharge` | `Compensating` | Quyết định refund được duyệt, Payment còn `SUCCEEDED`; ghi Trip `RefundPending` và `RefundPayment` outbox. |
| `PaymentUncertain`/`NeedsAttention` | `ApprovedRefundForVerifiedCharge` | `Compensating` | Đã xác minh charge thật và quyết định refund; ghi Trip `RefundPending` và command cùng key cố định. |
| `Compensating` | `PaymentRefunded` | `Compensated` | Đối chiếu refund; ghi Trip `Refunded`, history và checkpoint. |
| `Compensating` | `RefundOutcomeUnknown` | `Compensating` | Timeout/lỗi kết nối: tra Payment/refund receipt trước khi retry. |
| `Compensating` | `RefundRejected` hoặc `ManualReviewRequired` | `NeedsAttention` | Kết quả từ chối xác định; giữ thông tin đã thu, không báo khách đã hoàn. |
| `NeedsAttention` | `VerifiedPaymentSettled`/`VerifiedPaymentDeclined` | `Completed`/`Failed` | Người có quyền đối soát và ghi kết luận; đồng bộ Trip với kết quả xác minh. |
| `Completed`/`Failed`/`Compensated` | Timeout cũ | Giữ nguyên | Chỉ bỏ qua sau khi kiểm tra timeout thuộc đúng command gốc. |

Các cặp trạng thái/tín hiệu không có trong bảng bị từ chối. Consumer xử lý duplicate cùng `EventId` và payload bằng checkpoint **trước** khi gọi bảng chuyển trạng thái; `EventId` trùng payload khác là lỗi. `ApprovedRefundForVerifiedCharge` chỉ được phát sau khi application đã xác minh Payment `SUCCEEDED`, đúng Trip/amount và có quyết định được duyệt. Bảng thuần không truy vấn DB hoặc tự phê duyệt refund.

### Persistence của Saga

`PaymentSagaInstance` đã có model và bảng `PaymentSagas` trong SQL Server. Một Saga gắn với đúng một Trip: `TripId` và `CorrelationId` đều có unique index; `RowVersion` chặn hai worker ghi đè trạng thái của nhau. Bảng lưu `ProcessPaymentEventId`, deadline thanh toán, `PaymentId` nếu đã biết, `RefundPaymentEventId`/deadline refund nếu có, trạng thái và thời gian cập nhật.

`PaymentSagaRepository` dùng chung `TripDbContext` với `TripRepository`. Khi bắt đầu Saga ở dropoff, thêm Saga và `ProcessPayment` vào context rồi gọi **một lần** `TripRepository.SaveChangesAsync`; nhánh `PaymentPending` hiện có transaction/khóa gán tài xế. Khi xử lý kết quả hay refund, thay đổi Saga, Trip, history, checkpoint và outbox trên cùng context rồi gọi **một lần** `PaymentSagaRepository.SaveChangesAsync` để commit/rollback toàn bộ. Không gọi hai `SaveChangesAsync` riêng cho hai repository.

Trip outbox đã chấp nhận và publisher đã biết cách phát `ProcessPayment`/`RefundPayment`/`ProcessPaymentTimedOut`. Scheduler và consumer timeout đã có; runtime chưa tạo command `ProcessPayment`, luồng `RideCompleted` hiện tại vẫn chạy. Khi chuyển sang `ProcessPayment`, cần đổi `TripPaymentResultHandler` để đối chiếu `CausationId` với command mới thay vì chỉ tìm `RideCompleted` như hiện nay.

## 4. Hợp đồng message cần thêm — Cần triển khai

Luồng runtime cũ vẫn còn để tránh ngắt thanh toán trước khi Trip chuyển sang Saga. Các record Payment Saga `ProcessPayment`, `ProcessPaymentTimedOut`, `RefundPayment`, `PaymentRefunded`, `PaymentRefundFailed` đã được khai báo ở `SwiftRide.Contracts.Events`. Payment đã nhận `ProcessPayment` và `RefundPayment`, và phát kết quả refund qua outbox; Trip chưa phát command Saga hoặc nhận kết quả refund. `PaymentRefunded` dùng queue dự kiến `trip.payment-refunded`:

```csharp
public sealed record PaymentRefunded(
    Guid EventId,
    Guid CorrelationId,   // TripId
    Guid? CausationId,    // Event khởi phát quyết định bồi thường, nếu có
    DateTime OccurredAt,
    Guid TripId,
    Guid PaymentId,
    decimal Amount,
    string ReasonCode,
    int SchemaVersion = EventSchema.V1) : IIntegrationEvent;
```

Payment lưu `PaymentRefunded` vào **Payment outbox trong cùng transaction** với Payment `REFUNDED`, số dư, ledger và `RefundReceipt`; publisher phát sau commit. Trip consumer kiểm tra `TripId`, `PaymentId`, `Amount`, trạng thái `RefundPending` và `EventId`, rồi chuyển `Refunded` cùng history/checkpoint trong một lần lưu. Duplicate cùng `EventId` và payload là no-op; `EventId` trùng nhưng payload khác là lỗi. Không đưa token, số thẻ hoặc lý do tự do vào event/log.

`TripClosed` hiện chỉ chấp nhận `Paid`/`PaymentFailed` ở Matching. Chốt quy tắc:

- Nếu Trip đã phát `TripClosed(Paid)`, refund về sau **không phát lại** `TripClosed`; Matching đã giải phóng driver. `PaymentRefunded` chỉ cập nhật Trip/Payment.
- Nếu Trip chưa từng phát `TripClosed` vì kết quả Payment chưa áp dụng, sau khi chuyển `Refunded` Trip phát **một** `TripClosed(Refunded)`; Matching phải chấp nhận `Refunded` và vẫn chỉ giải phóng driver của đúng chuyến đó.
- Driver release là tác vụ hậu xử lý có retry, không phải điều kiện để công nhận refund.

`CorrelationId = TripId` xuyên suốt. `CausationId` trỏ đến event gây ra bước kế tiếp khi có event nguồn; `RefundReasonCode` là mã nghiệp vụ, không chứa dữ liệu nhạy cảm. Cần bổ sung mapping log trong BuildingBlocks khi nối luồng runtime và version contract nếu thay đổi các event hiện hữu. Các message Saga là contract mới, không đổi payload của bảy event cũ. `PaymentProtocol.TripPaymentKey(TripId)` giữ key checkout hiện có; `PaymentProtocol.TripRefundKey(TripId)` giữ một key refund cố định cho mỗi chuyến.

## 5. Ai quyết định và ai thực thi?

```mermaid
sequenceDiagram
    participant R as Rider / hỗ trợ
    participant S as Trip Saga
    participant P as Payment
    participant M as Matching
    R->>S: Yêu cầu/duyệt bồi thường cho TripId
    S->>S: Xác minh charge và ghi RefundPending + quyết định duy nhất
    S->>P: Refund command với PaymentId, reason code, idempotency key
    P->>P: Khóa, đảo ledger gốc, lưu receipt + PaymentRefunded outbox
    P-->>S: PaymentRefunded qua RabbitMQ
    S->>S: Kiểm tra kết quả, ghi Refunded + history
    opt Chưa từng phát TripClosed
        S-->>M: TripClosed(Refunded)
    end
    S-->>R: Hiển thị Đã hoàn tiền
```

Trip Saga là nơi **quyết định** có bồi thường; Payment là nơi **thực thi** refund và xác nhận kết quả. Không gọi refund trực tiếp chỉ vì một consumer throw exception. Lệnh refund phải có phân quyền phù hợp, được lưu bền trước khi gửi/retry, và dùng key gắn với `TripId` + quyết định bồi thường để không hoàn hai lần. Endpoint `POST /payments/{id}/refund` hiện đã có nhưng **chưa phải một Saga điều phối tự động**; nó chưa phát `PaymentRefunded` và Trip chưa có consumer/status mới.

## 6. Tiêu chí review trước khi triển khai

- Nhóm review hai `RefundReasonCode`, quyền duyệt và bằng chứng đã chốt ở mục 2 trước khi code Saga.
- Chốt một compensation duy nhất cho mỗi Trip/Payment; retry giữ nguyên key và không nhân đôi ledger/refund.
- Test các trường hợp: Payment đã thu nhưng Trip/Matching tạm lỗi; `Unknown`; mismatch cần đối soát; refund thành công; refund thiếu số dư người nhận; duplicate/out-of-order `PaymentRefunded`; `TripClosed` đến muộn khi driver đã nhận chuyến mới.
- UI/API phân biệt rõ `PaymentPending`, `RefundPending`, `Refunded` và `PaymentFailed`; chỉ báo hoàn tiền khi Payment xác nhận.

## Scheduler timeout thanh toán

`PaymentSagaTimeoutScheduler` quét `PaymentSagas` mỗi `PaymentSaga__TimeoutPollSeconds` (mặc định 5 giây), tối đa `PaymentSaga__TimeoutBatchSize` (mặc định 50) Saga mỗi lượt. Nó chỉ chọn Saga `AwaitingPayment` đã qua `PaymentDeadlineAt` và chưa có `PaymentTimeoutEventId`. SQL Server `UPDLOCK/READPAST` khóa các dòng được chọn để nhiều bản Trip không cùng lập lịch. Trong một transaction, worker lưu `PaymentTimeoutEventId` và event `ProcessPaymentTimedOut` vào Trip outbox. Publisher hiện có gửi event qua RabbitMQ; nếu broker tắt, outbox giữ event và gửi lại khi kết nối phục hồi.

Consumer `trip.process-payment-timed-out` kiểm tra TripId, correlation, command ID, deadline và timeout event ID. Nếu kết quả thanh toán đã đến hoặc timeout cũ, nó bỏ qua. Nếu còn `AwaitingPayment`, Saga chuyển sang `PaymentUncertain`; Trip không bị đổi thành `PaymentFailed` và không tự refund. Trạng thái này cần bước đối soát Payment trước khi chốt thành công, thất bại hoặc bồi thường. Retry tiêu thụ dùng giới hạn trong `SwiftRide.Messaging`; sau khi hết retry MassTransit chuyển tin sang queue `_error`.

Hiện tại luồng dropoff vẫn phát `RideCompleted` và chưa khởi tạo Saga/`ProcessPayment`, nên scheduler chỉ hoạt động với Saga được tạo trong bước tích hợp tiếp theo. Refund timeout chưa được lập lịch vì Trip chưa phát `RefundPayment` trong runtime.

## Payment thực thi lệnh hoàn

`RefundPaymentConsumer` nhận queue `payment.refund-payment`, kiểm tra correlation, `PaymentId`, số tiền, reason code và key `trip:{TripId}:refund:v1`. Consumer dùng `RefundAppService.RefundInsideTransactionAsync` để giữ nguyên quy tắc đảo ledger gốc và gateway CARD. Trong cùng transaction, Payment lưu refund, `RefundReceipt`, checkpoint `ProcessedMessage` và `PaymentRefunded` trong outbox. Gửi lại cùng `EventId` chỉ đọc checkpoint, không tạo refund thứ hai. `PaymentOutboxPublisher` phát kết quả sau commit.

Payment không tìm thấy, sai Trip/số tiền, refund không hợp lệ hoặc thiếu số dư người nhận được trả bằng `PaymentRefundFailed` với mã ổn định. Khi nghiệp vụ refund ném lỗi xác định, giao dịch thử hoàn rollback trước; checkpoint và event thất bại được lưu trong giao dịch mới. Lỗi hạ tầng/gateway không bị coi là refund thất bại: MassTransit retry có giới hạn và sau đó chuyển `_error` để điều tra. Trip vẫn cần consumer kết quả refund và bước phát command để hoàn tất Saga.
