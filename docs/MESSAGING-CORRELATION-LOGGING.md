# Correlation and messaging logs

## Workflow identifiers

- `UserRegistered` and `DriverRegistered`: `CorrelationId = UserId` (the driver ID is the same user ID).
- `TripAccepted`, `RideCompleted`, `PaymentSettled`, `PaymentFailed`, and `TripClosed`: `CorrelationId = TripId`.
- `EventId` identifies one integration event. Outbox redelivery retains the same `EventId`.
- `CausationId` points to the event that caused the next event: payment results point to `RideCompleted.EventId`; `TripClosed` points to the payment result's `EventId`.
- The publish filter writes `EventId` to the MassTransit `MessageId` and the workflow ID to the transport `CorrelationId`. The payload and its outbox row remain the durable source of these IDs.

`X-Correlation-Id` is an HTTP request tracing ID. The HTTP middleware validates or generates it, returns it in the response, and adds it to the request log scope. It does not replace the workflow `CorrelationId` in integration events.

## Structured log fields

The shared publish and consume filters record `EventId`, `MessageId`, `CorrelationId`, `CausationId`, `TripId` or `UserId`, `Queue`, `EventType`, and `Outcome`. Consume logs also record `RetryAttempt` (zero for the first attempt). A failed attempt is logged and rethrown so the configured retry and `_error` behavior still applies. A publish success log means the MassTransit publish task completed; the outbox publisher then marks its row processed.

Use `CorrelationId` to find a workflow across services, `CausationId` to follow one event to its successor, and `MessageId`/`EventId` to inspect redelivery of a single message.
