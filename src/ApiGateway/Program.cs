var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient();   // cung cấp IHttpClientFactory để gọi sang các service

var app = builder.Build();

app.MapControllers();

app.Run();
