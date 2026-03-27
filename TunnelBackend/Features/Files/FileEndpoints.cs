using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TunnelBackend.Data;
using TunnelBackend.Features.Entities;

namespace TunnelBackend.Features.Files;

public static class FileEndpoints
{
    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/files").WithTags("Files");

        group.MapPost("/", async (
            IFormFile file,
            AppDbContext db,
            [FromServices] IWebHostEnvironment env,
            CancellationToken ct) =>
        {
            // 1. 基础校验
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "请选择文件" });

            if (file.Length > 100 * 1024 * 1024)
                return Results.BadRequest(new { error = "文件大小不能超过 100MB" });

            // 2. 智能计算路径
            var webRoot = env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var uploadPath = Path.Combine(webRoot, "uploads");
            if (!Directory.Exists(uploadPath)) Directory.CreateDirectory(uploadPath);

            // 3. 生成安全文件名：时间戳_原文件名（清洗）
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");

            var safeOriginalName = Path.GetFileName(file.FileName); // 防止路径遍历
            safeOriginalName = SanitizeFileName(safeOriginalName);  // 进一步清洗特殊字符

            var newFileName = $"{timestamp}_{safeOriginalName}";
            var filePath = Path.Combine(uploadPath, newFileName);

            // 4. 保存文件到硬盘
            await using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await file.CopyToAsync(stream, ct);
            }

            // 5. 记录到数据库 (修正：使用 Sys_FileRecord)
            var record = new Sys_FileRecord
            {
                Id = Guid.NewGuid(),
                FileName = file.FileName,       // 原始文件名 (显示用)
                StoredFileName = newFileName,   // 存储文件名 (带时间戳)
                ContentType = file.ContentType,
                FileSize = file.Length,
                UploadTime = DateTime.Now,
                UploaderId = "System"
            };

            // ✅ 不依赖 db.Sys_FileRecords
            db.Set<Sys_FileRecord>().Add(record);
            await db.SaveChangesAsync(ct);

            // 6. 返回结果
            var fileUrl = $"/uploads/{newFileName}";

            return Results.Ok(new
            {
                record.Id,
                Url = fileUrl,
                OriginalName = record.FileName
            });
        }).DisableAntiforgery();

        return app;
    }

    /// <summary>
    /// 文件名清洗：去掉非法字符，避免 Windows 文件系统报错
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "file";

        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        // 额外去掉一些容易出事的字符
        name = name.Replace("..", "_").Replace("/", "_").Replace("\\", "_");

        return name;
    }
}