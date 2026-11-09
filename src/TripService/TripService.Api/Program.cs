using TripService.Infrastructure;
using TripService.Api.Middleware;              // thêm
using TripService.Application.Services;  
using TripService.Application.Interfaces;
using TripService.Application.Options;

using System.Text;                                       // thêm
using Microsoft.AspNetCore.Authentication.JwtBearer;     // thêm
using Microsoft.IdentityModel.Tokens;                    // thêm
using Microsoft.OpenApi; 


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<AuthAppService>();          // ← thêm
builder.Services.AddScoped<TripAppService>();
builder.Services.AddScoped<ITripDriverWorkflow>(serviceProvider =>
    serviceProvider.GetRequiredService<TripAppService>());
builder.Services.Configure<TripWorkflowOptions>(
    builder.Configuration.GetSection(TripWorkflowOptions.SectionName));
builder.Services.AddSingleton(sp => sp.GetRequiredService<
    Microsoft.Extensions.Options.IOptions<TripWorkflowOptions>>().Value);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();



// ← SỬA: thêm nút "Authorize" vào Swagger để dán token khi test
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
});


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
    }
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
