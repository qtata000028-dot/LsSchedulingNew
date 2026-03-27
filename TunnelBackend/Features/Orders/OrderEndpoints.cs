using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.RegularExpressions;
using TunnelBackend.Data;

namespace TunnelBackend.Features.Orders;

/// <summary>
/// APS 智能排程接口端点
/// 核心逻辑：
/// 1. 维度：按 mc (月度批次) -> 单号 -> 工序 顺序排程
/// 2. 日历：9:00-18:00 (540分钟/天)，自动跳过周日
/// 3. 单位：工时参数为【分钟】，产能参数为【小时】(内部转分钟计算)
/// </summary>
public static class ApsSchedulingEndpoints
{
    public static IEndpointRouteBuilder MapApsSchedulingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/aps/schedule").WithTags("APS Scheduling");

        // ==========================================
        // 1. 获取排程月份列表
        // ==========================================
        group.MapGet("/months", async (AppDbContext db, bool includeAll = false, CancellationToken ct = default) =>
        {
            var connStr = db.Database.GetConnectionString() ?? throw new Exception("数据库连接字符串未配置");

            var sql = @"
                SELECT
                    CAST(dt.yearid AS varchar(4)) + '年' + CAST(dt.monthid AS varchar(2)) + '月' AS mc,
                    (dt.yearid * 100 + dt.monthid) AS mcYm,
                    COUNT(DISTINCT a.crm_lco_billdocument_id) AS OrderCount,
                    COUNT(DISTINCT a.crm_lco_id) AS DetailCount
                FROM Crm_BillcoListtab a
                LEFT JOIN Crm_Billcomaintab d ON a.crm_lco_billdocument_id = d.crm_mco_billdocument_id
                LEFT JOIN (
                    SELECT id, yearid, monthid FROM p_setdttab
                ) dt ON a.crm_lco_str1 = CAST(dt.id AS varchar(50))
                WHERE
                    dt.yearid IS NOT NULL
                    AND (@IncludeAll = 1 OR d.crm_mco_stepover = 1)
                GROUP BY dt.yearid, dt.monthid
                ORDER BY dt.yearid, dt.monthid;
            ";

            var list = new List<object>();
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(ct);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@IncludeAll", includeAll ? 1 : 0);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new
                {
                    mc = reader.GetString(0),
                    mcYm = reader.GetInt32(1),
                    orderCount = reader.GetInt32(2),
                    detailCount = reader.GetInt32(3)
                });
            }
            return Results.Ok(list);
        });

        // ==========================================
        // 2. 核心排程运行接口 (POST)
        // ==========================================
        group.MapPost("/run", async (ApsScheduleRunRequest req, AppDbContext db, CancellationToken ct = default) =>
        {
            var connStr = db.Database.GetConnectionString() ?? throw new Exception("数据库连接字符串未配置");

            var fromYmon = ParseMcToYmonOrNull(req.FromMc);
            var toYmon = ParseMcToYmonOrNull(req.ToMc);

            var today = DateTime.Today;
            var anchor = req.AnchorStart ?? new DateTime(today.Year, today.Month, today.Day, 9, 0, 0);

            // 1. 数据加载
            var rawRows = await LoadFlattenRows(connStr, fromYmon, toYmon, req.IncludeAll, ct);

            // 2. 数据组装 (注意单位转换逻辑在 BuildDetails 中)
            var details = BuildDetails(rawRows, out var buildWarnings);

            // 3. 数据分桶
            var buckets = new SortedDictionary<int, List<DetailModel>>();
            var nullMcDetails = new List<DetailModel>();

            foreach (var d in details)
            {
                if (d.McYm <= 0)
                {
                    nullMcDetails.Add(d);
                    continue;
                }
                if (!buckets.TryGetValue(d.McYm, out var list))
                {
                    list = new List<DetailModel>();
                    buckets[d.McYm] = list;
                }
                list.Add(d);
            }

            // 4. 开始排程
            var scheduler = new ApsScheduler(anchor);
            var segments = new List<ApsScheduleSegment>(capacity: Math.Max(2048, rawRows.Count));
            var warnings = new List<ApsScheduleWarning>();

            warnings.AddRange(buildWarnings);

            foreach (var nd in nullMcDetails)
                warnings.Add(ApsScheduleWarning.Warn(nd, "mc 为空，该明细不参与排程"));

            foreach (var kv in buckets)
            {
                var monthYmon = kv.Key;
                var list = kv.Value;

                // 排序策略
                list.Sort((a, b) =>
                {
                    var c1 = string.Compare(a.BillNo, b.BillNo, StringComparison.OrdinalIgnoreCase);
                    if (c1 != 0) return c1;
                    return a.DetailId.CompareTo(b.DetailId);
                });

                var monthStart = YmonToMonthStart(monthYmon);
                if (monthStart < anchor) monthStart = anchor;

                var must = new List<DetailModel>();
                var movable = new List<DetailModel>();

                foreach (var d in list)
                {
                    var dueYm = d.DueTime.Year * 100 + d.DueTime.Month;
                    if (dueYm > monthYmon) movable.Add(d);
                    else must.Add(d);
                }

                foreach (var d in must)
                    ScheduleOneDetailOrWarn(scheduler, d, monthStart, segments, warnings, allowRollbackToDueMonth: false);

                foreach (var d in movable)
                {
                    var dueYm = d.DueTime.Year * 100 + d.DueTime.Month;
                    var segStartIndex = segments.Count;
                    var snapshot = scheduler.SnapshotFor(d);

                    var endTime = ScheduleOneDetailOrWarn(scheduler, d, monthStart, segments, warnings, allowRollbackToDueMonth: true);

                    if (endTime.HasValue)
                    {
                        var lateLimit = d.DueTime.Date.AddDays(2).AddHours(18);
                        if (endTime.Value > lateLimit)
                        {
                            // 回滚逻辑
                            scheduler.Restore(snapshot);
                            if (segments.Count > segStartIndex)
                                segments.RemoveRange(segStartIndex, segments.Count - segStartIndex);

                            if (!buckets.TryGetValue(dueYm, out var dueList))
                            {
                                dueList = new List<DetailModel>();
                                buckets[dueYm] = dueList;
                            }
                            dueList.Add(d);

                            warnings.Add(ApsScheduleWarning.Info(d, $"可后移明细（Due={d.DueTime:yyyy-MM-dd}），本月排程超期，已挪至 {dueYm}"));
                        }
                    }
                }
            }

            segments.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            return Results.Ok(new
            {
                req.FromMc,
                req.ToMc,
                anchorStart = anchor,
                segmentCount = segments.Count,
                warningCount = warnings.Count,
                // 必须添加下面这一行，前端才能拿到产品名称！
                details = details,
                segments,
                warnings
            });
        });

        return app;
    }

    // ============================================================
    // 内部逻辑方法
    // ============================================================

    private static async Task<List<FlatRow>> LoadFlattenRows(string connStr, int? fromYmon, int? toYmon, bool includeAll, CancellationToken ct)
    {
        var sql = @"
            SELECT
                CAST(dt.yearid AS varchar(4)) + '年' + CAST(dt.monthid AS varchar(2)) + '月' AS mc,
                (dt.yearid * 100 + dt.monthid) AS mcYm,

                a.crm_lco_billdocument_id AS BillNo,
                a.crm_lco_id AS DetailId,
                a.crm_lco_tm AS LineNo1,
                ISNULL(a.crm_lco_planamount, ISNULL(a.crm_lco_amount, 0)) AS PlanQty,
                a.crm_lco_productid AS ProductId,
                ISNULL(a.crm_lco_buytime, GETDATE()) AS DueTime,

                c.CoName,
                b.appellation,
                b.spec,
                b.model,
                b.ProductDescrip,
                b.Productunitname,

                p.mes_prolist_no AS ProcessNo,
                p.mes_prolist_name AS ProcessName,

                -- 【单位变更】数据库里存的现在是【分钟】
                ISNULL(p.mes_prolist_manhour, 0) AS PieceManMinute, 
                ISNULL(p.mes_prolist_manhour01, 0) AS PrepMinute,
                ISNULL(p.mes_prolist_manhour02, 0) AS DebugMinute,

                -- 【单位保持】产能存的依然是【小时】
                ISNULL(cap.MCapacity, 100) AS MCapacityHour,
                ISNULL(cap.michamount, 1) AS Michamount,
                ISNULL(cap.Capacityrate, 1) AS Capacityrate

            FROM Crm_BillcoListtab a
            LEFT JOIN p_ProductTab b ON a.crm_lco_productid = b.ProductId
            LEFT JOIN P_CustomerTab c ON a.crm_lco_clientid = c.id
            LEFT JOIN Crm_Billcomaintab d ON a.crm_lco_billdocument_id = d.crm_mco_billdocument_id
            LEFT JOIN (SELECT id, yearid, monthid FROM p_setdttab) dt ON a.crm_lco_str1 = CAST(dt.id AS varchar(50))
            LEFT JOIN mes_ProcessListTab p ON p.mes_prolist_ProductId = a.crm_lco_productid
            LEFT JOIN (
                SELECT
                    mes_list_no,
                    MAX(ISNULL(MCapacity, 100)) AS MCapacity,
                    MAX(ISNULL(michamount, 1)) AS michamount,
                    MAX(ISNULL(Capacityrate, 1)) AS Capacityrate
                FROM mes_fl_listtab
                WHERE speciesno LIKE '01%'
                GROUP BY mes_list_no
            ) cap ON cap.mes_list_no = p.mes_prolist_no

            WHERE
                dt.yearid IS NOT NULL
                AND (@IncludeAll = 1 OR d.crm_mco_stepover = 1)
                AND (@FromYm IS NULL OR (dt.yearid*100 + dt.monthid) >= @FromYm)
                AND (@ToYm   IS NULL OR (dt.yearid*100 + dt.monthid) <= @ToYm)

            ORDER BY
                dt.yearid, dt.monthid, a.crm_lco_billdocument_id, a.crm_lco_id, p.mes_prolist_no;
        ";

        var rows = new List<FlatRow>();
        using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@IncludeAll", includeAll ? 1 : 0);
        cmd.Parameters.AddWithValue("@FromYm", (object?)fromYmon ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ToYm", (object?)toYmon ?? DBNull.Value);

        using var reader = await cmd.ExecuteReaderAsync(ct);

        int o_mc = reader.GetOrdinal("mc");
        int o_mcYm = reader.GetOrdinal("mcYm");
        int o_bill = reader.GetOrdinal("BillNo");
        int o_detail = reader.GetOrdinal("DetailId");
        int o_line = reader.GetOrdinal("LineNo1");
        int o_qty = reader.GetOrdinal("PlanQty");
        int o_pid = reader.GetOrdinal("ProductId");
        int o_due = reader.GetOrdinal("DueTime");
        int o_coname = reader.GetOrdinal("CoName");
        int o_app = reader.GetOrdinal("appellation");
        int o_spec = reader.GetOrdinal("spec");
        int o_model = reader.GetOrdinal("model");
        int o_des = reader.GetOrdinal("ProductDescrip");
        int o_unit = reader.GetOrdinal("Productunitname");
        int o_pno = reader.GetOrdinal("ProcessNo");
        int o_pname = reader.GetOrdinal("ProcessName");

        // 关键列名变更：PieceManMinute, PrepMinute, DebugMinute
        int o_piece = reader.GetOrdinal("PieceManMinute");
        int o_prep = reader.GetOrdinal("PrepMinute");
        int o_debug = reader.GetOrdinal("DebugMinute");

        int o_cap = reader.GetOrdinal("MCapacityHour");
        int o_mich = reader.GetOrdinal("Michamount");
        int o_rate = reader.GetOrdinal("Capacityrate");

        while (await reader.ReadAsync(ct))
        {
            rows.Add(new FlatRow
            {
                Mc = reader.IsDBNull(o_mc) ? "" : reader.GetString(o_mc),
                McYm = reader.IsDBNull(o_mcYm) ? 0 : reader.GetInt32(o_mcYm),
                BillNo = reader.IsDBNull(o_bill) ? "" : reader.GetString(o_bill),
                DetailId = reader.IsDBNull(o_detail) ? 0 : reader.GetInt32(o_detail),
                LineNo1 = reader.IsDBNull(o_line) ? "" : reader.GetString(o_line),
                PlanQty = reader.IsDBNull(o_qty) ? 0m : reader.GetDecimal(o_qty),
                ProductId = reader.IsDBNull(o_pid) ? "" : reader.GetString(o_pid),
                DueTime = reader.IsDBNull(o_due) ? DateTime.Now : reader.GetDateTime(o_due),
                CoName = reader.IsDBNull(o_coname) ? "" : reader.GetString(o_coname),
                Appellation = reader.IsDBNull(o_app) ? "" : reader.GetString(o_app),
                Spec = reader.IsDBNull(o_spec) ? "" : reader.GetString(o_spec),
                Model = reader.IsDBNull(o_model) ? "" : reader.GetString(o_model),
                ProductDescrip = reader.IsDBNull(o_des) ? "" : reader.GetString(o_des),
                ProductUnitName = reader.IsDBNull(o_unit) ? "" : reader.GetString(o_unit),
                ProcessNo = reader.IsDBNull(o_pno) ? "" : reader.GetString(o_pno),
                ProcessName = reader.IsDBNull(o_pname) ? "" : reader.GetString(o_pname),

                // 读取分钟
                PieceManMinute = reader.IsDBNull(o_piece) ? 0m : reader.GetDecimal(o_piece),
                PrepMinute = reader.IsDBNull(o_prep) ? 0m : reader.GetDecimal(o_prep),
                DebugMinute = reader.IsDBNull(o_debug) ? 0m : reader.GetDecimal(o_debug),

                // 读取小时
                MCapacityHour = reader.IsDBNull(o_cap) ? 100m : Convert.ToDecimal(reader.GetValue(o_cap)),
                Michamount = reader.IsDBNull(o_mich) ? 1 : Convert.ToInt32(reader.GetValue(o_mich)),
                Capacityrate = reader.IsDBNull(o_rate) ? 1m : Convert.ToDecimal(reader.GetValue(o_rate)),
            });
        }
        return rows;
    }

    private static List<DetailModel> BuildDetails(List<FlatRow> rows, out List<ApsScheduleWarning> warnings)
    {
        warnings = new List<ApsScheduleWarning>();
        var dict = new Dictionary<int, DetailModel>();

        foreach (var r in rows)
        {
            if (!dict.TryGetValue(r.DetailId, out var d))
            {
                d = new DetailModel
                {
                    Mc = r.Mc,
                    McYm = r.McYm,
                    BillNo = r.BillNo,
                    DetailId = r.DetailId,
                    LineNo1 = r.LineNo1,
                    PlanQty = r.PlanQty,
                    ProductId = r.ProductId,
                    DueTime = r.DueTime,
                    CoName = r.CoName,
                    Appellation = r.Appellation,
                    Spec = r.Spec,
                    Model = r.Model,
                    ProductDescrip = r.ProductDescrip,
                    ProductUnitName = r.ProductUnitName,
                };
                dict[r.DetailId] = d;
            }

            if (string.IsNullOrWhiteSpace(r.ProcessNo))
                continue;

            d.Processes.Add(new ProcessStepModel
            {
                ProcessNo = r.ProcessNo,
                ProcessName = r.ProcessName,
                // 直接存分钟，转 double
                PieceManMinute = (double)r.PieceManMinute,
                PrepMinute = (double)r.PrepMinute,
                DebugMinute = (double)r.DebugMinute,

                // 产能是小时，转分钟（方便后续统一计算）
                MCapacityMinute = (double)r.MCapacityHour * 60.0,

                MachineCount = Math.Max(1, r.Michamount),
                CapacityRate = (double)r.Capacityrate
            });
        }

        var list = dict.Values.ToList();

        foreach (var d in list)
        {
            d.Processes.Sort((a, b) => string.Compare(a.ProcessNo, b.ProcessNo, StringComparison.OrdinalIgnoreCase));

            if (d.Processes.Count == 0)
            {
                d.Processes.Add(new ProcessStepModel
                {
                    ProcessNo = "MISSING",
                    ProcessName = "❌ 工艺缺失",
                    PieceManMinute = 0,
                    PrepMinute = 0,
                    DebugMinute = 0,
                    MCapacityMinute = 6000,
                    MachineCount = 1,
                    CapacityRate = 1
                });
                warnings.Add(ApsScheduleWarning.Error(d, $"ProductId={d.ProductId} 在 mes_ProcessListTab 查不到路线，已生成占位块"));
            }
            else
            {
                foreach (var p in d.Processes)
                {
                    // 校验是否全0（注意单位是分钟）
                    if (p.PieceManMinute <= 0 && p.PrepMinute <= 0 && p.DebugMinute <= 0)
                        warnings.Add(ApsScheduleWarning.Warn(d, $"工序[{p.ProcessName}/{p.ProcessNo}] 工时参数全0(单位:分钟)，将按 1 分钟兜底"));
                }
            }
        }
        return list;
    }

    private static DateTime? ScheduleOneDetailOrWarn(
        ApsScheduler scheduler,
        DetailModel d,
        DateTime monthStart,
        List<ApsScheduleSegment> segments,
        List<ApsScheduleWarning> warnings,
        bool allowRollbackToDueMonth)
    {
        if (d.Processes.Count == 0) return null;

        var qty = (double)d.PlanQty;
        if (qty < 0) qty = 0;

        DateTime cur = monthStart;

        foreach (var step in d.Processes)
        {
            int totalMinutes;
            if (step.ProcessNo == "MISSING")
            {
                totalMinutes = 30;
            }
            else
            {
                // ★★★ 核心公式修改：因为输入已经是分钟了，所以不需要再乘 60 ★★★
                // 总工时(分) = 单件(分) * 数量 + 准备(分) + 调试(分)
                var total = step.PieceManMinute * qty + step.PrepMinute + step.DebugMinute;
                totalMinutes = (int)Math.Ceiling(total);
            }

            if (totalMinutes <= 0) totalMinutes = 1;

            var segs = scheduler.Schedule(
                d, step,
                earliest: cur,
                notBefore: monthStart,
                durationMinutes: totalMinutes,
                out var opEnd);

            segments.AddRange(segs);
            cur = opEnd;
        }

        var lateLimit = d.DueTime.Date.AddDays(2).AddHours(18);
        if (cur > lateLimit)
        {
            if (!allowRollbackToDueMonth)
                warnings.Add(ApsScheduleWarning.Error(d, $"严重超期：完成时间 {cur:yyyy-MM-dd HH:mm} > 考核线 {lateLimit:yyyy-MM-dd HH:mm}"));
        }

        return cur;
    }

    private static int? ParseMcToYmonOrNull(string? mc)
    {
        if (string.IsNullOrWhiteSpace(mc)) return null;
        var s = mc.Trim();
        var m = Regex.Match(s, @"(?<y>\d{4})\D+(?<m>\d{1,2})");
        if (!m.Success) return null;
        int y = int.Parse(m.Groups["y"].Value);
        int mo = int.Parse(m.Groups["m"].Value);
        if (mo < 1 || mo > 12) return null;
        return y * 100 + mo;
    }

    private static DateTime YmonToMonthStart(int ymon)
    {
        int y = ymon / 100;
        int m = ymon % 100;
        return new DateTime(y, m, 1, 9, 0, 0);
    }

    // ============================================================
    // DTOs (字段名已更新以反映单位)
    // ============================================================
    public class ApsScheduleRunRequest
    {
        public string? FromMc { get; set; }
        public string? ToMc { get; set; }
        public DateTime? AnchorStart { get; set; }
        public bool IncludeAll { get; set; } = false;
    }

    private sealed class FlatRow
    {
        public string Mc { get; set; } = "";
        public int McYm { get; set; }
        public string BillNo { get; set; } = "";
        public int DetailId { get; set; }
        public string LineNo1 { get; set; } = "";
        public decimal PlanQty { get; set; }
        public string ProductId { get; set; } = "";
        public DateTime DueTime { get; set; }
        public string CoName { get; set; } = "";
        public string Appellation { get; set; } = "";
        public string Spec { get; set; } = "";
        public string Model { get; set; } = "";
        public string ProductDescrip { get; set; } = "";
        public string ProductUnitName { get; set; } = "";
        public string ProcessNo { get; set; } = "";
        public string ProcessName { get; set; } = "";

        // 已改为 Minute
        public decimal PieceManMinute { get; set; }
        public decimal PrepMinute { get; set; }
        public decimal DebugMinute { get; set; }

        // 依然是 Hour
        public decimal MCapacityHour { get; set; }

        public int Michamount { get; set; }
        public decimal Capacityrate { get; set; }
    }

    public sealed class DetailModel
    {
        public string Mc { get; set; } = "";
        public int McYm { get; set; }
        public string BillNo { get; set; } = "";
        public int DetailId { get; set; }
        public string LineNo1 { get; set; } = "";
        public decimal PlanQty { get; set; }
        public string ProductId { get; set; } = "";
        public DateTime DueTime { get; set; }
        public string CoName { get; set; } = "";
        public string Appellation { get; set; } = "";
        public string Spec { get; set; } = "";
        public string Model { get; set; } = "";
        public string ProductDescrip { get; set; } = "";
        public string ProductUnitName { get; set; } = "";
        public List<ProcessStepModel> Processes { get; } = new();
    }

    public sealed class ProcessStepModel
    {
        public string ProcessNo { get; set; } = "";
        public string ProcessName { get; set; } = "";

        // 单位：分钟
        public double PieceManMinute { get; set; }
        public double PrepMinute { get; set; }
        public double DebugMinute { get; set; }

        // 单位：分钟 (由小时*60转化而来)
        public double MCapacityMinute { get; set; }

        public int MachineCount { get; set; }
        public double CapacityRate { get; set; }
    }

    public sealed class ApsScheduleSegment
    {
        public string Mc { get; set; } = "";
        public int McYm { get; set; }
        public string BillNo { get; set; } = "";
        public int DetailId { get; set; }
        public string LineNo1 { get; set; } = "";
        public string ProductId { get; set; } = "";
        public DateTime DueTime { get; set; }
        public string ProcessNo { get; set; } = "";
        public string ProcessName { get; set; } = "";
        public int MachineIndex { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int Minutes { get; set; }
    }

    public sealed class ApsScheduleWarning
    {
        public string Level { get; set; } = "WARN";
        public string Mc { get; set; } = "";
        public string BillNo { get; set; } = "";
        public int DetailId { get; set; }
        public string LineNo1 { get; set; } = "";
        public string ProductId { get; set; } = "";
        public DateTime DueTime { get; set; }
        public string Message { get; set; } = "";

        public static ApsScheduleWarning Info(DetailModel d, string msg) => Create("INFO", d, msg);
        public static ApsScheduleWarning Warn(DetailModel d, string msg) => Create("WARN", d, msg);
        public static ApsScheduleWarning Error(DetailModel d, string msg) => Create("ERROR", d, msg);

        private static ApsScheduleWarning Create(string lv, DetailModel d, string msg) => new()
        {
            Level = lv,
            Mc = d.Mc,
            BillNo = d.BillNo,
            DetailId = d.DetailId,
            LineNo1 = d.LineNo1,
            ProductId = d.ProductId,
            DueTime = d.DueTime,
            Message = msg
        };
    }

    private sealed class ApsScheduler
    {
        private readonly DateTime _globalAnchor;
        private readonly Dictionary<string, ProcessResource> _resources = new(StringComparer.OrdinalIgnoreCase);

        public ApsScheduler(DateTime anchorStart)
        {
            _globalAnchor = new DateTime(anchorStart.Year, anchorStart.Month, anchorStart.Day, anchorStart.Hour, anchorStart.Minute, 0);
        }

        private bool IsWorkDay(DateTime dt)
        {
            if (dt.DayOfWeek == DayOfWeek.Sunday) return false;
            return true;
        }

        public SchedulerSnapshot SnapshotFor(DetailModel d)
        {
            var used = d.Processes.Select(p => p.ProcessNo).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var dict = new Dictionary<string, DateTime[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var pno in used)
            {
                if (_resources.TryGetValue(pno, out var res))
                    dict[pno] = res.MachineAvailable.ToArray();
            }
            return new SchedulerSnapshot(dict);
        }

        public void Restore(SchedulerSnapshot snap)
        {
            foreach (var kv in snap.AvailableTimes)
            {
                if (_resources.TryGetValue(kv.Key, out var res))
                {
                    var arr = kv.Value;
                    var n = Math.Min(res.MachineAvailable.Count, arr.Length);
                    for (int i = 0; i < n; i++) res.MachineAvailable[i] = arr[i];
                }
            }
        }

        public List<ApsScheduleSegment> Schedule(
            DetailModel d,
            ProcessStepModel step,
            DateTime earliest,
            DateTime notBefore,
            int durationMinutes,
            out DateTime opEnd)
        {
            var res = GetOrCreateResource(step);
            var hardNotBefore = notBefore;
            if (hardNotBefore < _globalAnchor) hardNotBefore = _globalAnchor;
            if (earliest < hardNotBefore) earliest = hardNotBefore;

            int bestIdx = 0;
            DateTime bestFinish = DateTime.MaxValue;
            DateTime bestStart = DateTime.MaxValue;

            for (int i = 0; i < res.MachineAvailable.Count; i++)
            {
                var cand = res.MachineAvailable[i];
                if (cand < earliest) cand = earliest;
                cand = AlignToWorkWindow(cand, res.PerMachineWorkMinutes);
                var finish = SimulateFinish(cand, durationMinutes, res.PerMachineWorkMinutes);
                if (finish < bestFinish)
                {
                    bestFinish = finish;
                    bestStart = cand;
                    bestIdx = i;
                }
            }

            var segs = new List<ApsScheduleSegment>();
            int remain = durationMinutes;
            var cur = bestStart;

            while (remain > 0)
            {
                cur = AlignToWorkWindow(cur, res.PerMachineWorkMinutes);
                var dayStart = new DateTime(cur.Year, cur.Month, cur.Day, 9, 0, 0);
                var dayEnd = dayStart.AddMinutes(res.PerMachineWorkMinutes);
                var canUse = (int)Math.Floor((dayEnd - cur).TotalMinutes);

                if (canUse <= 0)
                {
                    cur = dayStart.AddDays(1);
                    continue;
                }

                var chunk = Math.Min(remain, canUse);
                var end = cur.AddMinutes(chunk);

                segs.Add(new ApsScheduleSegment
                {
                    Mc = d.Mc,
                    McYm = d.McYm,
                    BillNo = d.BillNo,
                    DetailId = d.DetailId,
                    LineNo1 = d.LineNo1,
                    ProductId = d.ProductId,
                    DueTime = d.DueTime,
                    ProcessNo = step.ProcessNo,
                    ProcessName = step.ProcessName,
                    MachineIndex = bestIdx + 1,
                    StartTime = cur,
                    EndTime = end,
                    Minutes = chunk
                });

                remain -= chunk;
                cur = end;
                if (remain > 0) cur = new DateTime(cur.Year, cur.Month, cur.Day, 9, 0, 0).AddDays(1);
            }

            opEnd = cur;
            res.MachineAvailable[bestIdx] = opEnd;
            return segs;
        }

        private ProcessResource GetOrCreateResource(ProcessStepModel step)
        {
            if (!_resources.TryGetValue(step.ProcessNo, out var res))
            {
                res = new ProcessResource { ProcessNo = step.ProcessNo, ProcessName = step.ProcessName };
                _resources[step.ProcessNo] = res;
            }

            var mc = Math.Max(1, step.MachineCount);

            // ★★★ 核心修改：将小时产能转为分钟 ★★★
            // 旧：capHours * 60
            // 新：step.MCapacityMinute (已经在 BuildDetails 乘过 60 了)
            var capMinutes = step.MCapacityMinute <= 0 ? 6000 : step.MCapacityMinute; // 兜底给个大数

            var rate = step.CapacityRate <= 0 ? 1 : step.CapacityRate;

            var perMachineMinutes = (int)Math.Ceiling(capMinutes * rate);

            // 9:00 - 18:00 = 9小时 = 540分钟
            if (perMachineMinutes > 540) perMachineMinutes = 540;
            if (perMachineMinutes <= 0) perMachineMinutes = 540;

            if (res.PerMachineWorkMinutes <= 0) res.PerMachineWorkMinutes = perMachineMinutes;
            else res.PerMachineWorkMinutes = Math.Min(res.PerMachineWorkMinutes, perMachineMinutes);

            while (res.MachineAvailable.Count < mc) res.MachineAvailable.Add(_globalAnchor);
            return res;
        }

        private DateTime AlignToWorkWindow(DateTime dt, int perMachineMinutes)
        {
            while (true)
            {
                if (!IsWorkDay(dt))
                {
                    dt = new DateTime(dt.Year, dt.Month, dt.Day, 9, 0, 0).AddDays(1);
                    continue;
                }
                var dayStart = new DateTime(dt.Year, dt.Month, dt.Day, 9, 0, 0);
                var dayEnd = dayStart.AddMinutes(perMachineMinutes);
                if (dt < dayStart) return dayStart;
                if (dt >= dayEnd)
                {
                    dt = dayStart.AddDays(1);
                    continue;
                }
                return dt;
            }
        }

        private DateTime SimulateFinish(DateTime start, int minutes, int perMachineMinutes)
        {
            var cur = start;
            var remain = minutes;
            while (remain > 0)
            {
                cur = AlignToWorkWindow(cur, perMachineMinutes);
                var dayStart = new DateTime(cur.Year, cur.Month, cur.Day, 9, 0, 0);
                var dayEnd = dayStart.AddMinutes(perMachineMinutes);
                if (cur >= dayEnd)
                {
                    cur = dayStart.AddDays(1);
                    continue;
                }
                var canUse = (int)Math.Floor((dayEnd - cur).TotalMinutes);
                if (canUse <= 0)
                {
                    cur = dayStart.AddDays(1);
                    continue;
                }
                var chunk = Math.Min(remain, canUse);
                cur = cur.AddMinutes(chunk);
                remain -= chunk;
                if (remain > 0) cur = dayStart.AddDays(1);
            }
            return cur;
        }

        private sealed class ProcessResource
        {
            public string ProcessNo { get; set; } = "";
            public string ProcessName { get; set; } = "";
            public int PerMachineWorkMinutes { get; set; } = 0;
            public List<DateTime> MachineAvailable { get; } = new();
        }
    }

    private sealed class SchedulerSnapshot
    {
        public Dictionary<string, DateTime[]> AvailableTimes { get; }
        public SchedulerSnapshot(Dictionary<string, DateTime[]> dict) => AvailableTimes = dict;
    }
}