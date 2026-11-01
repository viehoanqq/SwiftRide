using System.Text.RegularExpressions;
using TripService.Application.DTOs;
using TripService.Application.Exceptions;
using TripService.Application.Interfaces;
using TripService.Domain.Entities;

namespace TripService.Application.Services;

public class AuthAppService(IUserRepository users, IPasswordHasher passwordHasher)
{
    public async Task<UserDto> RegisterAsync(RegisterRequest request)
    {
        // 1. Kiểm tra dữ liệu
        var fullName = request.FullName.Trim();
        var phone = request.Phone.Trim();

        if (fullName.Length == 0 || fullName.Length > 100)
            throw new ValidationException("Họ tên không được để trống và tối đa 100 ký tự");
        if (!Regex.IsMatch(phone, @"^0\d{9}$"))
            throw new ValidationException("Số điện thoại phải gồm 10 chữ số, bắt đầu bằng 0");
        if (request.Password.Length < 6)
            throw new ValidationException("Mật khẩu phải có ít nhất 6 ký tự");

        var (roleId, roleName) = request.Role.Trim().ToLower() switch
        {
            "rider" => (Role.RiderId, "Rider"),
            "driver" => (Role.DriverId, "Driver"),
            _ => throw new ValidationException("Vai trò chỉ được là Rider hoặc Driver")
        };

        // 2. Kiểm tra trùng số điện thoại
        if (await users.PhoneExistsAsync(phone))
            throw new ConflictException("PHONE_ALREADY_EXISTS", "Số điện thoại đã được đăng ký");

        // 3. Tạo user, băm mật khẩu, lưu
        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = roleId,
            FullName = fullName,
            Phone = phone,
            PasswordHash = passwordHasher.Hash(request.Password),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        await users.AddAsync(user);

        // 4. Trả về, KHÔNG trả mật khẩu
        return new UserDto(user.Id, user.FullName, user.Phone, roleName);
    }
}
