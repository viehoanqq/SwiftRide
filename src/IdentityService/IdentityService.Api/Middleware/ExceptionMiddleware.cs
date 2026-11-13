using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using IdentityService.Application.Exceptions;

namespace IdentityService.Api.Middleware;

public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (AppException ex)
        {
            context.Response.StatusCode = ex.StatusCode;
            await context.Response.WriteAsJsonAsync(new { code = ex.Code, message = ex.Message });
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            context.Response.StatusCode = 409;
            await context.Response.WriteAsJsonAsync(new { code = "PHONE_ALREADY_EXISTS", message = "Phone is already registered." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Identity request failed");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "An unexpected error occurred." });
        }
    }
}
