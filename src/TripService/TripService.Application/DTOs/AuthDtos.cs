namespace TripService.Application.DTOs;

public record RegisterRequest(string FullName, string Phone, string Password, string Role);

public record UserDto(Guid Id, string FullName, string Phone, string Role);
