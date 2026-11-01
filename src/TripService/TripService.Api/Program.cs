using TripService.Infrastructure;
using TripService.Api.Middleware;              // thêm
using TripService.Application.Services;  
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<AuthAppService>();          // ← thêm
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

await app.Services.MigrateDatabaseAsync();
app.UseMiddleware<ExceptionMiddleware>();              // ← thêm, đặt TRƯỚC Swagger và MapControllers

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
