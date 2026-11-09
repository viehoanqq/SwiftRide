using Microsoft.EntityFrameworkCore;
using Npgsql;
using PaymentService.Application.DTOs;
using PaymentService.Application.Exceptions;
using PaymentService.Domain.Exceptions;

namespace PaymentService.Api.Middleware;

public sealed class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (AppException ex)
        {
            await WriteAsync(context, ex.StatusCode, ex.Code, ex.Message);
        }
        catch (PaymentRuleException ex)
        {
            await WriteAsync(context, ex.Code == "VALIDATION_ERROR" ? 400 : 409, ex.Code, ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            await WriteAsync(context, 409, "CONCURRENCY_CONFLICT", "Số dư hoặc giao dịch vừa được cập nhật. Vui lòng thử lại.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The application must handle idempotency replay BEFORE this fallback.
            await WriteAsync(context, 409, "CONCURRENCY_CONFLICT", "Dữ liệu đã tồn tại hoặc đang được xử lý bởi yêu cầu khác.");
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            await WriteAsync(context, 409, "CONCURRENCY_CONFLICT", "Giao dịch bị xung đột. Vui lòng thử lại với cùng idempotency key.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected Payment Service failure. TraceId: {TraceId}", context.TraceIdentifier);
            await WriteAsync(context, 500, "INTERNAL_ERROR", "Có lỗi xảy ra, vui lòng thử lại.");
        }
    }

    private static Task WriteAsync(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ApiError(code, message));
    }
}
