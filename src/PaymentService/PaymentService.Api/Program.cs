using PaymentService.Infrastructure;
using PaymentService.Api.Middleware;
using PaymentService.Api.Security;
using PaymentService.Application.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using PaymentService.Application.Options;
using Microsoft.Extensions.Options;
SwiftRide.Messaging.DotEnvLoader.Load();
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<PaymentOptions>()
    .Bind(builder.Configuration.GetRequiredSection(PaymentOptions.SectionName))
    .Validate(options => options.DriverRate >= 0 && options.DriverRate <= 1,
        "Payment:DriverRate is required and must be between 0 and 1.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<PaymentOptions>>().Value);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddScoped<PaymentService.Application.Interfaces.IPaymentAppService,
    PaymentService.Application.Services.PaymentAppService>();
builder.Services.AddScoped<PaymentService.Application.Interfaces.IRefundAppService,
    PaymentService.Application.Services.RefundAppService>();
builder.Services.AddScoped<PaymentService.Application.Interfaces.IWalletAppService,
    PaymentService.Application.Services.WalletAppService>();
builder.Services.AddPaymentSecurity(builder.Configuration);
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(
        new ApiError("VALIDATION_ERROR", "Dá»¯ liá»‡u request thiáº¿u hoáº·c khÃ´ng Ä‘Ãºng Ä‘á»‹nh dáº¡ng."));
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
        Description = "Token user cho vÃ­/truy váº¥n; token service cÃ³ scope Payment cho API ná»™i bá»™."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

var app = builder.Build();
app.UseMiddleware<SwiftRide.Messaging.CorrelationLoggingMiddleware>();

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

