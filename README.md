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

## Làm việc với Git

Mỗi thành viên làm trên **một nhánh riêng** đặt theo tên mình. **Không push thẳng lên `main`**: code vào `main` chỉ qua Pull Request.

### tạo nhánh (làm 1 lần)
git checkout -b an                 # thay "an" bằng tên của bạn
git push -u origin an
```

### Mỗi lần làm việc

```powershell
git checkout an

# 1. Code xong → COMMIT TRƯỚC khi pull
git add .
git status                         # không được có bin/, obj/, node_modules/
git commit -m "feat(trip): API đăng ký, đăng nhập"

# 2. Lấy code mới nhất của main về nhánh mình
git pull origin main
#    có xung đột → sửa → git add . → git commit

# 3. Build thử rồi đẩy nhánh lên GitHub
dotnet build
git push origin an
```

### Tạo Pull Request

1. Trên GitHub: **Pull requests → New pull request**, chọn **base: `main` ← compare: `an`**.
2. Ghi tiêu đề (ví dụ `Đợt 1: API đăng ký, đăng nhập`) và mô tả đã làm gì, test thế nào.
3. Chọn **Reviewers** là một thành viên khác → **Create pull request**.
4. Cần sửa theo góp ý: sửa trên nhánh của mình → commit → push, PR tự cập nhật.
5. Người review thấy ổn thì **Merge pull request**.

### Sau khi PR được merge

```powershell
git checkout an
git pull origin main
git push origin an
```


### Lưu ý riêng cho dự án

- **Migration:** mỗi lúc chỉ một người tạo migration cho một service. Luôn `git pull origin main` trước khi chạy `dotnet ef migrations add`. Không sửa file migration đã có trên `main`.
- **Không commit** `bin/`, `obj/`, `node_modules/`, `.vs/` (đã có trong `.gitignore`).
- Pull về có migration mới: chỉ cần chạy lại service là database tự cập nhật.
