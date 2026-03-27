using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TunnelBackend.Data;


namespace TunnelBackend.Features.SqlGateway;

public static class SqlGatewayEndpoints
{
    public sealed record SqlQueryRequest(string Sql, Dictionary<string, object?>? Parameters, int? Take);

    public static IEndpointRouteBuilder MapSqlGatewayEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sql").WithTags("SqlGateway");

        group.MapPost("/query", async (SqlQueryRequest req, IConfiguration cfg, AppDbContext db, CancellationToken ct) =>
        {
            var enabled = cfg.GetValue<bool>("SqlGateway:Enabled");
            if (!enabled) return Results.StatusCode(StatusCodes.Status403Forbidden);

            var sql = (req.Sql ?? "").Trim();
            if (string.IsNullOrWhiteSpace(sql)) return Results.BadRequest("Sql is empty.");

            var maxLen = cfg.GetValue<int?>("SqlGateway:MaxSqlLength") ?? 8000;
            if (sql.Length > maxLen) return Results.BadRequest($"Sql too long (>{maxLen}).");

            if (!Regex.IsMatch(sql, @"^\s*(select|with)\b", RegexOptions.IgnoreCase))
                return Results.BadRequest("Only SELECT/WITH queries are allowed.");

            if (sql.Contains(';'))
                return Results.BadRequest("Multi-statement is not allowed.");

            if (HasDangerousKeyword(sql))
                return Results.BadRequest("Dangerous sql detected.");

            var maxRows = cfg.GetValue<int?>("SqlGateway:MaxRows") ?? 2000;
            var take = Math.Clamp(req.Take ?? maxRows, 1, maxRows);

            var conn = db.Database.GetDbConnection();
            await EnsureOpenAsync(conn, ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 30;

            if (req.Parameters is not null)
            {
                foreach (var kv in req.Parameters)
                {
                    var p = cmd.CreateParameter();
                    p.ParameterName = kv.Key.StartsWith("@") ? kv.Key : "@" + kv.Key;
                    p.Value = kv.Value ?? DBNull.Value;
                    cmd.Parameters.Add(p);
                }
            }

            var rows = new List<Dictionary<string, object?>>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            var fieldCount = reader.FieldCount;
            var names = new string[fieldCount];
            for (int i = 0; i < fieldCount; i++) names[i] = reader.GetName(i);

            int count = 0;
            while (await reader.ReadAsync(ct))
            {
                var row = new Dictionary<string, object?>(fieldCount, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < fieldCount; i++)
                    row[names[i]] = await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i);

                rows.Add(row);
                if (++count >= take) break;
            }

            return Results.Ok(new { ok = true, take, rows });
        });

        return app;
    }

    private static async Task EnsureOpenAsync(DbConnection conn, CancellationToken ct)
    {
        if (conn.State == System.Data.ConnectionState.Open) return;
        await conn.OpenAsync(ct);
    }

    private static bool HasDangerousKeyword(string sql)
    {
        var bad = new[] { "insert", "update", "delete", "merge", "drop", "alter", "create", "truncate", "exec", "execute", "sp_", "xp_", "grant", "revoke", "deny" };
        var stripped = Regex.Replace(sql, @"'([^']|'')*'", "''");
        return bad.Any(k => Regex.IsMatch(stripped, @"\b" + Regex.Escape(k) + @"\b", RegexOptions.IgnoreCase));
    }
}
