using TripService.Infrastructure;
using TripService.Api.Controllers;
using TripService.Api.Middleware;
using TripService.Application.Services;

using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Filters;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<AuthAppService>();          // ← thêm
//builder.Services.AddScoped<TripService.Application.Services.TripAppService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Dán accessToken lấy từ POST /auth/login"
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
    options.ExampleFilters();
});

builder.Services.AddSwaggerExamplesFromAssemblyOf<TripsController>();


var jwt = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;            // giữ nguyên tên claim "sub", "name", "role"
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt["Issuer"],             // phải khớp lúc ký trong JwtTokenService
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!)),
            ClockSkew = TimeSpan.FromSeconds(30),    // cho lệch giờ tối đa 30s (mặc định 5 phút)
            NameClaimType = "name",
            RoleClaimType = "role"                   // để sau này [Authorize(Roles = "Driver")] đọc đúng
        };
    });
builder.Services.AddAuthorization();



var app = builder.Build();





await app.Services.MigrateDatabaseAsync();
app.UseMiddleware<ExceptionMiddleware>();              // ← thêm, đặt TRƯỚC Swagger và MapControllers

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();     // ← THÊM: đọc và kiểm tra token. PHẢI đứng TRƯỚC UseAuthorization

app.UseAuthorization();

app.MapControllers();

app.Run();
