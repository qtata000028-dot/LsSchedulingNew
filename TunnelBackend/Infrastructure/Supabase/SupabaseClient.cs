using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TunnelBackend.Infrastructure.Supabase;

public sealed class SupabaseClient
{
    private readonly HttpClient _http;
    private readonly SupabaseOptions _opt;

    public SupabaseClient(HttpClient http, IOptions<SupabaseOptions> opt)
    {
        _http = http;
        _opt = opt.Value;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_opt.Url) && !string.IsNullOrWhiteSpace(_opt.ServiceRoleKey);

    private void EnsureHeaders()
    {
        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("apikey", _opt.ServiceRoleKey);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opt.ServiceRoleKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private Uri Build(string relative)
    {
        var baseUrl = _opt.Url.TrimEnd('/');
        return new Uri($"{baseUrl}/rest/v1/{relative}");
    }

    public async Task<JsonElement?> UpsertCloudflaredEndpointAsync(string name, string publicUrl, string? localTarget, string? note, CancellationToken ct)
    {
        if (!IsConfigured) return null;

        EnsureHeaders();

        var uri = Build("CloudflaredEndpoints?on_conflict=Name&select=*");

        var payload = new[]
        {
            new
            {
                Name = name,
                PublicUrl = publicUrl,
                LocalTarget = localTarget,
                Note = note,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, uri);
        req.Headers.Add("Prefer", "resolution=merge-duplicates,return=representation");
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Supabase upsert failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {body}");

        var doc = JsonDocument.Parse(body);
        return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
            ? doc.RootElement[0]
            : null;
    }

    public async Task<JsonElement?> GetCloudflaredEndpointAsync(string name, CancellationToken ct)
    {
        if (!IsConfigured) return null;

        EnsureHeaders();

        var uri = Build($"CloudflaredEndpoints?select=*&Name=eq.{Uri.EscapeDataString(name)}&limit=1");
        using var resp = await _http.GetAsync(uri, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Supabase query failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {body}");

        var doc = JsonDocument.Parse(body);
        return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
            ? doc.RootElement[0]
            : null;
    }
}
