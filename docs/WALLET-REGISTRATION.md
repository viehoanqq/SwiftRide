# Khởi tạo ví từ Identity

Identity lưu User và `UserRegistered` vào UserDb trong cùng `SaveChanges` transaction. `WalletRegistrationWorker` đọc Outbox rồi gọi Payment; chỉ ghi `ProcessedAt` sau khi nhận HTTP thành công. Lỗi mạng/Payment dừng sẽ được thử lại trong các vòng chạy sau (nghỉ 10 giây giữa các vòng).

API nội bộ Payment:

```http
PUT /internal/accounts/users/{userId}
Authorization: Bearer <service-token>
Content-Type: application/json

{"userId":"<userId>","role":"Rider"}
```

- Role chỉ nhận `Rider` hoặc `Driver`; UserId phải khớp route và khác Guid.Empty.
- Token phải có `sub=identity-service`, `scope=wallets:register`. Token Rider/Driver hoặc token tạo payment của Trip không được phép.
- Thành công trả `204`, kể cả khi ví đã tồn tại. Gateway không có route cho API này.
- Account mới có `OwnerId=UserId`, OwnerType theo role, Balance=0; không tạo ledger vì chưa phát sinh tiền.
- Unique `(owner_id, owner_type)` và `ON CONFLICT DO NOTHING` bảo đảm retry/concurrent request không tạo trùng, không reset số dư.

## Chạy và đồng bộ user cũ

Khởi động lại Payment (5003) và Identity (5004) với code mới. Identity sử dụng `Services:PaymentService=http://localhost:5003`; có thể override bằng biến môi trường `Services__PaymentService` khi triển khai.

Worker tự bổ sung sự kiện cho user cũ chưa có `UserRegistered`, tối đa 50 user/vòng, rồi gửi sang Payment. Ví seed/đã có tiền được giữ nguyên. Không cần đổi schema hoặc kết nối chéo database. Giữ sự kiện đã xử lý để tránh quét lại; nếu sự kiện bị dọn, gửi lại vẫn an toàn.

User mới có thể đăng nhập trước khi đồng bộ xong. `/wallets/me` khi chưa có ví vẫn trả `404 ACCOUNT_NOT_FOUND`; frontend hiển thị “Ví đang được khởi tạo” và nút thử lại. Ví mới không được cấp tiền test tự động.

Kiểm tra: đăng ký Rider/Driver, chờ worker; kiểm tra `UserRegistered.ProcessedAt` ở UserDb và Account theo UserId ở PayDb. Tạm dừng Payment rồi đăng ký: User vẫn được lưu, sự kiện chưa xử lý; chạy lại Payment sẽ tạo ví. Gọi lại API đăng ký ví không thay đổi số dư hiện tại.
