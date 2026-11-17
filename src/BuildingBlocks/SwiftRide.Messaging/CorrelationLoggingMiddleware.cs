using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SwiftRide.Messaging;

public sealed class CorrelationLoggingMiddleware(RequestDelegate next,
    ILogger<CorrelationLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = Guid.TryParse(context.Request.Headers["X-Correlation-Id"], out var supplied)
            && supplied != Guid.Empty ? supplied : Guid.NewGuid();
        context.Response.Headers["X-Correlation-Id"] = requestId.ToString("D");
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["RequestCorrelationId"] = requestId,
            ["TraceId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier,
            ["HttpMethod"] = context.Request.Method,
            ["HttpPath"] = context.Request.Path.Value
        });
        try
        {
            await next(context);
            logger.LogInformation("HTTP request completed. Outcome={Outcome} StatusCode={StatusCode} RequestCorrelationId={RequestCorrelationId}\n      Diễn giải: Yêu cầu {Method} {Path} đã hoàn tất với mã HTTP {StatusCode}; mã theo dõi yêu cầu: {RequestCorrelationId}.",
                "Completed", context.Response.StatusCode, requestId, context.Request.Method,
                context.Request.Path.Value, context.Response.StatusCode, requestId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "HTTP request failed. Outcome={Outcome} RequestCorrelationId={RequestCorrelationId}\n      Diễn giải: Yêu cầu {Method} {Path} gặp lỗi; mã theo dõi yêu cầu: {RequestCorrelationId}.",
                "Failed", requestId, context.Request.Method, context.Request.Path.Value, requestId);
            throw;
        }
    }
}
