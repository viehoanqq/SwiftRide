using MatchingService.Infrastructure;
using MatchingService.Application.Interfaces;
using MatchingService.Application.Services;
using MatchingService.Domain.Pricing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using MatchingService.Api.Middleware;
using System.Text;
SwiftRide.Messaging.DotEnvLoader.Load();
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddScoped<MatchingService.Application.Services.MatchAppService>();
builder.Services.AddScoped<QuoteAppService>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "DÃ¡n accessToken láº¥y tá»« POST /auth/login (Identity Service)"
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

// Kiá»ƒm tra token do Identity Service kÃ½ â€“ dÃ¹ng chung Issuer, Audience, Key.
var jwt = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;            // giá»¯ nguyÃªn tÃªn claim "sub", "name", "role"
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!)),
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            RoleClaimType = "role"                   // Ä‘á»ƒ [Authorize(Roles = "Driver")] Ä‘á»c Ä‘Ãºng
        };

        options.Events = new JwtBearerEvents
        {
            // Sáº¯p tráº£ 401: khÃ´ng cÃ³ token / token sai / háº¿t háº¡n
            OnChallenge = async context =>
            {
                context.HandleResponse();     // táº¯t pháº£n há»“i máº·c Ä‘á»‹nh (401 rá»—ng), dÃ¹ng pháº£n há»“i tá»± viáº¿t bÃªn dÆ°á»›i

                var (code, message) = context.AuthenticateFailure switch
                {
                    SecurityTokenExpiredException => ("TOKEN_EXPIRED", "PhiÃªn Ä‘Äƒng nháº­p Ä‘Ã£ háº¿t háº¡n, vui lÃ²ng Ä‘Äƒng nháº­p láº¡i"),
                    not null => ("TOKEN_INVALID", "Token khÃ´ng há»£p lá»‡"),
                    null => ("UNAUTHORIZED", "Báº¡n cáº§n Ä‘Äƒng nháº­p Ä‘á»ƒ thá»±c hiá»‡n thao tÃ¡c nÃ y")
                };

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { code, message });
            },

            // Sáº¯p tráº£ 403: token há»£p lá»‡ nhÆ°ng sai vai trÃ²
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { code = "FORBIDDEN", message = "Báº¡n khÃ´ng cÃ³ quyá»n thá»±c hiá»‡n thao tÃ¡c nÃ y" });
            }
    };
    });
var pricingSection = builder.Configuration.GetRequiredSection("Pricing");

var pricingParameters = new PricingParameters(
    pricingSection.GetValue<decimal>("Cd"),
    pricingSection.GetValue<decimal>("Ct"),
    pricingSection.GetValue<decimal>("S"),
    pricingSection.GetValue<decimal>("Cm"),
    pricingSection.GetValue<decimal>("Eta"),
    pricingSection.GetValue<decimal>("Tax"));

// Kiá»ƒm tra giÃ¡ trá»‹ cáº¥u hÃ¬nh ngay khi khá»Ÿi Ä‘á»™ng.
_ = new StandardPricing().Calculate(0, 0, pricingParameters);

builder.Services.AddSingleton(pricingParameters);
builder.Services.AddSingleton<StandardPricing>();
builder.Services.AddSingleton<SurgePricing>();
builder.Services.AddSingleton<PromoPricing>();
builder.Services.AddSingleton<
    IPricingStrategySelector, PricingStrategySelector>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("DriverRegistration", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("sub", "identity-service")
        .RequireClaim("scope", "drivers:register"));
    options.AddPolicy("DriverStatusSync", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("sub", "trip-service")
        .RequireClaim("scope", "drivers:status"));
});

var app = builder.Build();
app.UseMiddleware<SwiftRide.Messaging.CorrelationLoggingMiddleware>();

await app.Services.InitializeDatabaseAsync();
app.UseMiddleware<ExceptionMiddleware>();              // â† thÃªm, Ä‘áº·t TRÆ¯á»šC Swagger vÃ  MapControllers

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseAuthentication();                             // â† thÃªm: kiá»ƒm tra token, PHáº¢I trÆ°á»›c UseAuthorization
app.UseAuthorization();
app.MapControllers();

app.Run();

