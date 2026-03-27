using Microsoft.Extensions.Options;

namespace TunnelBackend.Infrastructure.ApiKey;

public sealed class ApiKeyMiddleware : IMiddleware
{
    private readonly ApiKeyOptions _opt;

    public ApiKeyMiddleware(IOptions<ApiKeyOptions> opt) => _opt = opt.Value;

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var path = context.Request.Path.Value ?? "";

        // 1. 跳过 Swagger 和 健康检查
        if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // 2. 跳过非 API 请求 (比如 /uploads/xxx.jpg 图片)
        // ✅ 关键点：图片路径通常是 /uploads/...，不以 /api 开头，所以这里直接放行
        if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // 3. 放行 CORS 预检请求 (OPTIONS)
        // ✅ 必须放行，否则前端发不出 POST/PUT
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            await next(context);
            return;
        }

        // 4. 检查全局开关
        if (!_opt.Enabled)
        {
            await next(context);
            return;
        }

        // 5. 检查配置是否正确 (防止配置文件没写 Key)
        if (_opt.Keys is null || _opt.Keys.Count == 0)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { ok = false, error = "Server Config Error: No API Keys configured." });
            return;
        }

        // 6. 检查 Header 是否携带 Key
        if (!context.Request.Headers.TryGetValue(_opt.HeaderName, out var key) ||
            string.IsNullOrWhiteSpace(key))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { ok = false, error = $"Missing header: {_opt.HeaderName}" });
            return;
        }

        // 7. 验证 Key 是否匹配 (比对 appsettings.json 里的 Keys 列表)
        var k = key.ToString().Trim();
        if (!_opt.Keys.Contains(k))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { ok = false, error = "Invalid API Key." });
            return;
        }

        // 验证通过，放行！
        await next(context);
    }
}