namespace TunnelBackend.Infrastructure.ApiKey;

public sealed class ApiKeyOptions
{
    // 对应 appsettings.json 里的节点名称
    public const string SectionName = "ApiKeyAuth";

    public bool Enabled { get; set; } = true;
    public string HeaderName { get; set; } = "X-API-KEY";

    // 对应 JSON 里的 "Keys": [] 数组
    public List<string> Keys { get; set; } = new();
}