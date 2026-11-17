using TripService.Infrastructure;
using TripService.Api.Middleware;              // thÃªm
using TripService.Application.Services;  
using TripService.Application.Interfaces;
using TripService.Application.Options;

using System.Text;                                       // thÃªm
using Microsoft.AspNetCore.Authentication.JwtBearer;     // thÃªm
using Microsoft.IdentityModel.Tokens;                    // thÃªm
using Microsoft.OpenApi; 

SwiftRide.Messaging.DotEnvLoader.Load();
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddScoped<TripAppService>();
builder.Services.AddScoped<ITripDriverWorkflow>(serviceProvider =>
    serviceProvider.GetRequiredService<TripAppService>());
builder.Services.Configure<TripWorkflowOptions>(
    builder.Configuration.GetSection(TripWorkflowOptions.SectionName));
builder.Services.AddSingleton(sp => sp.GetRequiredService<
    Microsoft.Extensions.Options.IOptions<TripWorkflowOptions>>().Value);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();



// â† Sá»¬A: thÃªm nÃºt "Authorize" vÃ o Swagger Ä‘á»ƒ dÃ¡n token khi test
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "DÃ¡n accessToken láº¥y tá»« POST /auth/login"
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});


var jwt = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;            // giá»¯ nguyÃªn tÃªn claim "sub", "name", "role"
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt["Issuer"],             // pháº£i khá»›p lÃºc kÃ½ trong JwtTokenService
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!)),
            ClockSkew = TimeSpan.FromSeconds(30),    // cho lá»‡ch giá» tá»‘i Ä‘a 30s (máº·c Ä‘á»‹nh 5 phÃºt)
            NameClaimType = "name",
            RoleClaimType = "role"                   // Ä‘á»ƒ sau nÃ y [Authorize(Roles = "Driver")] Ä‘á»c Ä‘Ãºng
        };
        options.Events = new JwtBearerEvents
{
    OnForbidden = async context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;

        await context.Response.WriteAsJsonAsync(new
        {
            code = "FORBIDDEN",
            message = "Báº¡n khÃ´ng cÃ³ quyá»n thá»±c hiá»‡n thao tÃ¡c nÃ y."
        });
    },
    OnChallenge = async context =>
    {
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
        var (code, message) = context.AuthenticateFailure switch
        {
            SecurityTokenExpiredException => ("TOKEN_EXPIRED", "PhiÃªn Ä‘Äƒng nháº­p Ä‘Ã£ háº¿t háº¡n."),
            not null => ("TOKEN_INVALID", "Token khÃ´ng há»£p lá»‡."),
            _ => ("UNAUTHORIZED", "Báº¡n cáº§n Ä‘Äƒng nháº­p Ä‘á»ƒ thá»±c hiá»‡n thao tÃ¡c nÃ y.")
        };
        await context.Response.WriteAsJsonAsync(new { code, message });
    }
};
    });
builder.Services.AddAuthorization();



var app = builder.Build();
app.UseMiddleware<SwiftRide.Messaging.CorrelationLoggingMiddleware>();





if (!app.Environment.IsEnvironment("Testing"))
    await app.Services.MigrateDatabaseAsync();
app.UseMiddleware<ExceptionMiddleware>();              // â† thÃªm, Ä‘áº·t TRÆ¯á»šC Swagger vÃ  MapControllers

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();     // â† THÃŠM: Ä‘á»c vÃ  kiá»ƒm tra token. PHáº¢I Ä‘á»©ng TRÆ¯á»šC UseAuthorization

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }

