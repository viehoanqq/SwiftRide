namespace TripService.Application.DTOs;

public record RegisterRequest(string FullName, string Phone, string Password, string Role);

public record UserDto(Guid Id, string FullName, string Phone, string Role);

public record LoginRequest(string Phone, string Password);
public record LoginResponse(string AccessToken, int ExpiresIn, LoginUserDto User);
public record LoginUserDto(Guid Id, string FullName, string Phone, string Role);