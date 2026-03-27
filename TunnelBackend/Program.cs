using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using TunnelBackend.Data;
using TunnelBackend.Features.Orders;
using TunnelBackend.Features.Auth;      // 登录模块
using TunnelBackend.Features.Employees; // 员工模块
using TunnelBackend.Features.Files;     // 文件模块
using TunnelBackend.Infrastructure.ApiKey;
using TunnelBackend.Infrastructure.Passwords;
using TunnelBackend.Infrastructure.Supabase;

var builder = WebApplication.CreateBuilder(args);

// ===================================================
// 1. 【核心修复】JSON 配置 (解决前端传小写后端接不到的问题)
// ===================================================
// 🟢 必须加这段！否则前端传 fileId，后端 FileId 就会是空的
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

// =========================
// 2. 配置选项
// =========================
builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection(ApiKeyOptions.SectionName));
builder.Services.Configure<SupabaseOptions>(builder.Configuration.GetSection(SupabaseOptions.SectionName));

// =========================
// 3. 数据库连接
// =========================
var connStr = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? builder.Configuration.GetConnectionString("Default");

if (string.IsNullOrWhiteSpace(connStr))
    throw new InvalidOperationException("缺少数据库连接字符串，请在 appsettings.json 里配置。");

builder.Services.AddDbContext<AppDbContext>(opt => opt.UseSqlServer(connStr));

// =========================
// 4. 注册服务
// =========================
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddHttpClient<SupabaseClient>();
builder.Services.AddTransient<ApiKeyMiddleware>();

// 上传大小限制 (100MB)
var maxFileBytes = builder.Configuration.GetValue<long?>("Upload:MaxFileBytes") ?? (100L * 1024 * 1024);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxFileBytes);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = maxFileBytes);

// CORS 跨域配置 (允许前端所有请求)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

// =========================
// 5. Swagger 文档配置
// =========================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "TunnelBackend", Version = "v1" });

    options.AddSecurityDefinition("ApiKeyAuth", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        Name = "X-API-KEY",
        In = ParameterLocation.Header,
        Description = "请输入 API Key 进行身份验证"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "ApiKeyAuth"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// =========================
// 6. 中间件管道 (注意顺序)
// =========================

app.UseCors("AllowAll"); // 1. 先允许跨域

// 🟢 2. SPA 入口与静态文件 (前端 + 上传文件)
app.UseDefaultFiles(); // 让 / 默认指向 wwwroot/index.html
app.UseStaticFiles();

app.UseSwagger();
app.UseSwaggerUI();

// 健康检查接口
app.MapGet("/health", () => Results.Ok(new { ok = true, serverTime = DateTimeOffset.Now }));

app.MapGet("/health/db", async (AppDbContext db, CancellationToken ct) =>
{
    try
    {
        if (!await db.Database.CanConnectAsync(ct)) return Results.Problem("无法连接数据库");
        return Results.Ok(new { ok = true, msg = "Database Connected!" });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

// 3. API Key 验证 (放在业务接口之前)
app.UseMiddleware<ApiKeyMiddleware>();

// =========================
// 7. 注册业务接口
// =========================

app.MapFileEndpoints();     // 文件上传
app.MapApsSchedulingEndpoints();// 订单管理
app.MapEmployeeEndpoints(); // 员工管理 (含 update-avatar)
app.MapAuthEndpoints();     // 登录验证

// SPA 前端回退路由 (非 /api 的路径会回到 index.html)
app.MapFallbackToFile("index.html");

app.Run();