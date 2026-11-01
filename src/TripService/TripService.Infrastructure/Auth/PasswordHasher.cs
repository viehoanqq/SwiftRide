using TripService.Application.Interfaces;

namespace TripService.Infrastructure.Auth;

public class PasswordHasher : IPasswordHasher
{
    // BCrypt tự sinh salt và gắn vào chuỗi hash → không cần cột salt riêng
    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11);
}
