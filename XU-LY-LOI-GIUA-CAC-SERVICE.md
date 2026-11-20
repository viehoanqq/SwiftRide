# SwiftRide: service lỗi, bị tắt và cách khôi phục

Đối chiếu code ngày 20/11/2026. Tài liệu mô tả hành vi đã triển khai, không phải cam kết rằng mọi lỗi đều tự hồi phục. Các con số bên dưới là giá trị trong source/config tại thời điểm đọc; biến môi trường có thể ghi đè. Không chứa thông tin bí mật từ `.env`.

## 1. Phân biệt bốn cơ chế chờ và thử lại

| Cơ chế | Ai chờ/thử lại? | Ý nghĩa |
| --- | --- | --- |
| HTTP | Service gọi chờ response | Hết timeout thì request thất bại; không tự được hưởng retry RabbitMQ |
| Outbox | Worker đọc DB rồi publish | Giữ sự kiện đã commit nhưng chưa gửi thành công, thử gửi lại sau |
| Consumer retry | MassTransit chạy lại consumer | Xử lý message lỗi có thể được thử lại giới hạn; hết lượt chuyển `_error` |
| Saga reconciliation | Worker Trip hỏi Payment định kỳ | Đối chiếu kết quả chưa chắc chắn, không tự kết luận thất bại vì mất kết nối |
| Postman polling | Script GET nhiều lần | Chỉ quan sát trạng thái; dừng Runner không hủy nghiệp vụ phía server |

**HTTP timeout, consumer retry và Saga deadline là ba thứ khác nhau.** `PAYMENT_TIMEOUT` từ HTTP không phải event `ProcessPaymentTimedOut` của Saga.

## 2. Các luồng giao tiếp hiện tại

```text
Client → Gateway → Identity / Trip / Matching / Payment           [HTTP]
Identity → UserRegistered → Payment tạo ví                        [Outbox + RabbitMQ]
Identity → DriverRegistered → Matching tạo hồ sơ                  [Outbox + RabbitMQ]
Trip → QuoteRequested → Matching → QuoteCalculated / QuoteFailed  [Outbox + RabbitMQ]
Trip → MatchRequested → Matching → DriverMatched / MatchFailed    [Outbox + RabbitMQ]
Trip confirm WALLET → Payment đọc số dư                           [HTTP, tối đa 3s]
Trip accept → TripAccepted → Matching đánh dấu Busy               [Outbox + RabbitMQ]
Trip dropoff → ProcessPayment → Payment                          [Outbox + RabbitMQ]
Payment → PaymentSettled / PaymentFailed → Trip                   [Outbox + RabbitMQ]
Trip → TripClosed → Matching giải phóng đúng chuyến               [Outbox + RabbitMQ]
Trip duyệt hoàn → RefundPayment → Payment                         [Outbox + RabbitMQ]
Payment → PaymentRefunded / PaymentRefundFailed → Trip             [Outbox + RabbitMQ]
Trip đối soát Saga → Payment tra cứu payment / refund receipt      [HTTP]
```

`RideCompletedConsumer` vẫn được đăng ký cho đường cũ; dropoff trong `TripAppService` hiện tạo `ProcessPayment`. Không tự phát cả hai để cố thu tiền lại.

## 3. HTTP: khi nào trả lỗi ngay, có retry không?

### 3.1. Confirm WALLET kiểm tra số dư

```text
POST confirm
 → kiểm tra chuyến còn Quoted và còn hạn
 → GET số dư bên Payment
 → đủ tiền và còn hạn: lưu confirm + FindingDriver + MatchRequested
```

| Sự cố | Kết quả | Trạng thái và cách xử lý |
| --- | --- | --- |
| Payment quá 3 giây chưa hoàn tất phản hồi | 504 `PAYMENT_TIMEOUT` | Không có HTTP retry tại chỗ; confirm chưa lưu, không thu tiền |
| Không kết nối được Payment | 502 `PAYMENT_UNAVAILABLE` | Khôi phục Payment rồi confirm lại nếu chuyến còn hạn |
| Ví chưa được tạo | `WALLET_NOT_READY` | Chờ luồng UserRegistered, kiểm tra queue/consumer; không coi là ví có số dư 0 |
| Payment trả dữ liệu không hợp lệ | `INVALID_PAYMENT_RESPONSE` | Kiểm tra contract/config/version; gửi lại không sửa được lỗi định dạng |
| Ví thiếu tiền | 409 `INSUFFICIENT_FUNDS` | Giữ Quoted; không phát MatchRequested; bổ sung tiền rồi confirm lại trước hạn |
| Trong lúc chờ ví, báo giá hết hạn | Không tiếp tục tìm tài xế | Backend kiểm tra lại hạn sau khi đọc số dư |

Chuyến vẫn Quoted nếu chưa bị worker hủy hoặc thao tác khác thay đổi. Không được suy ra mọi HTTP 500/timeout ở mọi API đều chưa ghi DB: kết luận này chỉ đúng với vị trí gọi kiểm tra ví trước khi lưu confirm.

Code: [PaymentClient](src/TripService/TripService.Infrastructure/Clients/PaymentClient.cs), [DI HTTP](src/TripService/TripService.Infrastructure/DependencyInjection.cs), [TripAppService](src/TripService/TripService.Application/Services/TripAppService.cs).

### 3.2. Đối soát Saga qua HTTP

PaymentClient vẫn dùng timeout 3 giây và không retry ngay trong một lời gọi. Tuy nhiên worker `PaymentSagaReconciler` chạy các vòng tiếp theo, nên lỗi của lượt này được thử lại ở lượt đối soát sau.

- Payment 404: chưa thấy payment; không suy ra đã thất bại.
- Payment PENDING: chưa có kết luận; tiếp tục giữ sự chưa chắc chắn.
- Payment SUCCEEDED/FAILED: handler kiểm tra và đồng bộ kết quả.
- Refund: cần payment REFUNDED **và** receipt đúng key, PaymentId, reason đã duyệt; không chỉ tin một trạng thái đơn lẻ.

Worker quét batch tối đa 50 payment và 50 refund, nghỉ 15 giây sau vòng xử lý. Vòng gồm nhiều HTTP request có thể lâu hơn 15 giây.

Code: [PaymentSagaReconciler](src/TripService/TripService.Infrastructure/Sagas/PaymentSagaReconciler.cs).

### 3.3. Gateway, Identity, token

| Sự cố | Người dùng thấy | Cần làm |
| --- | --- | --- |
| Gateway tắt | Không kết nối được cổng 5000 | Bật Gateway; worker backend có thể vẫn xử lý message |
| Service đích tắt | Request proxy lỗi, thường 5xx | Kiểm tra chính service đích và DB; đừng sửa token vì lỗi kết nối |
| Identity tắt | Không đăng ký/login mới được | JWT đã cấp có thể vẫn dùng ở service khác nếu chưa hết hạn và cấu hình xác thực đúng |
| Token hết hạn/thiếu/sai chữ ký | 401 | Login lại hoặc sửa cấu hình JWT |
| Token hợp lệ nhưng sai quyền | 403 | Dùng đúng vai trò; retry không cấp thêm quyền |
| Sai Services URL, port, hostname | Không kết nối được hoặc gọi nhầm instance | Đối chiếu launch settings, `.env`, địa chỉ container/host |
| Service lên nhưng migration/config lỗi | Process có thể thoát trước khi listen | Đọc log startup; bus đã kết nối không thay thế kiểm tra DB và schema |

HTTP response bị mất sau khi DB đã commit là một tình huống khác với request chưa tới server. Trước khi gửi lại thao tác ghi, tra cứu trạng thái hiện tại. API đăng ký có unique phone; các API khác cần xem quy tắc riêng, không giả định tất cả đều idempotent.

## 4. RabbitMQ: tắt service nhận khác với consumer bị lỗi

### 4.1. Service nhận tắt hoàn toàn

Nếu queue/binding đã tồn tại và broker hoạt động, message đã được route vào queue sẽ chờ consumer. Không có consumer chạy nghĩa là chưa có exception consumer để đếm retry.

Ví dụ Payment tắt sau dropoff:

```text
Trip đã commit PaymentPending + Saga + ProcessPayment trong Outbox
 → publisher gửi tới queue payment.process-payment
 → queue chờ Payment
 → Payment bật lại, kết nối consumer rồi xử lý
```

Chờ 5 phút không làm message tự biến thành PaymentFailed. Saga có thể đã chuyển sang trạng thái chưa chắc chắn trong thời gian chờ.

**Điều kiện topology rất quan trọng:** khi consumer chưa từng khởi động, queue/binding có thể chưa được tạo. Publish được broker xác nhận không chứng minh đã có queue đích nhận tin. Không giả định broker sẽ lưu hộ mọi event đến lúc một subscriber mới xuất hiện; cần triển khai topology/consumer trước khi phát nghiệp vụ.

### 4.2. Consumer đang chạy nhưng throw exception

Trong [MessagingExtensions](src/BuildingBlocks/SwiftRide.Messaging/MessagingExtensions.cs):

```csharp
endpoint.UseMessageRetry(retry =>
{
    retry.Ignore<JsonException>();
    retry.Ignore<ArgumentException>();
    retry.Ignore<FormatException>();
    retry.Exponential(options.RetryLimit,
        TimeSpan.FromMilliseconds(options.MinRetryIntervalMs),
        TimeSpan.FromMilliseconds(options.MaxRetryIntervalMs),
        TimeSpan.FromMilliseconds(options.RetryIntervalDeltaMs));
});
```

Mặc định [RabbitMqOptions](src/BuildingBlocks/SwiftRide.Messaging/RabbitMqOptions.cs): RetryLimit=3, min=1000ms, max=10000ms, delta=2000ms, PrefetchCount=16. Đây là tối đa 3 lần **thử lại sau lần đầu**, không phải mọi message đều thử tổng cộng 3 lần. Không coi các tham số exponential là lịch cố định 1s→2s→4s.

- Exception thuộc nhóm Ignore bỏ qua các lần retry này, đi xử lý lỗi.
- Exception khác thoát khỏi consumer được retry; hết lượt thì chuyển `<queue>_error` theo pipeline MassTransit.
- Consumer bắt exception rồi trả thành công: middleware không còn exception để retry.
- Kết quả nghiệp vụ như thiếu tiền được xử lý thành PaymentFailed không nhất thiết là lỗi consumer.
- Sau khi vào `_error`, bật lại service không tự kéo message trở lại queue chính.

Đây là retry trong pipeline xử lý, chưa cấu hình delayed redelivery dài hạn trong phần DI chung. Retry chờ có thể giữ tài nguyên xử lý của endpoint.

### 4.3. ACK và crash

Consumer không tự viết lệnh ACK trong code ứng dụng. MassTransit điều khiển ACK/NACK theo kết quả pipeline.

| Điểm crash | Hệ quả cần dự phòng |
| --- | --- |
| Consumer chết trước commit DB | Transaction chưa commit bị rollback; message chưa ACK có thể được giao lại |
| DB đã commit nhưng chưa ACK | Broker có thể giao lại cùng message; handler phải chống xử lý trùng |
| Consumer xử lý thành công và ACK | Message đã rời queue; lỗi ở service kế tiếp là một bước khác |

Không có transaction chung giữa tất cả DB và RabbitMQ. Mục tiêu là giao ít nhất một lần với xử lý lặp an toàn, không phải bảo đảm mỗi message chỉ xuất hiện một lần.

## 5. Outbox: khi broker hoặc publisher bị lỗi

| Điểm lỗi | Dữ liệu còn lại | Phục hồi |
| --- | --- | --- |
| Lưu nghiệp vụ/Outbox lỗi trước commit | Không có thay đổi đã commit của transaction đó | Sửa DB; client xử lý response lỗi rồi thử lại theo quy tắc API |
| Commit xong, process chết trước publish | Row Outbox còn chưa gửi | Worker chạy lại đọc DB và gửi |
| RabbitMQ tắt/mất mạng | Outbox chưa đánh dấu gửi thành công | Publisher thử lại khi hoạt động; không rollback chuyến đã commit |
| Broker nhận tin nhưng response confirm bị mất | Publisher không biết chắc đã nhận | Có thể gửi lại; giữ EventId để consumer deduplicate |
| Publish thành công, chết trước đánh dấu Outbox | Tin đã ở broker, Outbox vẫn pending | Gửi trùng có thể xảy ra |
| Payload Outbox hỏng/loại event không hỗ trợ | Worker tiếp tục gặp lỗi ở row đó | Sửa payload/phiên bản có kiểm soát; đây không tự động là queue `_error` |

Nhịp nghỉ hiện tại của worker (không phải SLA):

| Publisher | Nghỉ giữa vòng | Ghi chú |
| --- | --- | --- |
| Identity | 10 giây | UserRegistered/DriverRegistered |
| Trip | 5 giây | Batch 50, claim lease 120 giây; crash có thể phải chờ hết lease |
| Payment | 5 giây | Publish rồi đánh dấu trong transaction DB; lỗi DB sau publish có thể gây gửi trùng |
| Matching Quote/Match | 1 giây | Mongo document chứa kết quả và dữ liệu outbox, claim lease 2 phút |

Thời gian xử lý, kết nối broker, backlog và lease làm thời gian thực tế dài hơn. Không có nghĩa cứ đúng 5 giây thì mọi sự kiện chắc chắn được gửi.

Code: [Identity publisher](src/IdentityService/IdentityService.Infrastructure/Outbox/IdentityOutboxPublisher.cs), [Trip publisher](src/TripService/TripService.Infrastructure/Outbox/TripOutboxPublisher.cs), [Payment publisher](src/PaymentService/PaymentService.Infrastructure/Messaging/PaymentOutboxPublisher.cs), [Quote publisher](src/MatchingService/MatchingService.Infrastructure/Messaging/QuoteOutboxPublisher.cs), [Match publisher](src/MatchingService/MatchingService.Infrastructure/Messaging/MatchOutboxPublisher.cs).

## 6. Từng giai đoạn nghiệp vụ khi một service bị tắt

### 6.1. Đăng ký và tạo ví/hồ sơ

- Identity DB lỗi: không thể hoàn tất đăng ký.
- Identity commit User+Outbox nhưng broker lỗi: user đã tồn tại, tạo ví/hồ sơ sẽ trễ.
- Payment tắt: user có thể login nhưng `/wallets/me` chưa có ví, không được tự tạo ví giả từ phía client.
- Matching tắt: DriverRegistered chưa áp dụng; hồ sơ Driver có thể chưa tồn tại.
- Tin đăng ký lặp: tạo ví không reset số dư; upsert Driver không được reset Busy, vị trí hoặc chuyến đang gán.
- Message vào `_error`: sửa lỗi và replay đúng tin; không chỉ đăng ký lại một số điện thoại khác để che mất lỗi.

### 6.2. Báo giá

- `POST /trips` trả 202 sau khi lưu Requested+Outbox; price/fare chưa có là trạng thái hợp lệ.
- Matching tắt hoặc request chưa publish: Trip có thể ở Requested.
- Matching tính xong nhưng kết quả chưa gửi/Trip tắt: kết quả nằm ở Outbox/queue; Trip chưa chuyển Quoted.
- QuoteFailed hợp lệ: Trip chuyển Failed theo handler; retry kỹ thuật không biến kết quả nghiệp vụ này thành báo giá thành công.
- Rider đã hủy trước khi kết quả đến: handler không được đưa chuyến Cancelled sống lại.
- Worker hết hạn Quoted chỉ xử lý Quoted; không tự áp hạn đó cho Requested.

Code: [QuoteResultConsumers](src/TripService/TripService.Infrastructure/Messaging/QuoteResultConsumers.cs).

### 6.3. Confirm và tìm tài xế

- Confirm WALLET còn phụ thuộc HTTP kiểm tra số dư như mục 3.
- Confirm đã commit FindingDriver+MatchRequested rồi Matching tắt: chuyến chờ xử lý, không tự hoàn tiền vì chưa có charge ở bước này.
- MatchFailed vì không có tài xế: Trip chuyển Failed; khác với Matching không phản hồi vì process đã tắt.
- DriverMatched về muộn sau hủy: kiểm tra state/attempt/causation trước khi áp dụng.
- Requested, Quoted, FindingDriver cho Rider hủy qua `/trips/{id}/cancel` theo API hiện tại.
- Chưa thấy scheduler tổng quát tự hủy mọi Requested/FindingDriver quá hạn; không dùng timeout Quoted để giải thích hai trạng thái này.

Matching chọn tài xế Available gần pickup nhất. Postman có Driver mới nhưng offer nằm ở Driver cũ là vấn đề lựa chọn dữ liệu, không phải bằng chứng mất message. Nhiều Driver tại cùng tọa độ không bảo đảm chọn tài khoản đang login.

Code: [MatchResultConsumers](src/TripService/TripService.Infrastructure/Messaging/MatchResultConsumers.cs), [MatchAppService](src/MatchingService/MatchingService.Application/Services/MatchAppService.cs).

### 6.4. Driver accept và đồng bộ Busy

Trip lưu việc nhận chuyến rồi phát TripAccepted. Matching tắt làm trạng thái Busy ở Matching cập nhật chậm; Trip vẫn có trạng thái đã nhận. Luồng accept có kiểm tra chống tài xế nhận hai chuyến; không được dựa duy nhất vào một bản sao Available đang trễ bên Matching.

TripAccepted và TripClosed đi qua hai queue khác nhau. Không giả định thứ tự đến giống thứ tự phát. Matching phải dùng thông tin chuyến/lifecycle để không áp event cũ lên chuyến mới.

### 6.5. Dropoff và thanh toán

| Tình huống | Trip / Saga | Payment |
| --- | --- | --- |
| Payment đang tắt | PaymentPending, Saga AwaitingPayment rồi có thể PaymentUncertain | Chưa xử lý ProcessPayment |
| Consumer đang xử lý nhưng DB Payment lỗi | Chờ kết quả | Rollback transaction; retry rồi có thể `_error` |
| Ví không đủ tiền lúc thực thu | Khi nhận kết quả: PaymentFailed / Saga Failed | Kết quả nghiệp vụ thất bại; không phải lỗi cần thu lại vô hạn |
| Thu thành công, kết quả Outbox chưa gửi | Trip vẫn có thể PaymentPending | Payment đã SUCCEEDED |
| Trip tắt khi kết quả về | Chưa áp kết quả | Kết quả đã commit; chờ queue/đối soát |
| Event kết quả sai chuyến/key/correlation | Không được áp mù vào Trip | Cần xem log validation, `_error`, handler |

Precheck số dư lúc confirm không giữ tiền; số dư có thể thay đổi trước dropoff. Checkout vẫn phải kiểm tra và khóa tài khoản lại.

ProcessPaymentConsumer kiểm tra hình dạng DeadlineAt nhưng không từ chối chỉ vì thời gian hiện tại đã vượt deadline. Timeout Saga không hủy message thu tiền đang nằm trong queue; lệnh đến muộn vẫn có thể được xử lý.

Code: [ProcessPaymentConsumer](src/PaymentService/PaymentService.Infrastructure/Messaging/ProcessPaymentConsumer.cs).

### 6.6. Saga timeout: tại sao không coi là thất bại?

Deadline payment mặc định trong options là 120 giây. Scheduler tìm Saga AwaitingPayment quá hạn, lưu marker và ProcessPaymentTimedOut vào Outbox cùng transaction. Trip tự publish rồi consume timeout để chuyển Saga sang PaymentUncertain nếu vẫn phù hợp.

- Trip tắt: scheduler không chạy; bật lại mới quét deadline đã qua.
- RabbitMQ tắt: marker/timeout có thể đã lưu, nhưng consumer chưa nhận được; đổi state sẽ trễ.
- PaymentSettled về trước timeout: timeout cũ không được ghi đè kết quả cuối.
- Payment trả 404/PENDING khi đối soát: vẫn chưa chắc chắn, không tự ghi PaymentFailed.
- Không có kết quả xác định lâu dài: có thể tiếp tục chờ; worker đối soát hiện không tự dựng một payment hoặc tự phát lại lệnh thu chỉ vì GET 404.

**Đừng hủy hoặc refund chỉ vì không nhận được phản hồi:** tiền có thể đã thu nhưng kết quả đang trên đường về.

Code: [scheduler](src/TripService/TripService.Infrastructure/Sagas/PaymentSagaTimeoutScheduler.cs), [timeout consumer](src/TripService/TripService.Infrastructure/Messaging/ProcessPaymentTimedOutConsumer.cs), [bảng chuyển trạng thái](src/TripService/TripService.Application/Sagas/PaymentSagaTransitions.cs).

### 6.7. Đóng chuyến và giải phóng Driver

Trip lưu kết quả và TripClosed nguyên tử trong DB của Trip. Matching tắt khiến Driver có thể còn Busy dù Trip đã Paid/PaymentFailed. Bật Matching và kiểm tra `matching.trip-closed`, Outbox Trip và `_error`.

TripClosed chỉ được giải phóng đúng chuyến đang gán. Event của chuyến cũ không được giải phóng Driver đang thực hiện chuyến mới. Không ép Available toàn bộ Driver để sửa một tin bị kẹt.

### 6.8. Refund

- Admin duyệt hợp lệ: Trip lưu RefundPending/Saga Compensating và RefundPayment.
- Payment tắt: lệnh hoàn chờ queue; không khẳng định đã hoàn tiền.
- Payment refund lỗi DB: rollback các thay đổi DB trong transaction, exception đi theo retry.
- Hoàn đã commit nhưng event chưa đến Trip: Payment REFUNDED trong khi Trip vẫn RefundPending là khoảng trễ có thể xảy ra.
- Refund timeout: scheduler đặt RefundTimedOutAt; không lập tức biến thành kết quả hoàn thất bại. Reconciler tra payment và receipt.
- Kết quả hoàn bị từ chối hợp lệ: Saga NeedsAttention, Trip RefundNeedsAttention; cần xử lý theo reason, không retry cùng thao tác vô hạn.
- Refund dùng ledger gốc của payment; không tính lại tỷ lệ chia hiện tại và không tự refund vì Matching tạm mất kết nối.

Code: [RefundPaymentConsumer](src/PaymentService/PaymentService.Infrastructure/Messaging/RefundPaymentConsumer.cs), [TripRefundCoordinator](src/TripService/TripService.Application/Sagas/TripRefundCoordinator.cs), [TripRefundResultHandler](src/TripService/TripService.Application/Sagas/TripRefundResultHandler.cs).

## 7. DB, broker và tải cao

| Sự cố | Điều cần hiểu |
| --- | --- |
| SQL Server/PostgreSQL/MongoDB tắt | Service có thể lỗi startup hoặc lỗi lúc request/consumer; Outbox cũng không đọc được khi DB của nó tắt |
| Migration không chạy được | Phục hồi DB/config trước; restart vòng lặp không sửa schema sai |
| RabbitMQ restart, dữ liệu broker còn | Queue durable giúp tồn tại qua restart; vẫn cần persistence và cấu hình broker phù hợp |
| Xóa volume DB/broker | Không được xem như restart thông thường; có thể mất Outbox, state hoặc message |
| Queue tăng nhanh | Consumer xử lý chậm hơn tốc độ gửi; polling client nhiều hơn không làm consumer nhanh hơn |
| Nhiều instance cùng consume một queue | Chia tải giữa consumer; không phải mỗi instance nhận một bản riêng |
| Tạo queue tên khác | Không tự là queue dự phòng; binding/subscription khác có thể làm nhân đôi xử lý |
| Duplicate hoặc sai thứ tự | Dùng EventId, business key, correlation, attempt và state checks; không dựa vào FIFO toàn hệ thống |

Code hiện đặt Durable=true và PrefetchCount mặc định 16; không thấy chủ động đặt queue type quorum hay chính sách HA trong DI chung. Không tuyên bố broker hiện đã có cụm dự phòng. Prefetch là lượng tin được lấy trước, không phải bảo đảm 16 thao tác nghiệp vụ chạy đồng thời hay đúng thứ tự.

Không có giao dịch phân tán bảo vệ một cổng thanh toán bên ngoài cùng DB nội bộ. Nếu sau này thay simulator bằng gateway thật, cần idempotency/tra cứu ở provider và xử lý kết quả không xác định; rollback DB không tự đảo một charge đã xảy ra bên ngoài.

## 8. Timeout nào đang được cấu hình?

| Tham số | Giá trị đọc được | Phạm vi |
| --- | --- | --- |
| PaymentClient RequestTimeout | 3 giây | Một HTTP call từ Trip sang Payment |
| QuoteConfirmationTimeoutSeconds | appsettings hiện tại 30; class default 300 | Chuyến đã Quoted, chưa confirm |
| QuoteExpiryPollSeconds | 5 giây | Nhịp quét hết hạn Quoted |
| QuoteExpiryBatchSize | 50 | Số row mỗi batch hết hạn |
| PaymentSagaDeadlineSeconds | class default 120 giây | Chờ kết quả thu tiền |
| RefundSagaDeadlineSeconds | class default 120 giây | Chờ kết quả hoàn tiền |
| Reconciler delay | 15 giây sau vòng | Đối soát kết quả chưa chắc chắn |

Quoted hết hạn: worker hủy và cập nhật history; không thu/hoàn tiền. Confirm thiếu tiền không kéo dài QuotedAt. Payment/Refund deadline: đánh dấu sự chưa chắc chắn để đối soát, không phải quy tắc tự Cancelled.

Code: [Trip options](src/TripService/TripService.Application/Options/TripWorkflowOptions.cs), [appsettings](src/TripService/TripService.Api/appsettings.json), [QuoteExpiryWorker](src/TripService/TripService.Infrastructure/Sagas/QuoteExpiryWorker.cs).

Môi trường `Testing` dùng MassTransit InMemory và một số hosted worker không chạy. Kết quả ở môi trường đó không chứng minh đã kiểm tra đường RabbitMQ thật.

## 9. Checklist chẩn đoán một chuyến bị kẹt

1. Ghi TripId, UserId/DriverId, HTTP status, error code và thời điểm.
2. Xác định đang lỗi HTTP hay đang chờ event. HTTP PAYMENT_TIMEOUT ưu tiên kiểm tra PaymentClient/Payment/DB, không bắt đầu bằng queue `_error`.
3. GET Trip và Payment theo cùng TripId. Đừng so payment của chuyến cũ với chuyến mới.
4. Kiểm tra đúng process/port và phiên bản đang chạy. Build thành công chưa chắc process cũ đã được thay.
5. Kiểm tra Outbox phía phát: pending hay processed, claim còn hạn không, AttemptCount và log publish.
6. Kiểm tra broker đúng host/vhost, queue/binding, số consumer, Ready và Unacked.
7. Nếu queue chính trống, kiểm tra `_error`, `_skipped`, Outbox và DB đích; queue trống không tự chứng minh nghiệp vụ đã hoàn thành.
8. Đối chiếu EventId/MessageId, CorrelationId (luồng chuyến thường là TripId), CausationId và RetryAttempt trong log.
9. Kiểm tra Saga state/deadline/timeout marker và log reconciliation.
10. Sửa nguyên nhân rồi mới replay message. Giữ nguyên định danh nghiệp vụ/event; xác minh dữ liệu đã commit trước khi replay.

### Queue cần xem theo từng luồng

| Luồng | Queue |
| --- | --- |
| Tạo ví | payment.user-registered |
| Tạo Driver | matching.driver-registered |
| Báo giá | matching.quote-requested; trip.quote-calculated; trip.quote-failed |
| Tìm tài xế | matching.match-requested; trip.driver-matched; trip.match-failed |
| Busy/giải phóng | matching.trip-accepted; matching.trip-closed |
| Thu tiền | payment.process-payment; trip.payment-settled; trip.payment-failed |
| Timeout thu tiền | trip.process-payment-timed-out |
| Hoàn tiền | payment.refund-payment; trip.payment-refunded; trip.payment-refund-failed |

Với mỗi queue, xem thêm hậu tố `_error` hoặc `_skipped` nếu tồn tại. `_skipped` thường là tin không có consumer phù hợp trên endpoint, khác với lỗi nghiệp vụ. Tên chuẩn ở [QueueNames](src/BuildingBlocks/SwiftRide.Contracts/Events/QueueNames.cs).

Khi đọc message trên UI RabbitMQ để chẩn đoán, chọn chế độ requeue nếu chưa định lấy bỏ. Không purge queue hoặc xóa DB để chữa một giao dịch đang kẹt. Message ở `_error` cần được replay có chủ đích sau khi sửa lỗi, không chỉ restart rồi chờ.

## 10. Các tình huống kiểm tra thủ công

| Thao tác | Kỳ vọng cần quan sát |
| --- | --- |
| Tắt Payment rồi confirm WALLET | Lỗi HTTP kết nối/timeout; chưa bắt đầu tìm tài xế |
| Confirm xong, tắt Payment rồi dropoff | Trip lưu PaymentPending; lệnh thu chờ; timeout không tự PaymentFailed |
| Bật Payment trở lại | Consumer xử lý backlog; kết quả về Trip hoặc được đối soát |
| Tắt Matching trước tạo chuyến | Requested chờ báo giá; chưa có price |
| Tắt Matching sau Quoted, trước confirm | Nếu precheck ví thành công, FindingDriver chờ MatchRequested |
| Tắt Matching sau accept/đóng chuyến | Busy/Available cập nhật trễ; TripClosed phải giải phóng đúng chuyến khi khôi phục |
| Tắt RabbitMQ sau transaction nghiệp vụ | Outbox còn pending; không được báo đã hoàn tất mọi bước phía sau |
| Tắt Trip khi Payment xử lý | Payment có thể hoàn tất; Trip đồng bộ khi chạy lại |
| Gây lỗi DB consumer đủ lâu | Retry rồi `_error` nếu exception thuộc nhóm được retry |
| Phát lại cùng event hợp lệ | Không reset ví, không thu/hoàn hai lần, không nhân đôi history |
| Đưa event cũ sau event của chuyến mới | Không giải phóng sai Driver hoặc ghi đè trạng thái cuối |

Đây là kế hoạch quan sát, không phải báo cáo các ca đã được chạy. Khi diễn tập lỗi, dùng dữ liệu kiểm thử và xem kết quả thực tế ở cả DB, broker và API.

## 11. Những giới hạn hiện tại cần nhớ

- HTTP Payment chưa có retry ngắn/circuit breaker trong DI được kiểm tra.
- Outbox không thay thế backup; dữ liệu DB mất thì event chưa gửi có thể mất theo.
- Publisher confirm chỉ xác nhận broker, không xác nhận consumer đã commit nghiệp vụ.
- Retry consumer hữu hạn; `_error` cần can thiệp, không tự hồi phục chỉ nhờ restart.
- Chưa thấy timeout tự hủy tổng quát cho mọi trạng thái Requested/FindingDriver/DriverOffered.
- Saga không tự biến 404/PENDING thành thất bại chắc chắn và không hoàn tiền vì lỗi kết nối tạm thời.
- Health endpoint Healthy không đủ chứng minh toàn bộ luồng bất đồng bộ đã thông: cần quan sát bus, Outbox, queue và consumer.
- Chưa có bảo đảm mọi service đồng bộ tức thời; trạng thái trung gian là một phần của thiết kế.

Nếu bổ sung sau này: ưu tiên retry ngắn có tổng ngân sách thời gian cho HTTP GET, cảnh báo Outbox/queue tồn lâu, quy trình replay `_error`, deadline rõ cho quote/match và triển khai topology trước traffic. Đây là đề xuất, chưa phải tính năng đã có.
