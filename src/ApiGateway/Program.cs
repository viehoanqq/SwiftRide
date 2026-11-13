var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient();   // cung cấp IHttpClientFactory để gọi sang các service
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()));
// YARP: chuyển tiếp request theo cấu hình "ReverseProxy" trong appsettings.json
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
var app = builder.Build();

app.UseCors();

app.Use(async (context, next) =>
{
    var segments = (context.Request.Path.Value ?? "")
        .Split('/', StringSplitOptions.RemoveEmptyEntries);

    var isBlockedPost = HttpMethods.IsPost(context.Request.Method) &&
        (
            (segments.Length == 1 &&
             string.Equals(segments[0], "payments", StringComparison.OrdinalIgnoreCase)) ||
            (segments.Length == 3 &&
             string.Equals(segments[0], "payments", StringComparison.OrdinalIgnoreCase) &&
             string.Equals(segments[2], "refund", StringComparison.OrdinalIgnoreCase)) ||
            (segments.Length == 3 &&
             string.Equals(segments[0], "trips", StringComparison.OrdinalIgnoreCase) &&
             string.Equals(segments[2], "dropoff", StringComparison.OrdinalIgnoreCase))
        );

    if (isBlockedPost)
    {
        context.Response.StatusCode = StatusCodes.Status501NotImplemented;
        await context.Response.WriteAsJsonAsync(new
        {
            code = "ENDPOINT_DISABLED",
            message = "API này hiện chưa được bật."
        });
        return;
    }

    await next();
});

app.MapControllers();     // GET /health
app.MapReverseProxy();    // /auth/* → Identity Service
app.Run();
