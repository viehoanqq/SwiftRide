# SwiftRide

Hệ thống đặt xe công nghệ theo kiến trúc **Microservices**, **Clean Architecture**, **Polyglot Persistence**, **Event-Driven Transactional Outbox** và **Orchestrated Payment/Refund Saga** trên nền tảng **.NET 10** & **React**.  
Đồ án môn học **Kiến trúc Phần mềm** — Khoa Công nghệ Thông tin, **Trường Đại học Sài Gòn** (Học kỳ I, Năm học 2026–2027).

---

## 1. Tổng quan Kiến trúc Hệ thống

Toàn bộ giao tiếp từ bên ngoài đi qua cổng biên duy nhất là **API Gateway (`:5000`)**. Phía sau Gateway gồm **4 microservice độc lập** (mỗi service tuân thủ **Clean Architecture 4 tầng** và sở hữu **cơ sở dữ liệu vật lý riêng**), kết hợp **giao tiếp lai** (*REST đồng bộ* cho báo giá/ghép xe tức thời và *RabbitMQ + Transactional Outbox* cho đồng bộ trạng thái, thu tiền và hoàn tiền):

| Thành phần | Cổng | Cơ sở dữ liệu sở hữu | Trách nhiệm cốt lõi |
|---|---|---|---|
| **API Gateway** (`src/ApiGateway`) | `:5000` | — | Định tuyến tập trung bằng **YARP**, gộp `GET /health` song song (timeout `5s`), chặn biên `403 INTERNAL_ENDPOINT_ONLY` trước các API thanh toán nội bộ. |
| **Identity Service** (`src/IdentityService`) | `:5004` | SQL Server `UserDb` (`:1435`) | Đăng ký, đăng nhập (BCrypt + User JWT `HMAC-SHA256`), cấp quản trị viên qua `AdminBootstrapper`, phát Outbox `UserRegistered` & `DriverRegistered`. |
| **Trip Service** (`src/TripService`) | `:5001` | SQL Server `TripDb` (`:1434`) | Máy trạng thái chuyến đi **15 trạng thái** (*State Pattern*), mời tài xế tuần tự tối đa `k = 3` lượt, khóa `sp_getapplock` + `RowVersion`, vận hành **Payment & Refund Saga**. |
| **Matching & Pricing Service** (`src/MatchingService`) | `:5002` | MongoDB `MatchDb` (`:27018`) | Tính giá theo **Strategy Pattern** (`Standard`, `Surge`, `Promo`), tìm tài xế gần nhất bằng chỉ mục cầu `2dsphere` (`$nearSphere`), chống lệch thứ tự sự kiện qua `StatusTripId` & `ClosedTripIds`. |
| **Payment Service** (`src/PaymentService`) | `:5003` | PostgreSQL `PayDb` (`:5433`) | Chọn bộ xử lý Ví/Thẻ qua **Factory Pattern**, sổ cái kép **Double-Entry** (`80%` tài xế / `20%` nền tảng), cổng thẻ giả lập `FakeCardAuthorizationGateway` và hoàn tiền đảo bút toán gốc. |
| **Web Frontend** (`frontend/`) | `:5173` | — | Giao diện React + Vite cho 3 vai trò `Rider`, `Driver`, `Admin` (đặt xe, nhận chuyến, theo dõi ví/sổ cái, duyệt hoàn tiền). |

```text
                    Rider / Driver / Admin (React :5173 | Postman)
                                          │
                                          ▼
         ┌────────────────────── API Gateway (YARP :5000) ──────────────────────┐
         │  /auth/*     /trips/*, /drivers/me/*     /availability/*    GET only │
         ▼                        ▼                        ▼              ▼
  Identity Service          Trip Service           Matching Service   Payment Service
      (:5004)                 (:5001)                  (:5002)            (:5003)
         │               │       ├─── REST (/quote, /match) ──►           ▲   │
         │               │       └────────── REST Reconcile ──────────────┘   │
         ▼               ▼                        ▼                           ▼
      UserDb          TripDb                   MatchDb                      PayDb
 (SQL Server :1435) (SQL Server :1434)     (MongoDB :27018)           (PostgreSQL :5433)
         │               │ ▲                      ▲                         │ ▲
         └─► Outbox      │ │ Outbox / Consume     │ Consume 3 Queues        │ │ Outbox / Consume
             ┌───────────▼─┴──────────────────────┴─────────────────────────▼─┴──┐
             │          RabbitMQ 3 + MassTransit 8 (:5672 / UI :15672)           │
             │   Fanout Exchanges ──► Classic Durable Queues & *_error DLQs      │
             └───────────────────────────────────────────────────────────────────┘
```

---

## 2. Danh mục Tài liệu Dự án (Documentation Hub)

Toàn bộ tài liệu kiến trúc, hợp đồng giao tiếp, hướng dẫn kiểm thử và báo cáo học thuật của dự án được tổ chức đầy đủ tại [`docs/`](docs/) và [`postman/`](postman/):

### Báo cáo Đồ án & Đặc tả Môn học
- [`docs/SwiftRide-KienTrucPhanMem-Backend.tex`](docs/SwiftRide-KienTrucPhanMem-Backend.tex) — **Báo cáo LaTeX chuyên sâu phân hệ Backend** (môn Kiến trúc Phần mềm: ADRs, Clean Architecture, Schema 4 CSDL, State Machine, Outbox, Saga, SOLID, 11 Design Patterns, Trade-offs & FMEA).
- [`docs/SwiftRide-BaoCaoDoAn.docx`](docs/SwiftRide-BaoCaoDoAn.docx) — Báo cáo tổng hợp đồ án định dạng Word.
- [`docs/SwiftRide-DacTaYeuCau.pdf`](docs/SwiftRide-DacTaYeuCau.pdf) — Tài liệu đặc tả yêu cầu phần mềm (SRS).
- [`docs/RideLite-KienTrucPhanMem.pdf`](docs/RideLite-KienTrucPhanMem.pdf) — Tài liệu định hướng bài toán tham khảo của môn học.

### Tài liệu Kiến trúc Phân hệ & Quy ước Phát triển
- [`docs/KIEN-TRUC-TONG-THE-VA-CSDL.md`](docs/KIEN-TRUC-TONG-THE-VA-CSDL.md) — Kiến trúc tổng thể 4 Microservices, YARP Gateway, tách đôi `BuildingBlocks`, máy trạng thái 15 trạng thái, Schema vật lý 4 CSDL & ma trận khóa đồng thời.
- [`docs/IDENTITY-BAO-MAT-VA-ADMIN-BOOTSTRAP.md`](docs/IDENTITY-BAO-MAT-VA-ADMIN-BOOTSTRAP.md) — Thiết kế Identity Service, mô hình bảo mật 4 lớp (*User Role JWT* vs. *Service Scope JWT*) và hướng dẫn khởi tạo tài khoản `Admin` bằng `AdminBootstrapper`.
- [`docs/MATCHING-DINH-GIA-VA-GEO-QUERY.md`](docs/MATCHING-DINH-GIA-VA-GEO-QUERY.md) — Công thức định giá toán học Haversine, Strategy Pattern, truy vấn không gian MongoDB `2dsphere` (`$nearSphere`) và cơ chế chống lệch thứ tự sự kiện (`StatusTripId` / `ClosedTripIds`).
- [`docs/HUONG-DAN-CODE-API.md`](docs/HUONG-DAN-CODE-API.md) & [`CONTRIBUTING.md`](CONTRIBUTING.md) — Quy trình 7 bước phát triển API mới theo Clean Architecture và chuẩn hóa hợp đồng lỗi `{ code, message }`.

### Tài liệu Messaging, RabbitMQ, Thanh toán & Saga
- [`docs/INTEGRATION-EVENT-CONTRACT.md`](docs/INTEGRATION-EVENT-CONTRACT.md) — Chuẩn hóa `IIntegrationEvent`, 5 trường metadata truy vết và danh mục hợp đồng sự kiện.
- [`docs/RABBITMQ-TRONG-SWIFTRIDE.md`](docs/RABBITMQ-TRONG-SWIFTRIDE.md) — Topology RabbitMQ & MassTransit 8, Transactional Outbox đa instance (`UPDLOCK/READPAST` & `SKIP LOCKED`), Exponential Retry và hàng đợi lỗi `*_error`.
- [`docs/MESSAGING-CORRELATION-LOGGING.md`](docs/MESSAGING-CORRELATION-LOGGING.md) — Truy vết phân tán xuyên dịch vụ qua `X-Correlation-Id`, `CorrelationId` và `CausationId`.
- [`docs/PAYMENT-CONTRACT-DOT-5.md`](docs/PAYMENT-CONTRACT-DOT-5.md) — Giao thức thanh toán nội bộ, Sổ cái kép Double-Entry và khóa chống trùng `IdempotencyKey`.
- [`docs/FACTORY-VA-REFUND-PAYMENT.md`](docs/FACTORY-VA-REFUND-PAYMENT.md) — Áp dụng `PaymentMethodProcessorFactory` và nguyên lý hoàn tiền bất biến đảo dấu bút toán gốc.
- [`docs/CARD-GIA-LAP-CONTRACT.md`](docs/CARD-GIA-LAP-CONTRACT.md) — Hợp đồng cổng thẻ giả lập `FakeCardAuthorizationGateway` (`test-card-approved`, `test-card-declined`, `test-card-unknown`).
- [`docs/WALLET-REGISTRATION.md`](docs/WALLET-REGISTRATION.md) — Cơ chế tự động mở ví người dùng idempotent từ sự kiện `UserRegistered`.
- [`docs/SAGA-VA-BOI-THUONG-THANH-TOAN.md`](docs/SAGA-VA-BOI-THUONG-THANH-TOAN.md) — Thiết kế chi tiết `PaymentSagaInstance`, xử lý quá hạn `PaymentUncertain`, đối soát chủ động `PaymentSagaReconciler` và quy trình bồi thường `Refund Saga`.

### Tài liệu Demo UI & Kiểm thử Tự động Postman
- [`docs/FRONTEND-VA-KICH-BAN-DEMO-E2E.md`](docs/FRONTEND-VA-KICH-BAN-DEMO-E2E.md) — Cấu trúc các trang Frontend, bảng tài khoản Seed mẫu và **5 kịch bản trình diễn End-to-End**.
- [`postman/TAI-LIEU-API-VA-POSTMAN.md`](postman/TAI-LIEU-API-VA-POSTMAN.md) & [`docs/TAI-LIEU-API-VA-POSTMAN.md`](docs/TAI-LIEU-API-VA-POSTMAN.md) — Đặc tả toàn bộ REST API và hướng dẫn chạy Postman Collection Runner.
- [`postman/POSTMAN-TEST-CHI-TIET.md`](postman/POSTMAN-TEST-CHI-TIET.md) — Hướng dẫn kiểm thử chi tiết từng nhánh Payment Saga & Refund Saga.
- Bộ file import Postman:
  - [`postman/SwiftRide-Gateway.local.postman_environment.json`](postman/SwiftRide-Gateway.local.postman_environment.json) (Environment chuẩn qua Gateway `:5000`)
  - [`postman/SwiftRide-EndToEnd.postman_collection.json`](postman/SwiftRide-EndToEnd.postman_collection.json) (E2E luồng chuẩn `WALLET`)
  - [`postman/SwiftRide-Card-EndToEnd.postman_collection.json`](postman/SwiftRide-Card-EndToEnd.postman_collection.json) (E2E 3 kịch bản `CARD`)
  - [`postman/SwiftRide-RefundSaga.postman_collection.json`](postman/SwiftRide-RefundSaga.postman_collection.json) (E2E Admin duyệt `Refund Saga`)

---

## 3. Ngăn xếp Công nghệ

| Phân lớp | Công nghệ & Thư viện thực tế |
|---|---|
| **Backend Runtime** | [.NET 10 SDK](https://dotnet.microsoft.com/download), ASP.NET Core Web API (Controllers), Swagger / OpenAPI |
| **API Gateway** | `Yarp.ReverseProxy` + Middleware chặn endpoint nội bộ + Parallel Health Aggregation |
| **Truy cập dữ liệu** | EF Core 10 (`SqlServer`, `Npgsql.EntityFrameworkCore.PostgreSQL` + SnakeCase), `MongoDB.Driver` 3.x |
| **Messaging & Outbox** | RabbitMQ 3 (`3-management`) + MassTransit 8 (`MassTransit.RabbitMQ`) + Background Outbox Workers |
| **Bảo mật** | Dual JWT Bearer (`HMAC-SHA256`: User Role JWT & Internal 2-min Service Scope JWT), `BCrypt.Net-Next` |
| **Frontend** | React 19, React Router, Vite (Node.js 20+) |
| **Kiểm thử & Hạ tầng** | `xUnit`, `Moq`, `WebApplicationFactory`, SQLite In-Memory, Postman Runner, Docker Compose |

---

## 4. Hướng dẫn Cài đặt & Khởi chạy Dự án

### 4.1. Kiểm tra công cụ & Cấu hình `.env`

1. Kiểm tra nhanh các công cụ bắt buộc (`.NET 10 SDK`, `dotnet-ef`, `Docker Desktop`, `Node.js 20+`):
   ```powershell
   .\1-check-tools.ps1
   # Nếu thiếu công cụ trên máy Windows, có thể chạy tự động cài đặt:
   .\2-install-missing.ps1
   ```
2. Tạo file biến môi trường `.env` từ file mẫu [`.env.example`](.env.example) (nếu chưa có):
   ```powershell
   Copy-Item .env.example .env
   ```
   > **Mẹo khi chạy Demo đầy đủ (Thẻ giả lập + Admin duyệt hoàn tiền):** Mở `.env` và đặt:
   > - `Payment__FakeCard__Enabled='true'` (bật 3 token kiểm thử thẻ trong môi trường `Development`/`Testing`).
   > - `Identity__AdminBootstrap__Phone='0999999999'` và `Identity__AdminBootstrap__Password='AdminPassword123!'` (để `AdminBootstrapper` tự khởi tạo tài khoản `Admin` ngay khi `IdentityService` chạy).

### 4.2. Cách 1 — Khởi chạy nhanh toàn bộ bằng 1 lệnh (Khuyên dùng trên Windows)

```powershell
.\3-run-services.ps1
```
Script tự động bật 5 container Docker (`docker compose up -d`), cài đặt `node_modules` (nếu thiếu), mở các cửa sổ chạy cả 4 microservice + API Gateway + Frontend Vite. Khi muốn dừng toàn bộ tiến trình và hạ container:

```powershell
.\stop-services.ps1
```

### 4.3. Cách 2 — Khởi chạy thủ công từng thành phần

1. **Khởi động 4 cơ sở dữ liệu & RabbitMQ:**
   ```powershell
   docker compose up -d
   docker ps   # Đảm bảo cả 5 container swiftride-* đều ở trạng thái Up
   ```
2. **Chạy 4 Microservices và API Gateway** (mỗi lệnh trên một cửa sổ terminal):
   ```powershell
   dotnet run --project src/IdentityService/IdentityService.Api
   dotnet run --project src/MatchingService/MatchingService.Api
   dotnet run --project src/PaymentService/PaymentService.Api
   dotnet run --project src/TripService/TripService.Api
   dotnet run --project src/ApiGateway
   ```
   - `Identity`, `Trip`, `Payment` tự động áp dụng EF Core Migrations và dữ liệu mẫu khi khởi động.
   - `Matching` tự động tạo chỉ mục `2dsphere` và seed 5 tài xế quanh TP.HCM nếu collection trống.
3. **Chạy giao diện Frontend:**
   ```powershell
   cd frontend
   npm install
   npm run dev
   ```

### 4.4. Bảng Địa chỉ Truy cập & Kiểm tra Sức khỏe Hệ thống

| Thành phần | Địa chỉ URL | Ghi chú |
|---|---|---|
| **Web Frontend** | http://localhost:5173 | Giao diện Rider / Driver / Admin |
| **Gateway Health Check** | http://localhost:5000/health | Tổng hợp trạng thái sống của cả 4 service & 4 DB |
| **Identity Swagger** | http://localhost:5004/swagger | API `/auth/*` |
| **Trip Swagger** | http://localhost:5001/swagger | API `/trips/*`, `/drivers/me/*` |
| **Matching Swagger** | http://localhost:5002/swagger | API `/availability/*` & Internal `/quote`, `/match` |
| **Payment Swagger** | http://localhost:5003/swagger | API `/wallets/me`, `/payments/*` |
| **RabbitMQ Dashboard** | http://localhost:15672 | Đăng nhập: `swiftride` / `swiftride` |

---

## 5. Tài khoản Mẫu (Seed Data) & Kết nối Cơ sở dữ liệu

### 5.1. Tài khoản Demo khởi tạo sẵn (Mật khẩu chung: `123456`)

| Vai trò | Số điện thoại | Họ tên | Thông tin khởi tạo sẵn |
|---|---|---|---|
| **Rider** | `0900000001` | Nguyễn Văn An | Số dư ví **500.000 đ** (demo thanh toán `WALLET` thành công) |
| **Rider** | `0900000002` | Trần Thị Bình | Số dư ví **20.000 đ** (demo nhánh ví không đủ tiền $\rightarrow$ `PaymentFailed`) |
| **Driver** | `0910000001` | Lê Văn Cường | `Available` tại Chợ Bến Thành (`10.7725, 106.6980`), ví `0 đ` |
| **Driver** | `0910000002` | Phạm Minh Dũng | `Available` tại Nhà thờ Đức Bà (`10.7798, 106.6990`), ví `0 đ` |
| **Driver** | `0910000003` | Hoàng Văn Em | `Available` tại Bitexco (`10.7716, 106.7044`), ví `0 đ` |
| **Driver** | `0910000004` | Võ Thị Phương | `Available` tại Dinh Độc Lập (`10.7770, 106.6953`), ví `0 đ` |
| **Driver** | `0910000005` | Đặng Quốc Gia | `Available` tại Đại học Sài Gòn (`10.7598, 106.6823`), ví `0 đ` |
| **Admin** | Theo `.env` | Quản trị viên | Cấp qua `Identity__AdminBootstrap__Phone` & `Password` |

### 5.2. Thông tin kết nối 4 Cơ sở dữ liệu & Message Broker

Cổng phía máy chủ (*Host Port*) đã được ánh xạ lệch khỏi cổng mặc định để không xung đột với dịch vụ cài sẵn trên máy cá nhân:

| Hạ tầng | Container | Kết nối từ máy Host | Tài khoản / Mật khẩu | Công cụ quản trị gợi ý |
|---|---|---|---|---|
| **TripDb** (SQL Server) | `swiftride-sqlserver` | `localhost,1434` | `sa` / `SwiftRide@123` | SSMS, Azure Data Studio (*TrustServerCertificate*) |
| **UserDb** (SQL Server) | `swiftride-identity-sqlserver` | `localhost,1435` | `sa` / `SwiftRide@123` | SSMS, Azure Data Studio (*TrustServerCertificate*) |
| **PayDb** (PostgreSQL) | `swiftride-postgres` | `localhost:5433` | `swiftride` / `swiftride` | pgAdmin 4, DBeaver |
| **MatchDb** (MongoDB) | `swiftride-mongodb` | `mongodb://localhost:27018` | Không mật khẩu (local) | MongoDB Compass |
| **RabbitMQ** | `swiftride-rabbitmq` | `localhost:5672` (UI `:15672`) | `swiftride` / `swiftride` | RabbitMQ Management Web UI |

---

## 6. Cấu trúc Thư mục Dự án

```text
SwiftRide/
├── src/
│   ├── ApiGateway/                         # YARP Reverse Proxy (:5000), chặn API nội bộ, gộp /health
│   ├── BuildingBlocks/
│   │   ├── SwiftRide.Contracts/            # 0 NuGet: IIntegrationEvent, 12 Event/Saga records, PaymentProtocol
│   │   └── SwiftRide.Messaging/            # MassTransit 8 / RabbitMQ, Retry, DLQ *_error, DotEnvLoader, Logging
│   ├── IdentityService/                    # (:5004) Domain / Application / Infrastructure (UserDb) / Api
│   ├── TripService/                        # (:5001) Domain (13 States) / Application (Sagas) / Infra (TripDb) / Api
│   ├── MatchingService/                    # (:5002) Domain (Pricing Strategies) / App / Infra (MatchDb) / Api
│   └── PaymentService/                     # (:5003) Domain (MoneyRules, Settlement) / App (Factory) / Infra (PayDb) / Api
├── frontend/                               # Ứng dụng React + Vite (:5173) cho Rider, Driver, Admin
├── tests/                                  # Bộ kiểm thử tự động xUnit (Trip, Matching, Payment)
├── postman/                                # 3 bộ Postman E2E Collections + Environment + Tài liệu kiểm thử
├── docs/                                   # Toàn bộ tài liệu kiến trúc, hợp đồng sự kiện, Saga & Báo cáo LaTeX/Word
├── scripts/                                # Script di trú & tách dữ liệu Identity (split-identity, move-userdb)
├── 1-check-tools.ps1                       # Kiểm tra môi trường máy phát triển
├── 2-install-missing.ps1                   # Tự động cài công cụ còn thiếu trên Windows
├── 3-run-services.ps1                      # Khởi chạy toàn bộ Docker + Backend + Frontend
├── stop-services.ps1                       # Dừng toàn bộ tiến trình & Docker Compose
├── docker-compose.yml                      # Định nghĩa 5 container hạ tầng (2 SQL Server, Postgres, Mongo, RabbitMQ)
├── .env.example                            # Mẫu cấu hình biến môi trường tập trung
├── CONTRIBUTING.md                         # Hướng dẫn quy ước code & phát triển API mới
└── SwiftRide.sln                           # Visual Studio / .NET Solution
```

---

## 7. Kiểm thử & Thao tác Cơ sở dữ liệu

### Chạy bộ kiểm thử tự động (.NET xUnit & Frontend Build)

```powershell
# Build toàn bộ Backend Solution và chạy toàn bộ Unit / Integration Tests (tự động dùng In-Memory Bus)
dotnet test SwiftRide.sln

# Kiểm tra lint và đóng gói giao diện Frontend
cd frontend
npm run lint
npm run build
```

### Làm việc với EF Core Migrations

```powershell
# Identity Service (UserDb)
cd src/IdentityService
dotnet ef migrations add <TenMigration> -p IdentityService.Infrastructure -s IdentityService.Api -o Persistence/Migrations

# Trip Service (TripDb)
cd src/TripService
dotnet ef migrations add <TenMigration> -p TripService.Infrastructure -s TripService.Api -o Persistence/Migrations

# Payment Service (PayDb)
cd src/PaymentService
dotnet ef migrations add <TenMigration> -p PaymentService.Infrastructure -s PaymentService.Api -o Persistence/Migrations
```

### Làm sạch dữ liệu Docker để chạy lại từ đầu

```powershell
.\stop-services.ps1
docker compose down -v
.\3-run-services.ps1
```
*(Nếu làm việc trên dữ liệu cũ trước khi tách `IdentityService`, xem hướng dẫn chạy `scripts/split-identity.ps1` và `scripts/move-userdb-container.ps1` trong thư mục [`scripts/`](scripts/)).*

---

## 8. Tiến độ Hoàn thiện Đồ án

| Đợt | Hạng mục kiến trúc & chức năng | Trạng thái |
|---|---|---|
| **Đợt 0** | Khung Clean Architecture, `docker-compose` đa CSDL, kiểm tra sức khỏe tổng hợp `/health` | ✅ Hoàn thành |
| **Đợt 1** | Tách `IdentityService` (`UserDb :1435`), BCrypt, JWT, `AdminBootstrapper`, định tuyến YARP Gateway | ✅ Hoàn thành |
| **Đợt 2** | `Matching & Pricing`: công thức Haversine, Strategy Pattern, chỉ mục `2dsphere` (`$nearSphere`), bật/tắt trực tuyến | ✅ Hoàn thành |
| **Đợt 3** | Đặt chuyến, báo giá đồng bộ, xác nhận phương thức thanh toán (`WALLET` / `CARD`), hủy chuyến | ✅ Hoàn thành |
| **Đợt 4** | Đề xuất tài xế tuần tự tối đa `k` lần, khóa chống tranh chấp `sp_getapplock` + `RowVersion`, vòng đời chuyến 15 trạng thái | ✅ Hoàn thành |
| **Đợt 5** | `PaymentService`: Factory Pattern, sổ cái kép Double-Entry (`80/20`), `FakeCardAuthorizationGateway`, hoàn tiền đảo bút toán | ✅ Hoàn thành |
| **Đợt 6** | RabbitMQ + MassTransit 8, Transactional Outbox đa instance, Idempotent Consumer, **Payment & Refund Saga** + Reconciler | ✅ Hoàn thành |
| **Đợt 7** | Bộ kiểm thử tự động `xUnit`, 3 bộ Postman E2E Collections, hoàn thiện Frontend (Rider/Driver/Admin) & Báo cáo Kiến trúc | ✅ Hoàn thành |

---

## 9. Quy ước Làm việc Nhóm với Git

- Mỗi thành viên phát triển trên nhánh riêng, **không push trực tiếp lên `main`** (mọi thay đổi vào `main` đều đi qua Pull Request & Code Review).
- Trước khi tạo EF Core Migration mới, bắt buộc `git pull origin main` và đảm bảo không có hai người tạo migration trên cùng một service cùng lúc.
- Không commit các thư mục sinh tự động (`bin/`, `obj/`, `node_modules/`, `.vs/`) hoặc thông tin bí mật thực tế trong `.env`. Xem chi tiết quy ước code tại [`CONTRIBUTING.md`](CONTRIBUTING.md).
