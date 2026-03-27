namespace TunnelBackend.Infrastructure.Supabase;

public sealed class SupabaseOptions
{
    public const string SectionName = "Supabase";
    public string Url { get; set; } = "";
    public string ServiceRoleKey { get; set; } = "";
}
