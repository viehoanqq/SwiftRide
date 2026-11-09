using PaymentService.Infrastructure;
using PaymentService.Api.Middleware;
using PaymentService.Api.Security;
using PaymentService.Application.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using PaymentService.Application.Options;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<PaymentOptions>()
    .Bind(builder.Configuration.GetRequiredSection(PaymentOptions.SectionName))
    .Validate(options => options.DriverRate >= 0 && options.DriverRate <= 1,
        "Payment:DriverRate is required and must be between 0 and 1.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<PaymentOptions>>().Value);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddPaymentSecurity(builder.Configuration);
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(
        new ApiError("VALIDATION_ERROR", "Dữ liệu request thiếu hoặc không đúng định dạng."));
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
        Description = "Token user cho ví/truy vấn; token service có scope Payment cho API nội bộ."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
    await app.Services.MigrateDatabaseAsync();

app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }
