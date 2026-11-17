# Integration event contract (v1)

All seven events implement `IIntegrationEvent` in `SwiftRide.Contracts.Events`. The publisher assigns a nonempty `EventId` once and retains it on republish. `CorrelationId` is the business flow ID (`UserId` for registration, `TripId` for trip and payment). `CausationId` is the parent event's `EventId`, or `null` for an HTTP-originated action. `OccurredAt` is UTC at the time the business event occurred. `SchemaVersion` is `1`; incompatible payload changes require a new version. Consumers deduplicate using `EventId`.

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
