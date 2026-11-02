using MatchingService.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using MatchingService.Api.Middleware;
using System.Text;
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
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
        Description = "Dán accessToken lấy từ POST /auth/login (Trip Service)"
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

// Kiểm tra token do Trip Service ký – phải dùng chung Issuer, Audience, Key
var jwt = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;            // giữ nguyên tên claim "sub", "name", "role"
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!)),
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            RoleClaimType = "role"                   // để [Authorize(Roles = "Driver")] đọc đúng
        };

        options.Events = new JwtBearerEvents
        {
            // Sắp trả 401: không có token / token sai / hết hạn
            OnChallenge = async context =>
            {
                context.HandleResponse();     // tắt phản hồi mặc định (401 rỗng), dùng phản hồi tự viết bên dưới

                var (code, message) = context.AuthenticateFailure switch
                {
                    SecurityTokenExpiredException => ("TOKEN_EXPIRED", "Phiên đăng nhập đã hết hạn, vui lòng đăng nhập lại"),
                    not null => ("TOKEN_INVALID", "Token không hợp lệ"),
                    null => ("UNAUTHORIZED", "Bạn cần đăng nhập để thực hiện thao tác này")
                };

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { code, message });
            },

            // Sắp trả 403: token hợp lệ nhưng sai vai trò
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { code = "FORBIDDEN", message = "Bạn không có quyền thực hiện thao tác này" });
            }
    };
    });


builder.Services.AddAuthorization();

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();
app.UseMiddleware<ExceptionMiddleware>();              // ← thêm, đặt TRƯỚC Swagger và MapControllers

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseAuthentication();                             // ← thêm: kiểm tra token, PHẢI trước UseAuthorization
app.UseAuthorization();
app.MapControllers();

app.Run();
