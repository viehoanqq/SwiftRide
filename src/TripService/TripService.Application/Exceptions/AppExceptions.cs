namespace TripService.Application.Exceptions;

public abstract class AppException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public class ValidationException(string message) : AppException(400, "VALIDATION_ERROR", message);

public class ConflictException(string code, string message) : AppException(409, code, message);
