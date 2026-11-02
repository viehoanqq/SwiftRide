using TripService.Application.Exceptions;

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
        catch (Exception ex)                        // lỗi không lường trước: 500
        {
            logger.LogError(ex, "Lỗi không mong muốn");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "Có lỗi xảy ra, vui lòng thử lại" });
        }
    }
}
