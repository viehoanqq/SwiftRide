# SwiftRide

Hệ thống đặt xe theo kiến trúc **Microservices**, bài tập lớn môn **Kiến trúc Phần mềm**, Trường Đại học Sài Gòn (HK I, 2026–2027).

Gồm 3 microservice, mỗi service có cơ sở dữ liệu riêng (database-per-service, polyglot persistence), đi qua một API Gateway:

| Service | Trách nhiệm | Database | Cổng |
|---|---|---|---|
| **API Gateway** | Điểm vào duy nhất cho client, gom dữ liệu từ các service | — | 5000 |
| **Trip Service** | Vòng đời chuyến đi (state machine), tài khoản người dùng | SQL Server | 5001 |
| **Matching & Pricing Service** | Tìm tài xế gần nhất (geo-query), tính giá | MongoDB | 5002 |
| **Payment Service** | Thanh toán, sổ cái double-entry, hoàn tiền | PostgreSQL | 5003 |

```
Rider / Driver (React)
        │
        ▼
  API Gateway :5000 ──────────────┬───────────────────┐
        │                         │                   │
        ▼                         ▼                   ▼
  Trip Service :5001    Matching Service :5002   Payment Service :5003
        │                         │                   │
        ▼                         ▼                   ▼
  TripDB (SQL Server)     MatchDB (MongoDB)      PayDB (PostgreSQL)

            RabbitMQ: sự kiện giữa các service (đợt 6)
```

---

## Công nghệ

| Thành phần | Công nghệ |
|---|---|
| Backend | .NET 10, ASP.NET Core Web API (Controllers) |
| ORM / Data | EF Core 10 (SQL Server, PostgreSQL), MongoDB.Driver |
| API Gateway | ASP.NET Core (YARP cho định tuyến ở đợt 1) |
| Message broker | RabbitMQ + MassTransit 8 |
| Frontend | React + Vite |
| Hạ tầng | Docker, docker-compose |
| Kiểm thử | xUnit + Moq |

---

## Yêu cầu cài đặt

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [Node.js 20+](https://nodejs.org/) (cho frontend)
- Công cụ EF Core: `dotnet tool install --global dotnet-ef`

---

## Chạy dự án

### 1. Bật database và RabbitMQ

```powershell
docker compose up -d
docker ps          # 4 container swiftride-* phải ở trạng thái Up
```

### 2. Chạy các service (mỗi lệnh một terminal)

```powershell
dotnet run --project src/TripService/TripService.Api
dotnet run --project src/MatchingService/MatchingService.Api
dotnet run --project src/PaymentService/PaymentService.Api
dotnet run --project src/ApiGateway
```

Không cần chạy lệnh tạo database. Khi khởi động, mỗi service tự chuẩn bị DB của mình:
- **Trip, Payment:** tự áp dụng EF Core migration (tạo bảng + dữ liệu mẫu).
- **Matching:** tự tạo index và seed 5 tài xế nếu collection còn trống.

### 3. Kiểm tra

| Địa chỉ | Nội dung |
|---|---|
| http://localhost:5000/health | Trạng thái database của cả 3 service (qua Gateway) |
| http://localhost:5001/swagger | Swagger – Trip Service |
| http://localhost:5002/swagger | Swagger – Matching Service |
| http://localhost:5003/swagger | Swagger – Payment Service |
| http://localhost:15672 | RabbitMQ Management (`swiftride` / `swiftride`) |

Kết quả mong đợi của `/health`:

```json
{
  "status": "Healthy",
  "services": [
    { "service": "matching-service", "database": "MongoDB",    "status": "Healthy" },
    { "service": "payment-service",  "database": "PostgreSQL", "status": "Healthy" },
    { "service": "trip-service",     "database": "SQL Server", "status": "Healthy" }
  ]
}
```

### 4. Frontend

```powershell
cd frontend
npm install
npm run dev        # http://localhost:5173
```

---

## Tài khoản demo

Mật khẩu của tất cả tài khoản: **`123456`**. ID tài khoản giống nhau ở cả 3 database.

| Số điện thoại | Họ tên | Vai trò | Số dư ví |
|---|---|---|---|
| 0900000001 | Nguyễn Văn An | Rider | 500.000đ |
| 0900000002 | Trần Thị Bình | Rider | 20.000đ (dùng để demo thanh toán lỗi) |
| 0910000001 | Lê Văn Cường | Driver | 0đ |
| 0910000002 | Phạm Minh Dũng | Driver | 0đ |
| 0910000003 | Hoàng Văn Em | Driver | 0đ |
| 0910000004 | Võ Thị Phương | Driver | 0đ |
| 0910000005 | Đặng Quốc Gia | Driver | 0đ |

5 tài xế được đặt sẵn vị trí quanh Quận 1, TP.HCM, trạng thái `Available`.

---

## Kết nối database

Cổng phía máy được đổi khác cổng mặc định để không trùng với SQL Server / PostgreSQL / MongoDB cài sẵn trên máy.

| Database | Container | Kết nối từ máy | Tài khoản | Công cụ gợi ý |
|---|---|---|---|---|
| TripDb | `swiftride-sqlserver` | `localhost,1434` | `sa` / `SwiftRide@123` | SSMS, Azure Data Studio (tick *Trust server certificate*) |
| PayDb | `swiftride-postgres` | `localhost:5433` | `swiftride` / `swiftride` | pgAdmin, DBeaver |
| MatchDb | `swiftride-mongodb` | `mongodb://localhost:27018` | — | MongoDB Compass |

> Mật khẩu trên chỉ dùng cho container chạy trên máy cá nhân. Hệ thống thật cần dùng biến môi trường hoặc secret manager.

---

## Cấu trúc thư mục

```
SwiftRide/
├─ src/
│  ├─ ApiGateway/                   # Điểm vào, gom /health
│  ├─ BuildingBlocks/
│  │  └─ SwiftRide.Contracts/       # Sự kiện dùng chung (đợt 6)
│  ├─ TripService/
│  │  ├─ TripService.Domain/        # Entity, enum (không phụ thuộc gì)
│  │  ├─ TripService.Application/   # Use case, interface
│  │  ├─ TripService.Infrastructure/# EF Core, migration, seed
│  │  └─ TripService.Api/           # Controller, Program.cs
│  ├─ MatchingService/              # Cùng 4 lớp, MongoDB
│  └─ PaymentService/               # Cùng 4 lớp, PostgreSQL
├─ frontend/                        # React + Vite
├─ tests/                           # xUnit cho từng service
├─ docker-compose.yml               # 3 database + RabbitMQ
└─ SwiftRide.sln
```

Quy tắc tham chiếu trong mỗi service: `Api → Application → Domain`, `Infrastructure → Application`.
Các service **không tham chiếu project của nhau**; chỉ giao tiếp qua HTTP hoặc sự kiện.

---

## Làm việc với database

### Thay đổi entity (Trip / Payment)

```powershell
# Trip Service
cd src/TripService
dotnet ef migrations add TenThayDoi -p TripService.Infrastructure -s TripService.Api -o Persistence/Migrations

# Payment Service
cd src/PaymentService
dotnet ef migrations add TenThayDoi -p PaymentService.Infrastructure -s PaymentService.Api -o Persistence/Migrations
```

Chạy lại service là migration mới được áp dụng (hoặc dùng `dotnet ef database update -p ... -s ...`).

### Thay đổi document (Matching)

MongoDB không có migration: sửa class trong `Infrastructure/Mongo/Documents.cs`; thêm index trong `MongoContext.EnsureIndexesAsync()`.

### Xóa sạch dữ liệu, làm lại từ đầu

```powershell
docker compose down -v
docker compose up -d
# chạy lại 3 service → database được tạo lại cùng dữ liệu mẫu
```

---

## Tiến độ

| Đợt | Nội dung | Trạng thái |
|---|---|---|
| 0 | Cấu trúc project, docker-compose, database 3 service, health check | ✅ Xong |
| 1 | Đăng ký, đăng nhập (JWT), định tuyến Gateway, khung frontend | ⏳ |
| 2 | Matching & Pricing: báo giá, tìm tài xế, bật/tắt nhận chuyến | ⏳ |
| 3 | Đặt xe, báo giá, xác nhận giá | ⏳ |
| 4 | Ghép tài xế (tối đa k lần), thực hiện chuyến | ⏳ |
| 5 | Thanh toán, sổ cái, ví, hoàn tiền | ⏳ |
| 6 | RabbitMQ, Outbox, Saga | ⏳ |
| 7 | Unit test, Docker hóa toàn bộ, kịch bản demo | ⏳ |

---

## Tài liệu

Nằm ở thư mục `docs/` (cạnh thư mục `SwiftRide/`):

| File | Nội dung |
|---|---|
| `SwiftRide-DacTaYeuCau.pdf` | Đặc tả: chức năng, use case, user story, sơ đồ luồng |
| `STRUCTURE.md` | Cấu trúc project chi tiết, pattern nằm ở đâu |
| `KE-HOACH-THUC-HIEN.md` | Kế hoạch theo đợt: API, màn hình, tiêu chí hoàn thành |
| `HUONG-DAN-TAO-DATABASE.md` | Hướng dẫn tạo database từ đầu (EF Core migration, MongoDB) |

---

## Xử lý sự cố

| Hiện tượng | Nguyên nhân | Cách xử lý |
|---|---|---|
| Service báo lỗi kết nối khi khởi động | Container chưa chạy | `docker compose up -d`, chờ khoảng 10 giây rồi chạy lại |
| Gateway báo `address already in use` | Cổng 5000 bị ứng dụng khác chiếm | Tắt ứng dụng đó, hoặc đổi cổng trong `src/ApiGateway/Properties/launchSettings.json` |
| Mở SSMS / pgAdmin không thấy bảng | Đang kết nối vào CSDL cài trên máy | Dùng đúng cổng container: 1434 / 5433 / 27018 |
| `The model ... has pending changes` | Sửa entity nhưng chưa tạo migration | `dotnet ef migrations add ...` |
| `/health` báo `Unreachable` | Service tương ứng chưa chạy | Chạy service đó |
| `/health` báo `Unhealthy` | Database của service đó không kết nối được | Kiểm tra container: `docker ps`, `docker start swiftride-...` |
