# Hướng dẫn code một API mới (Backend + Frontend)

Quy trình chung để cả nhóm thêm API cho các đợt tiếp theo, dựa trên những gì đã làm ở đợt 1 (`/auth/register`, `/auth/login`, `/auth/me`).
Làm đúng thứ tự các bước để code giữa các tầng khớp nhau và không bỏ sót phần bảo mật.

---

## 1. Một request đi qua những đâu

```
Frontend (React)                 API Gateway :5000              Service (ví dụ Trip :5001)
─────────────────                ─────────────────              ──────────────────────────────────────────────
Page (pages/...)                                                ExceptionMiddleware   ← bắt lỗi → JSON { code, message }
  └─ xxxApi.js (api/...)                                          └─ UseAuthentication ← đọc, kiểm tra token
       └─ client.js  ── Bearer ──► CORS + YARP route ──────────►       └─ UseAuthorization ← [Authorize] / Roles
          (tự gắn token)            (chuyển tiếp nguyên header)             └─ Controller        ← lấy userId từ token
                                                                                └─ AppService     ← kiểm tra dữ liệu, quyền trên dữ liệu
                                                                                     └─ Domain    ← quy tắc nghiệp vụ (State pattern)
                                                                                     └─ Repository → Database
```

| Tầng | Thư mục | Việc của tầng | Không được làm |
|---|---|---|---|
| Domain | `*.Domain/` | Entity, enum, quy tắc nghiệp vụ (đổi trạng thái hợp lệ hay không) | Dùng EF Core, HTTP, JWT |
| Application | `*.Application/` | DTO, interface, service điều phối, kiểm tra dữ liệu và quyền | Gọi `DbContext`, `SaveChangesAsync`, BCrypt trực tiếp |
| Infrastructure | `*.Infrastructure/` | Repository (EF Core / MongoDB), JWT, BCrypt, đăng ký DI | Chứa logic nghiệp vụ |
| Api | `*.Api/` | Controller, middleware, `Program.cs` | Truy vấn DB, viết logic nghiệp vụ |

---

## 2. Quy ước chung

### Response lỗi

Mọi lỗi nghiệp vụ trả về cùng một dạng, do `ExceptionMiddleware` tạo ra:

```json
{ "code": "PHONE_ALREADY_EXISTS", "message": "Số điện thoại đã được đăng ký" }
```

Trong service chỉ cần `throw`, **không** `try/catch` trong controller:

| Exception (`Application/Exceptions/AppExceptions.cs`) | HTTP | Khi nào |
|---|---|---|
| `ValidationException(message)` | 400 | Dữ liệu sai quy tắc (số điện thoại sai dạng, giá âm…) |
| `UnauthorizedException(code, message)` | 401 | Sai thông tin đăng nhập |
| `ForbiddenException(code, message)` | 403 | Không có quyền trên dữ liệu (chuyến của người khác), tài khoản bị khóa |
| `NotFoundException(code, message)` | 404 | Không tìm thấy |
| `ConflictException(code, message)` | 409 | Trùng dữ liệu, chuyển trạng thái không hợp lệ |

`code` viết HOA_GẠCH_DƯỚI, có nghĩa rõ ràng: `TRIP_NOT_FOUND`, `TRIP_INVALID_STATE`, `NOT_TRIP_OWNER`…

### 401 hay 403

| Mã | Ý nghĩa | Ai trả |
|---|---|---|
| 401 | Không có token / token sai chữ ký / hết hạn → "bạn là ai?" | `UseAuthorization` tự trả khi API có `[Authorize]` |
| 403 | Token hợp lệ nhưng **sai vai trò** hoặc **không phải dữ liệu của bạn** | `[Authorize(Roles = ...)]` tự trả, hoặc service `throw new ForbiddenException(...)` |

### Đặt tên

| Thứ | Quy ước | Ví dụ |
|---|---|---|
| Request DTO | `<HànhĐộng>Request` | `CreateTripRequest`, `ConfirmTripRequest` |
| Response DTO | `<Tên>Dto` | `TripDto`, `UserDto` |
| Service | `<Nhóm>AppService` | `AuthAppService`, `TripAppService` |
| Repository | `I<Tên>Repository` hoặc tách `I<Tên>Reader` / `I<Tên>Writer` (ISP) | `IUserRepository`, `ITripReader` |
| Route | danh từ số nhiều, chữ thường, gạch ngang | `/trips/{id}/driver-accept` |

---

## 3. Backend: 7 bước cho một API mới

Ví dụ xuyên suốt: **`GET /trips/{id}` – xem chi tiết chuyến** (đợt 3).
Yêu cầu bảo mật: phải đăng nhập; **chỉ rider của chuyến hoặc tài xế được gán** mới xem được.

### Bước 1 – Domain *(chỉ khi cần)*

| Cần làm gì | Ví dụ |
|---|---|
| Bảng / cột mới | Thêm entity hoặc property trong `Domain/Entities/` |
| Quy tắc nghiệp vụ mới | Thêm method vào entity, ví dụ `trip.Confirm(...)`, `trip.Accept(driverId)`. Việc đổi trạng thái đi qua **State pattern** (`Domain/States/`), mỗi lần đổi ghi `TripStatusHistory` |

API chỉ đọc dữ liệu như `GET /trips/{id}` thường **không cần** bước này.

### Bước 2 – Migration *(chỉ khi Bước 1 đổi cấu trúc bảng)*

1. Sửa cấu hình bảng trong `Infrastructure/Persistence/Configurations/`.
2. Tạo migration:

```powershell
cd src/TripService
dotnet ef migrations add TenThayDoi -p TripService.Infrastructure -s TripService.Api -o Persistence/Migrations
```

Chạy lại service là migration tự áp dụng. Mỗi lúc chỉ **một người** tạo migration cho một service; `git pull origin main` trước khi tạo.

### Bước 3 – Application: DTO, interface, service

**3.1 DTO** – `Application/DTOs/TripDtos.cs`

```csharp
public record TripDto(Guid Id, string Status, Guid RiderId, decimal? Price, DateTime CreatedAt);
```

Chỉ trả những gì client cần. **Không bao giờ** trả `PasswordHash`, token thẻ, hay entity nguyên vẹn.

**3.2 Interface** – `Application/Interfaces/ITripReader.cs` (chỉ thêm hàm khi chưa có)

```csharp
public interface ITripReader
{
    Task<Trip?> GetByIdAsync(Guid id);
}
```

**3.3 Service** – `Application/Services/TripAppService.cs`

Thứ tự trong một hàm service: **lấy dữ liệu → kiểm tra quyền trên dữ liệu → gọi Domain → lưu → trả DTO.**

```csharp
public class TripAppService(ITripReader trips)
{
    public async Task<TripDto> GetByIdAsync(Guid tripId, Guid userId)
    {
        // 1. Lấy dữ liệu
        var trip = await trips.GetByIdAsync(tripId)
                   ?? throw new NotFoundException("TRIP_NOT_FOUND", "Không tìm thấy chuyến đi");

        // 2. Kiểm tra quyền trên dữ liệu: chỉ rider của chuyến hoặc tài xế đã nhận chuyến
        var driverId = trip.Offers.FirstOrDefault(o => o.Status == OfferStatus.Accepted)?.DriverId;
        if (trip.RiderId != userId && driverId != userId)
            throw new ForbiddenException("NOT_TRIP_MEMBER", "Bạn không có quyền xem chuyến này");

        // 3. Trả DTO
        return new TripDto(trip.Id, trip.Status.ToString(), trip.RiderId, trip.Fare?.Price, trip.CreatedAt);
    }
}
```

> **Quan trọng:** `userId` luôn được truyền từ **token** (controller lấy ra), không bao giờ nhận từ body hay query.
> Nếu nhận `userId` từ client, ai cũng có thể giả làm người khác.

### Bước 4 – Infrastructure: repository + đăng ký DI

**4.1 Hiện thực interface** – `Infrastructure/Repositories/TripRepository.cs`

```csharp
public class TripRepository(TripDbContext db) : ITripReader, ITripWriter
{
    public Task<Trip?> GetByIdAsync(Guid id) =>
        db.Trips
            .Include(t => t.Location)
            .Include(t => t.Fare)
            .Include(t => t.Offers)
            .Include(t => t.History)
            .FirstOrDefaultAsync(t => t.Id == id);
}
```

EF Core **không tự load bảng liên quan** – cần `Include` cho mọi navigation sẽ dùng (giống `Include(u => u.Role)` ở login).

**4.2 Đăng ký** – `Infrastructure/DependencyInjection.cs`

```csharp
services.AddScoped<ITripReader, TripRepository>();
services.AddScoped<ITripWriter, TripRepository>();
```

| Loại | Dùng khi |
|---|---|
| `AddScoped` | Dùng `DbContext` (repository, service gọi repository) |
| `AddSingleton` | Không giữ dữ liệu theo request (`PasswordHasher`, `JwtTokenService`, `MongoContext`) |

### Bước 5 – Api: controller + phân quyền

**5.1 Controller** – `Api/Controllers/TripsController.cs`

Người gọi là ai thì lấy từ token đã được `UseAuthentication()` kiểm tra, giống `AuthController.Me()`:

| Cần | Viết |
|---|---|
| Id người gọi | `var userId = Guid.Parse(User.FindFirstValue("sub")!);` |
| Vai trò người gọi | `var role = User.FindFirstValue("role")!;` |

Cần `using System.Security.Claims;` để dùng `FindFirstValue`.

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("trips")]
[Authorize]                                            // mặc định: mọi API trong controller đều cần đăng nhập
public class TripsController(TripAppService trips) : ControllerBase
{
    // GET /trips/{id} – Rider của chuyến hoặc Driver được gán
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);   // lấy Id từ token, KHÔNG nhận từ client
        return Ok(await trips.GetByIdAsync(id, userId));
    }

    // POST /trips – chỉ Rider
    [HttpPost]
    [Authorize(Roles = "Rider")]
    public async Task<IActionResult> Create(CreateTripRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        return StatusCode(StatusCodes.Status201Created, await trips.CreateAsync(userId, request));
    }
}
```

**5.2 Đăng ký service** – `Program.cs`

```csharp
builder.Services.AddScoped<TripAppService>();
```

### Bước 6 – Gateway: mở đường cho tiền tố mới *(chỉ khi có tiền tố đường dẫn mới)*

Gateway hiện chỉ chuyển tiếp `/auth/*`. Khi thêm nhóm API mới (`/trips/*`, `/drivers/me/*`, `/availability/*`, `/payments/*`, `/wallets/*`), thêm route trong `src/ApiGateway/appsettings.json`:

```json
"ReverseProxy": {
  "Routes": {
    "auth":  { "ClusterId": "trip", "Match": { "Path": "/auth/{**catch-all}" } },
    "trips": { "ClusterId": "trip", "Match": { "Path": "/trips/{**catch-all}" } }
  },
  "Clusters": {
    "trip": { "Destinations": { "d1": { "Address": "http://localhost:5001" } } }
  }
}
```

Service mới (Matching, Payment) thì thêm cluster tương ứng (`http://localhost:5002`, `http://localhost:5003`).

**API nội bộ** (`/quote`, `/match`, `POST /payments`, `/payments/{id}/refund`, `PUT /drivers/{id}/status`) **không thêm route** – client bên ngoài không gọi được.

### Bước 7 – Test bằng Swagger

1. `POST /auth/login` → copy `accessToken` → bấm **Authorize** → dán token (không gõ chữ `Bearer`).
2. Thử **đủ 5 trường hợp** cho mỗi API cần bảo vệ:

| # | Trường hợp | Mong đợi |
|---|---|---|
| 1 | Không có token | 401 |
| 2 | Token sai vai trò (ví dụ Driver gọi `POST /trips`) | 403 |
| 3 | Đúng vai trò nhưng dữ liệu của người khác (Rider A xem chuyến của Rider B) | 403 |
| 4 | Dữ liệu gửi lên sai | 400 / 404 / 409 kèm `code` |
| 5 | Đúng hết | 200 / 201 |

Sau đó thử lại qua Gateway (`http://localhost:5000/...`) để chắc route YARP đúng.

---

## 4. Bảo mật và phân quyền: tóm tắt

### Ba lớp kiểm tra

| Lớp | Ở đâu | Kiểm tra | Viết như thế nào |
|---|---|---|---|
| 1. Đăng nhập chưa | Controller | Có token hợp lệ | `[Authorize]` trên controller hoặc action |
| 2. Đúng vai trò không | Controller | `role` trong token | `[Authorize(Roles = "Rider")]`, `[Authorize(Roles = "Driver")]` |
| 3. Đúng dữ liệu của mình không | **Application service** | `trip.RiderId == userId`… | `throw new ForbiddenException(...)` |

Lớp 3 hay bị quên nhất. **Đúng vai trò chưa đủ**: mọi rider đều có role `Rider`, nhưng rider A không được đụng vào chuyến của rider B.

### Bảng phân quyền các API theo kế hoạch

| API | Thuộc tính | Kiểm tra thêm trong service |
|---|---|---|
| `POST /auth/register`, `POST /auth/login` | (không có `[Authorize]`) | — |
| `GET /auth/me` | `[Authorize]` | — |
| `GET /health` | (không có `[Authorize]`) | — |
| `POST /trips` | `[Authorize(Roles = "Rider")]` | Rider chưa có chuyến đang chạy |
| `POST /trips/{id}/confirm` | `[Authorize(Roles = "Rider")]` | `trip.RiderId == userId` |
| `GET /trips/{id}` | `[Authorize]` | Là rider của chuyến **hoặc** tài xế được gán |
| `GET /trips/me/current` | `[Authorize(Roles = "Rider")]` | Lọc theo `userId` |
| `GET /drivers/me/offers`, `/drivers/me/current-trip` | `[Authorize(Roles = "Driver")]` | Lọc theo `userId` |
| `POST /trips/{id}/driver-accept`, `driver-reject` | `[Authorize(Roles = "Driver")]` | Lời mời đang chờ là của `userId` |
| `POST /trips/{id}/en-route`, `pickup`, `dropoff` | `[Authorize(Roles = "Driver")]` | `userId` là tài xế đã nhận chuyến |
| `/availability/*` (Matching) | `[Authorize(Roles = "Driver")]` | Chỉ cập nhật bản ghi của `userId` |
| `GET /wallets/me` (Payment) | `[Authorize]` | Ví của `userId` |
| `GET /payments/{id}`, `/payments/by-trip/{tripId}` | `[Authorize]` | Là rider hoặc driver của giao dịch |

### Những điều không được làm

| ❌ Không làm | ✅ Làm |
|---|---|
| Nhận `userId`, `role` từ body / query | Lấy từ token: `Guid.Parse(User.FindFirstValue("sub")!)`, `User.FindFirstValue("role")` |
| Chỉ ẩn nút trên frontend rồi coi như đã bảo mật | Luôn kiểm tra lại ở backend (frontend chỉ để hiển thị) |
| Trả entity nguyên vẹn (`return Ok(user)`) | Trả DTO đã chọn lọc trường |
| Báo "số điện thoại không tồn tại" / "sai mật khẩu" riêng | Gộp chung 1 thông báo 401 |
| Ghi log token, mật khẩu, token thẻ | Không log dữ liệu nhạy cảm |
| Viết `try/catch` trả lỗi trong từng controller | `throw` exception, để `ExceptionMiddleware` xử lý |

### Khi làm API ở Matching / Payment Service

Mỗi service là một chương trình riêng, nên lần đầu thêm API cần đăng nhập vào service đó phải làm thêm:

1. Chép mục `"Jwt"` trong `TripService.Api/appsettings.json` sang `appsettings.json` của service đó – **giống hệt** (cùng `Issuer`, `Audience`, `Key`).
2. Chép phần `AddAuthentication().AddJwtBearer(...)`, `AddAuthorization()`, `UseAuthentication()` trong `TripService.Api/Program.cs`.
3. Chép `ExceptionMiddleware` và `AppExceptions.cs` (đổi namespace).
4. Matching chưa có package JWT: `dotnet add src/MatchingService/MatchingService.Api package Microsoft.AspNetCore.Authentication.JwtBearer`.

---

## 5. Frontend: thêm trang mới

### Cấu trúc

```
frontend/src/
├─ api/
│  ├─ client.js        ← KHÔNG sửa khi thêm API: lưu token, tự gắn Bearer, 401 → /login
│  ├─ authApi.js       ← mỗi nhóm API một file
│  ├─ tripApi.js       ← (đợt 3)
│  └─ walletApi.js     ← (đợt 5)
├─ auth/
│  ├─ AuthContext.jsx  ← useAuth(): user { id, name, role }, login(), logout()
│  └─ ProtectedRoute.jsx
├─ pages/
│  ├─ auth/            ← LoginPage, RegisterPage
│  ├─ rider/           ← BookTripPage, TripStatusPage...
│  └─ driver/          ← DriverHomePage, ActiveTripPage...
└─ App.jsx             ← khai báo route
```

### Quy tắc bắt buộc: luôn gọi API qua `client`

```js
import client from './client';          // ✅ đúng – token được gắn tự động
import axios from 'axios';              // ❌ sai – không có token, không xử lý 401
fetch('http://localhost:5000/...')      // ❌ sai – như trên
```

`client.js` đã "bọc" access token cho mọi request:

| Việc | Ở đâu trong `client.js` | Trang có cần làm gì không |
|---|---|---|
| Lưu / xóa token | `tokenStorage` (dùng trong `AuthContext`) | Không |
| Gắn `Authorization: Bearer <token>` | `interceptors.request` | Không |
| Token hết hạn / sai → về `/login` | `interceptors.response` (401) | Không |
| Lấy câu báo lỗi từ backend | `getErrorMessage(error)` | Gọi trong `catch` |

### Bước 1 – Thêm hàm gọi API: `api/tripApi.js`

```js
import client from './client';

export const tripApi = {
  create: data => client.post('/trips', data).then(res => res.data),
  getById: id => client.get(`/trips/${id}`).then(res => res.data),
  confirm: (id, data) => client.post(`/trips/${id}/confirm`, data).then(res => res.data),
  current: () => client.get('/trips/me/current').then(res => (res.status === 204 ? null : res.data)),
};
```

Đường dẫn bắt đầu từ `/` (không ghi `http://localhost:5000`) – `client` đã có `baseURL` trỏ tới Gateway.

### Bước 2 – Tạo trang

**Mẫu A – trang có form gửi dữ liệu** (`pages/rider/BookTripPage.jsx`):

```jsx
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { tripApi } from '../../api/tripApi';
import { getErrorMessage } from '../../api/client';

export default function BookTripPage() {
  const navigate = useNavigate();
  const [pickup, setPickup] = useState('');
  const [dropoff, setDropoff] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const handleSubmit = async e => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      const trip = await tripApi.create({ pickup, dropoff });
      navigate(`/rider/trips/${trip.tripId}`);
    } catch (err) {
      setError(getErrorMessage(err));       // hiện message 400/403/409 từ backend
    } finally {
      setLoading(false);
    }
  };

  return (
    <div>
      <h1>Đặt xe</h1>
      <form onSubmit={handleSubmit}>
        <div><label>Điểm đón <input value={pickup} onChange={e => setPickup(e.target.value)} required /></label></div>
        <div><label>Điểm đến <input value={dropoff} onChange={e => setDropoff(e.target.value)} required /></label></div>
        {error && <p>{error}</p>}
        <button type="submit" disabled={loading}>Đặt xe</button>
      </form>
    </div>
  );
}
```

**Mẫu B – trang tải dữ liệu, tự cập nhật** (`pages/rider/TripStatusPage.jsx`):

```jsx
import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { tripApi } from '../../api/tripApi';
import { getErrorMessage } from '../../api/client';

export default function TripStatusPage() {
  const { id } = useParams();
  const [trip, setTrip] = useState(null);
  const [error, setError] = useState('');

  useEffect(() => {
    const load = () => tripApi.getById(id).then(setTrip).catch(err => setError(getErrorMessage(err)));
    load();
    const timer = setInterval(load, 3000);    // polling 3 giây để thấy trạng thái đổi
    return () => clearInterval(timer);         // rời trang thì dừng
  }, [id]);

  if (error) return <p>{error}</p>;
  if (!trip) return <p>Đang tải...</p>;

  return (
    <div>
      <h1>Chuyến đi</h1>
      <p>Trạng thái: {trip.status}</p>
      <p>Giá: {trip.price}</p>
    </div>
  );
}
```

### Bước 3 – Khai báo route trong `App.jsx`, kèm vai trò

```jsx
<Route path="/rider/book" element={<ProtectedRoute role="Rider"><BookTripPage /></ProtectedRoute>} />
<Route path="/rider/trips/:id" element={<ProtectedRoute role="Rider"><TripStatusPage /></ProtectedRoute>} />
<Route path="/driver/trips/:id" element={<ProtectedRoute role="Driver"><ActiveTripPage /></ProtectedRoute>} />
```

| Cách khai báo | Ý nghĩa |
|---|---|
| `<ProtectedRoute role="Rider">` | Phải đăng nhập **và** là Rider; Driver vào sẽ bị chuyển về `/driver` |
| `<ProtectedRoute>` (không có `role`) | Chỉ cần đăng nhập, vai trò nào cũng được |
| Không bọc `ProtectedRoute` | Trang công khai (`/login`, `/register`) |

### Bước 4 – Dùng thông tin người dùng trong trang

```jsx
import { useAuth } from '../../auth/AuthContext';

const { user, logout } = useAuth();      // user = { id, name, role } đọc từ token
```

Dùng `user` để **hiển thị** (tên, ẩn/hiện nút theo vai trò). **Không** gửi `user.id` lên backend làm "người gọi" – backend tự lấy từ token.

### Kiểm tra trên trình duyệt (F12)

| Tab | Xem gì |
|---|---|
| **Network** → chọn request → *Request Headers* | Có `Authorization: Bearer eyJ...`; URL là `localhost:5000/...` (đi qua Gateway) |
| **Network** → *Response* | Lỗi có dạng `{ code, message }` |
| **Application** → *Local Storage* → `localhost:5173` | Có key `accessToken` sau khi đăng nhập, mất sau khi đăng xuất |
| **Console** | Lỗi CORS → Gateway chưa chạy bản mới hoặc thiếu route YARP |

---

## 6. Checklist trước khi tạo Pull Request

**Backend**
- [ ] API cần đăng nhập đã có `[Authorize]`; API theo vai trò có `[Authorize(Roles = ...)]`
- [ ] Service có kiểm tra **quyền trên dữ liệu** khi API đụng tới dữ liệu của một người cụ thể
- [ ] `userId` lấy từ token, không nhận từ body/query
- [ ] Response là DTO, không lộ trường nhạy cảm
- [ ] Lỗi dùng exception có `code` rõ nghĩa, không `try/catch` trong controller
- [ ] Repository có `Include` đủ bảng liên quan
- [ ] Đã đăng ký DI (repository trong `DependencyInjection.cs`, service trong `Program.cs`)
- [ ] Có migration nếu đổi cấu trúc bảng
- [ ] Gateway có route nếu là tiền tố đường dẫn mới
- [ ] Đã test đủ 5 trường hợp ở Bước 7

**Frontend**
- [ ] Gọi API qua `client` (trong file `api/xxxApi.js`), không dùng `axios`/`fetch` trực tiếp
- [ ] Route trong `App.jsx` có `ProtectedRoute` đúng `role`
- [ ] `catch` lỗi và hiển thị bằng `getErrorMessage`
- [ ] Nút gửi bị khóa khi đang chờ (`disabled={loading}`)
- [ ] Polling có `clearInterval` khi rời trang
- [ ] Tên file và đường dẫn `import` khớp **đúng hoa/thường** (Windows bỏ qua nhưng Docker/Linux sẽ lỗi)
