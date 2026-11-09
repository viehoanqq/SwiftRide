namespace TripService.Application.Exceptions;

public class DownstreamException(int statusCode, string code, string message)
    : AppException(statusCode, code, message);

public abstract class AppException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public class ValidationException(string message) : AppException(400, "VALIDATION_ERROR", message);

public class ConflictException(string code, string message) : AppException(409, code, message);

public class NotFoundException(string code, string message) : AppException(404, code, message);

public class UnauthorizedException(string code, string message) : AppException(401, code, message);

public class ForbiddenException(string code, string message) : AppException(403, code, message);
