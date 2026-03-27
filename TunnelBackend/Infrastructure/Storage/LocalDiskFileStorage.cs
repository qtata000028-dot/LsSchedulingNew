
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace TunnelBackend.Infrastructure.Storage;

public sealed class LocalDiskFileStorage : IFileStorage
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _cfg;

    public LocalDiskFileStorage(IWebHostEnvironment env, IConfiguration cfg)
    {
        _env = env;
        _cfg = cfg;
    }

    public async Task<string> SaveAsync(Stream stream, string originalFileName, CancellationToken ct)
    {
        var root = _cfg.GetValue<string>("Upload:Root") ?? "App_Data/uploads";
        var absRoot = Path.Combine(_env.ContentRootPath, root);

        var now = DateTimeOffset.Now;
        var folder = Path.Combine(now.Year.ToString("0000"), now.Month.ToString("00"), now.Day.ToString("00"));
        var absFolder = Path.Combine(absRoot, folder);
        Directory.CreateDirectory(absFolder);

        var safeName = MakeSafeFileName(originalFileName);
        var storedName = $"{Guid.NewGuid():N}_{safeName}";

        var absPath = Path.Combine(absFolder, storedName);
        await using (var fs = new FileStream(absPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await stream.CopyToAsync(fs, ct);
        }

        return Path.Combine(folder, storedName).Replace('\\', '/');
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct)
    {
        var root = _cfg.GetValue<string>("Upload:Root") ?? "App_Data/uploads";
        var absRoot = Path.Combine(_env.ContentRootPath, root);
        var abs = Path.Combine(absRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(abs))
            throw new FileNotFoundException("File not found.", abs);

        Stream s = new FileStream(abs, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(s);
    }

    private static string MakeSafeFileName(string input)
    {
        var name = Path.GetFileName(input);
        // 允许中英文、数字、常见符号，其它替换为 _
        name = Regex.Replace(name, @"[^a-zA-Z0-9._\-\u4e00-\u9fa5]", "_");
        if (string.IsNullOrWhiteSpace(name)) name = "file.bin";
        if (name.Length > 120) name = name[..120];
        return name;
    }
}
