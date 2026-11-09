namespace PaymentService.Application.Exceptions;

public abstract class AppException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public sealed class ValidationException(string message) : AppException(400, "VALIDATION_ERROR", message);
public sealed class UnauthorizedException(string code, string message) : AppException(401, code, message);
public sealed class ForbiddenException(string code, string message) : AppException(403, code, message);
public sealed class NotFoundException(string code, string message) : AppException(404, code, message);
public sealed class ConflictException(string code, string message) : AppException(409, code, message);
