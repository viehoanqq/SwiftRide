using Microsoft.EntityFrameworkCore;
using TripService.Application.Exceptions;
using TripService.Domain.Exceptions;

namespace TripService.Api.Middleware;

public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AppException ex)                     // lỗi nghiệp vụ: 400, 409...
        {
            context.Response.StatusCode = ex.StatusCode;
            await context.Response.WriteAsJsonAsync(new { code = ex.Code, message = ex.Message });
        }
        catch (InvalidTripStateException ex)        // lỗi chuyển trạng thái từ Domain: 409
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new { code = "INVALID_TRIP_STATE", message = ex.Message });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var conflictingEntries = ex.Entries.Select(entry =>
            {
                var key = entry.Metadata.FindPrimaryKey();
                var keyValues = key is null
                    ? "unknown key"
                    : string.Join(", ", key.Properties.Select(property =>
                        $"{property.Name}={entry.Property(property.Name).CurrentValue}"));
                return $"{entry.Metadata.GetTableName()} ({keyValues})";
            });
            logger.LogWarning(
                ex,
                "Xung đột cập nhật đồng thời tại: {Entries}",
                string.Join("; ", conflictingEntries));
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "CONCURRENCY_CONFLICT",
                message = "Dữ liệu chuyến đi vừa được cập nhật bởi yêu cầu khác. Vui lòng tải lại chuyến đi."
            });
        }
        catch (Exception ex)                        // lỗi không lường trước: 500
        {
            logger.LogError(ex, "Lỗi không mong muốn");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "Có lỗi xảy ra, vui lòng thử lại" });
        }
        
        
    }
}
