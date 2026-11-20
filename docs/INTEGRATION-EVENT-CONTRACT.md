# Integration event contract (v1)

The original seven events implement `IIntegrationEvent` in `SwiftRide.Contracts.Events`. The publisher assigns a nonempty `EventId` once and retains it on republish. `CorrelationId` is the business flow ID (`UserId` for registration, `TripId` for trip and payment). `CausationId` is the parent event's `EventId`, or `null` for an HTTP-originated action. `OccurredAt` is UTC at the time the business event occurred. `SchemaVersion` is `1`; incompatible payload changes require a new version. Consumers deduplicate using `EventId`.

## Asynchronous quote contract

| Message | Publisher | Consumer queue | Payload beyond metadata |
| --- | --- | --- | --- |
| `QuoteRequested` | Trip SQL Outbox | `matching.quote-requested` | TripId; PickupLat/PickupLng/DropoffLat/DropoffLng: double |
| `QuoteCalculated` | Matching embedded Mongo Outbox | `trip.quote-calculated` | TripId; QuoteId; Price: decimal (2 places); DistanceKm/DurationMin: double; Strategy |
| `QuoteFailed` | Matching embedded Mongo Outbox | `trip.quote-failed` | TripId; Reason: NO_DRIVER_AVAILABLE or VALIDATION_ERROR |

CorrelationId is TripId; result CausationId is the original QuoteRequested.EventId.
Matching stores one immutable result per request EventId, rejects changed payloads,
and retains result EventId on republish. Trip verifies the original request and
applies fare/state/history atomically; valid duplicates do not reset accepted fares.
Infrastructure errors retry / enter `_error`, not QuoteFailed.
See [quote workflow](QUOTE-BAT-DONG-BO.md) for 202/polling and cancellation behavior.

## Original events

| Event | Publisher | Consumer queue | Payload beyond metadata |
| --- | --- | --- | --- |
| `UserRegistered` | Identity | `payment.user-registered` | `UserId: Guid`, `Role: string` |
| `DriverRegistered` | Identity | `matching.driver-registered` | `DriverId: Guid`, `Name: string` |
| `TripAccepted` | Trip | `matching.trip-accepted` | `TripId: Guid`, `DriverId: Guid` |
| `RideCompleted` | Trip | `payment.ride-completed` | `TripId`, `RiderId`, `DriverId: Guid`; `Amount: decimal`; `PaymentMethod: PaymentMethod`; `CardToken: string?` |
| `PaymentSettled` | Payment | `trip.payment-settled` | `TripId`, `PaymentId: Guid`; `Amount: decimal` |
| `PaymentFailed` | Payment | `trip.payment-failed` | `TripId: Guid`; `PaymentId: Guid?`; `Reason: string` |
| `TripClosed` | Trip | `matching.trip-closed` | `TripId`, `DriverId: Guid`; `FinalStatus: string` |

Queue names are centralized in `QueueNames`. They are receive endpoint names, not event exchange names. Registration events retain a two-argument constructor for existing HTTP/JSON callers; it generates metadata. Other events require explicit metadata in their constructor. Publishers should preserve the original `EventId` when retrying or replaying an event.

## Messaging convention

The four APIs load the repository `.env` before building configuration. `RabbitMQ__*` keys bind to `RabbitMqOptions`; real environment variables take precedence. MassTransit uses durable RabbitMQ receive endpoints with publisher confirmations. Registered consumer endpoints get bounded exponential retry; `JsonException`, `ArgumentException`, and `FormatException` skip retry. After an unhandled consumer failure, MassTransit moves the message to `<queue>_error`. An unhandled message delivered to a queue goes to `<queue>_skipped`. Inspect the error queue and exception headers, fix the cause, then replay with the original `EventId`.

The shared registration provides `configureConsumers` and `configureEndpoints` hooks. Each service must register its actual consumers and bind them to the queue constants when the event workflow is implemented. Merely adding the messaging foundation does not create the seven queues or publish events. Publisher confirmation acknowledges broker acceptance; database and publish atomicity still requires an outbox.

## Payment Saga contract (chưa nối publisher/consumer)

Các record Saga đã được khai báo trong `PaymentSagaEvents.cs`. Queue là tên endpoint dự kiến; chưa có consumer nên các queue mới chưa được tạo ở runtime. Saga dùng các event hiện có `RideCompleted`, `PaymentSettled`, `PaymentFailed` cùng các message mới dưới đây.

| Message | Hướng | Queue | Payload thêm ngoài metadata |
| --- | --- | --- | --- |
| `ProcessPayment` | Trip Saga → Payment | `payment.process-payment` | `TripId`, `RiderId`, `DriverId`, `Amount`, `PaymentMethod`, `IdempotencyKey`, `DeadlineAt`, `CardToken?` |
| `ProcessPaymentTimedOut` | Saga timer → Trip Saga | `trip.process-payment-timed-out` | `TripId`, `ProcessPaymentEventId`, `DeadlineAt` |
| `RefundPayment` | Trip Saga → Payment | `payment.refund-payment` | `TripId`, `PaymentId`, `Amount`, `ReasonCode`, `IdempotencyKey` |
| `PaymentRefunded` | Payment → Trip Saga | `trip.payment-refunded` | `TripId`, `PaymentId`, `Amount`, `ReasonCode` |
| `PaymentRefundFailed` | Payment → Trip Saga | `trip.payment-refund-failed` | `TripId`, `PaymentId`, `FailureCode` |

Mọi message dùng `CorrelationId = TripId`, `SchemaVersion = 1`, `OccurredAt` UTC. Khi phát phản hồi, `CausationId` trỏ đến `EventId` của command gây ra nó; timeout trỏ tới `ProcessPayment.EventId`. Retry cùng một command giữ nguyên `EventId`, payload, `DeadlineAt` và key. `ProcessPayment.IdempotencyKey = PaymentProtocol.TripPaymentKey(TripId)` để trùng với khóa checkout hiện có; `RefundPayment.IdempotencyKey = PaymentProtocol.TripRefundKey(TripId)`. Không tạo key mới theo lần retry.

`ProcessPaymentTimedOut` chỉ báo đã qua hạn chờ; Saga phải tra Payment theo `TripId` trước khi quyết định, không tự coi là `PaymentFailed` và không tự refund. `PaymentRefundFailed` chỉ dành cho từ chối nghiệp vụ đã xác định và **không có refund được commit**; lỗi hạ tầng thì retry/đối soát, không phát sự kiện từ chối sai. `ReasonCode` chỉ nhận `INVALID_TRIP_CHARGE` hoặc `APPROVED_CUSTOMER_REFUND`. `FailureCode` dùng các mã trong `SagaRefundFailureCodes`; không đưa CardToken hoặc lời giải thích tự do vào kết quả refund.

**Quy tắc chuyển luồng:** `ProcessPayment` dùng cùng key với `RideCompleted` hiện tại. Khi triển khai Saga, mỗi Trip chỉ có **một đường chủ động tạo kết quả thanh toán**. Không để cả `RideCompletedConsumer` và `ProcessPaymentConsumer` đồng thời phát `PaymentSettled`/`PaymentFailed` cho cùng Trip; idempotency của payment ngăn charge lặp nhưng không tự ngăn hai event kết quả khác `EventId`. Yêu cầu refund của Trip cũng phải đi qua một đường duy nhất; không gọi endpoint refund trực tiếp song song với `RefundPayment` cho cùng quyết định.
