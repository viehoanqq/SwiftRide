# Kiến trúc Tổng thể, Clean Architecture, Máy trạng thái Chuyến đi & Thiết kế 4 Cơ sở dữ liệu

Tài liệu này tổng hợp kiến trúc kỹ thuật xuyên suốt toàn hệ thống **SwiftRide** theo mã nguồn hiện hành trên nền tảng **.NET 10**.

---

## 1. Kiến trúc Container & Giao tiếp Lai (Hybrid Communication)

Hệ thống được phân rã thành **01 API Gateway (YARP)**, **04 Microservices độc lập**, **02 thư viện nền tảng `BuildingBlocks`**, **04 cơ sở dữ liệu độc lập** (Polyglot Persistence) và **01 Message Broker RabbitMQ**:

| Thành phần | Cổng Host | CSDL sở hữu | Trách nhiệm cốt lõi |
|---|---|---|---|
| **API Gateway** (`src/ApiGateway`) | `:5000` | — | Định tuyến tập trung bằng YARP, tổng hợp `GET /health` song song (timeout `5s`), chặn biên `403 INTERNAL_ENDPOINT_ONLY` với `POST /payments` và `POST /payments/{id}/refund`. |
| **Identity Service** (`src/IdentityService`) | `:5004` | SQL Server `UserDb` (`:1435`) | Đăng ký/Đăng nhập (BCrypt + JWT `HMAC-SHA256`), `AdminBootstrapper`, phát Outbox `UserRegistered` & `DriverRegistered` (kèm backfill tài khoản seed). |
| **Trip Service** (`src/TripService`) | `:5001` | SQL Server `TripDb` (`:1434`) | Quản lý Aggregate `Trip` với **15 trạng thái** (State Pattern), điều phối mời tài xế tối đa `k = 3` lượt, vận hành **Orchestrated Payment & Refund Saga**. |
| **Matching & Pricing Service** (`src/MatchingService`) | `:5002` | MongoDB `MatchDb` (`:27018`) | Báo giá theo **Strategy Pattern** (`Standard`, `Surge`, `Promo`), tìm tài xế gần nhất qua chỉ mục cầu `2dsphere` (`$nearSphere`), chống lệch thứ tự sự kiện bằng `StatusTripId` & `ClosedTripIds`. |
| **Payment Service** (`src/PaymentService`) | `:5003` | PostgreSQL `PayDb` (`:5433`) | Chọn bộ quyết toán qua **Factory Pattern** (`WALLET` / `CARD`), hạch toán **Sổ cái kép Double-Entry** (tỷ lệ `80/20`), cổng thẻ giả lập `FakeCardAuthorizationGateway` và hoàn tiền đảo bút toán gốc. |

### Nguyên tắc giao tiếp giữa các dịch vụ
- **REST đồng bộ (timeout ngắn 3s):** Dùng cho các thao tác cần phản hồi tức thời trong luồng đặt xe (`Trip -> Matching` qua `POST /quote`, `POST /match`) và đối soát chủ động khi quá hạn Saga (`Trip -> Payment` qua `GET /payments/by-trip/{tripId}`, `GET /payments/refunds/by-key/{key}`).
- **RabbitMQ bất đồng bộ + Transactional Outbox:** Dùng cho toàn bộ luồng mở ví/hồ sơ tài xế, đồng bộ trạng thái `Busy`/`Available` của tài xế, yêu cầu thu tiền (`ProcessPayment`) và bồi thường hoàn tiền (`RefundPayment`).

---

## 2. Clean Architecture 4 Tầng & Vai trò của `BuildingBlocks`

Mỗi microservice được chia thành 4 project độc lập tuân theo quy tắc phụ thuộc hướng tâm:

1. **`*.Domain`**: Chứa Entity, Aggregate Root, Enum, State Hierarchy (`TripState`), Domain Rules/Services (`PricingCalculator`, `MoneyRules`, `PaymentSettlement`). **Không phụ thuộc bất kỳ gói NuGet hạ tầng nào và không tham chiếu `BuildingBlocks`.**
2. **`*.Application`**: Chứa Use Cases (`*AppService`), Saga (`PaymentSagaInstance`, `PaymentSagaTransitions`, `TripRefundCoordinator`), DTOs và các cổng giao tiếp trừu tượng (`IRepository`, `IUnitOfWork`, `IMatchingClient`, `IPaymentClient`). Chỉ tham chiếu `*.Domain` và `SwiftRide.Contracts`.
3. **`*.Infrastructure`**: Hiện thực EF Core (`SqlServer`, `Npgsql`), `MongoDB.Driver`, Outbox Publishers, MassTransit Consumers và HTTP Typed Clients. Tham chiếu `*.Application` và `SwiftRide.Messaging`.
4. **`*.Api`**: Chứa Controllers, cấu hình JWT/Scope Policies, `ExceptionMiddleware` chuẩn hóa lỗi `{ code, message }` và `Program.cs`.

### Tại sao tách đôi `src/BuildingBlocks/`?
- **`SwiftRide.Contracts`**: Thư viện C# thuần **0 phụ thuộc NuGet**, định nghĩa `IIntegrationEvent`, 12 bản ghi sự kiện/lệnh Saga, `QueueNames` và `PaymentProtocol` — an toàn để tầng `Application` sử dụng mà không bị kéo theo thư viện broker.
- **`SwiftRide.Messaging`**: Đóng gói cấu hình MassTransit 8 / RabbitMQ, Exponential Retry 3 lần, cô lập hàng đợi `*_error`, `DotEnvLoader` và bộ lọc ghi log truy vết (`CorrelationLoggingMiddleware`, `IntegrationEventPublishFilter`, `IntegrationEventConsumeFilter`) — chỉ gắn ở tầng `Infrastructure`/`Api`.

---

## 3. Máy trạng thái Chuyến đi (15 Trạng thái `TripStatus`)

Thuộc tính `Trip.Status` được khóa `private set`; mọi bước chuyển bắt buộc đi qua phân cấp lớp `TripState` và tự động ghi vết vào bảng `TripStatusHistory`:

- **Nhánh đặt chuyến & ghép tài xế:** `Requested` $\rightarrow$ `Quoted` $\rightarrow$ `DriverOffered` (lặp tối đa `k = 3` lượt khi `driver-reject`) $\rightarrow$ `DriverAccepted`. Các nhánh rẽ kết thúc sớm: `Cancelled` (khách hủy trước khi lên xe) hoặc `Failed` (không có tài xế bán kính `5 km` hoặc hết `k` lượt mời).
- **Nhánh thực hiện hành trình:** `DriverAccepted` $\rightarrow$ `EnRoute` $\rightarrow$ `PickedUp` $\rightarrow$ `DroppedOff` $\rightarrow$ (tự động chuyển tiếp nguyên tử trong cùng giao dịch) `PaymentPending`.
- **Nhánh quyết toán & bồi thường Saga:**
  - Từ `PaymentPending`: nhận `PaymentSettled` $\rightarrow$ `Paid`; nhận `PaymentFailed` $\rightarrow$ `PaymentFailed` (đồng thời phát `TripClosed` giải phóng tài xế).
  - Từ `Paid`: khi `Admin` phê duyệt qua `POST /trips/{id}/refund-approval` $\rightarrow$ `RefundPending`; nhận `PaymentRefunded` $\rightarrow$ `Refunded`; nhận `PaymentRefundFailed` $\rightarrow$ `RefundNeedsAttention`.

---

## 4. Thiết kế Schema Vật lý 4 Cơ sở dữ liệu

### 4.1. `UserDb` (SQL Server `:1435` — Identity Service)
- `Roles`: `Id (PK)`, `Name (UNIQUE)` — seed cố định `1=Rider`, `2=Driver`, `3=Admin`.
- `Users`: `Id (PK)`, `FullName`, `Phone (UNIQUE)`, `PasswordHash`, `RoleId (FK -> Roles, RESTRICT)`, `IsActive`, `CreatedAt`.
- `OutboxMessages`: `Id (PK = EventId)`, `Type`, `Payload`, `OccurredAt`, `ProcessedAt`.

### 4.2. `TripDb` (SQL Server `:1434` — Trip Service)
- `Trips`: `Id (PK)`, `RiderId`, `Status`, `CreatedAt`, `UpdatedAt`, `RowVersion (rowversion)`, `INDEX(RiderId, Status)`.
- `TripLocations`: `TripId (PK, FK)`, `PickupLat/Lng (decimal(9,6))`, `DropoffLat/Lng (decimal(9,6))`, `PickupAddress`, `DropoffAddress`.
- `TripFares`: `TripId (PK, FK)`, `QuoteId`, `Price (decimal(18,2))`, `Strategy`, `PaymentMethod`, `CardToken`, `Status`.
- `TripDriverOffers`: `Id (PK)`, `TripId (FK)`, `DriverId`, `DriverName` (snapshot), `AttemptNo`, `DistanceToPickupKm`, `Status`, `UNIQUE(TripId, AttemptNo)`, `INDEX(DriverId, Status)`.
- `TripStatusHistory`: `Id (PK)`, `TripId (FK)`, `FromStatus`, `ToStatus`, `ChangedAt`.
- `PaymentSagas`: `Id (PK)`, `TripId (UNIQUE, FK)`, `CorrelationId (UNIQUE)`, `State`, `ProcessPaymentEventId`, `PaymentDeadlineAt`, `PaymentTimeoutEventId`, `PaymentId`, `RefundPaymentEventId`, `RefundReasonCode`, `RefundEvidenceReference`, `RefundApprovedBy`, `RowVersion`.
- `OutboxMessages`: hỗ trợ thuê lô nguyên tử đa instance bằng `ClaimedBy`, `ClaimedUntil`, `AttemptCount`.
- `ProcessedPaymentResults`: `EventId (PK)`, `TripId`, `EventType`, `PayloadHash` (SHA-256), `ProcessedAt`.

### 4.3. `MatchDb` (MongoDB `:27018` — Matching & Pricing Service)
- Collection `drivers`: `_id` (`DriverId`), `Name`, `Status` (`Offline | Available | Busy`), `Location` (`GeoJSON Point [lng, lat]` có chỉ mục `2dsphere`), `StatusTripId`, `LastTripEventAt`, `ClosedTripIds`.
- Collection `quotes`: `_id` (`QuoteId`), `TripId`, `DistanceKm`, `DurationMin`, `Strategy`, `Params { Cd, Ct, S, Cm, Eta, Tax }`, `Price`, `CreatedAt`.

### 4.4. `PayDb` (PostgreSQL `:5433` — Payment Service, `snake_case`)
- `accounts`: `id (PK)`, `owner_id`, `owner_type`, `balance (numeric(18,2))`, `xmin` (concurrency token), `UNIQUE(owner_id, owner_type)`. Seed sẵn `PLATFORM`, `GATEWAY` và ví của các tài khoản demo.
- `payments`: `id (PK)`, `trip_id (UNIQUE)`, `rider_id`, `driver_id`, `amount`, `method`, `card_token`, `status`, `failure_reason`, `idempotency_key (UNIQUE)`.
- `ledger_entries`: `id (PK)`, `payment_id (FK)`, `account_id (FK)`, `entry_type` (`CHARGE | REFUND`), `amount` (có dấu), `created_at` — chỉ thêm mới (*append-only*).
- `refund_receipts`: `idempotency_key (PK)`, `payment_id (UNIQUE, FK)`, `reason`, `status`, `created_at`.
- `fake_card_authorizations` & `fake_card_refunds`: lưu trạng thái cấp phép/hoàn thẻ giả lập bất biến theo khóa chống trùng.
- `outbox_messages`: `payload (jsonb)`, có **Partial Index** `WHERE processed_at IS NULL` kết hợp `FOR UPDATE SKIP LOCKED`.
- `processed_messages`: `message_id (PK)`, `processed_at`.

---

## 5. Ma trận Kiểm soát Đồng thời (Concurrency Control)

| Tình huống tranh chấp | Kỹ thuật sử dụng | Vị trí mã nguồn |
|---|---|---|
| Hai request cùng sửa 1 bản ghi `Trip` hoặc 1 phiên `PaymentSaga` | Khóa lạc quan `RowVersion` (`409 CONCURRENCY_CONFLICT`) | `TripConfigurations.cs`, `PaymentSagaConfiguration.cs` |
| Một tài xế bấm nhận 2 chuyến đi khác nhau cùng một thời điểm | Khóa tư vấn giao dịch `sys.sp_getapplock` trên `driver-assignment:{DriverId}` | `TripRepository.SaveChangesAsync()` |
| Hai luồng thanh toán cùng `IdempotencyKey` hoặc cùng `TripId` khi dòng chưa kịp tạo | Khóa tư vấn `pg_advisory_xact_lock(hashtextextended(...))` | `PaymentRepository.LockRequestAsync()` |
| Hai giao dịch cùng cập nhật chéo số dư ví `Rider/Driver/Platform` | Khóa dòng `SELECT ... ORDER BY id FOR UPDATE` (chống Deadlock vòng tròn) + cột `xmin` | `AccountRepository.GetForUpdateAsync()` |
