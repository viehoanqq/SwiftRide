using IdentityService.Infrastructure;
using IdentityService.Api.Middleware;              // thÃªm
using IdentityService.Application.Services;  
using IdentityService.Infrastructure.Persistence;
using IdentityService.Application.Interfaces;

using System.Text;                                       // thÃªm
using Microsoft.AspNetCore.Authentication.JwtBearer;     // thÃªm
using Microsoft.IdentityModel.Tokens;                    // thÃªm
using Microsoft.OpenApi; 

SwiftRide.Messaging.DotEnvLoader.Load();
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddScoped<AuthAppService>();          // â† thÃªm

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
        Description = "POST /auth/login"
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
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!)),
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
{
    OnForbidden = async context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;

        await context.Response.WriteAsJsonAsync(new
        {
            code = "FORBIDDEN",
            message = "Bạn không có quyền thực hiện thao tác này."
        });
    },
    OnChallenge = async context =>
    {
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
        var (code, message) = context.AuthenticateFailure switch
        {
            SecurityTokenExpiredException => ("TOKEN_EXPIRED", "Phiên đăng nhập đã hết hạn."),
            not null => ("TOKEN_INVALID", "Token không hợp lệ."),
            _ => ("UNAUTHORIZED", "Bạn cần đăng nhập để thực hiện thao tác này.")
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
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    await AdminBootstrapper.ProvisionAsync(
        scope.ServiceProvider.GetRequiredService<UserDbContext>(),
        scope.ServiceProvider.GetRequiredService<IPasswordHasher>(),
        app.Configuration);
}
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

