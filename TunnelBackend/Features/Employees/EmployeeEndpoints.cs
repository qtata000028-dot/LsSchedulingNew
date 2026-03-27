using Microsoft.EntityFrameworkCore;
using TunnelBackend.Data;
using TunnelBackend.Features.Entities;

namespace TunnelBackend.Features.Employees;

public static class EmployeeEndpoints
{
    public static IEndpointRouteBuilder MapEmployeeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/employees").WithTags("Employees");

        // ==========================================
        // 1. 更新员工头像接口 (POST)
        // ==========================================
        group.MapPost("/update-avatar", async (UpdateAvatarRequest req, AppDbContext db, CancellationToken ct) =>
        {
            Console.WriteLine($"[Debug] 收到头像更新请求 -> EmpId: '{req.EmpId}', FileId: '{req.FileId}', URL: '{req.FileUrl}'");

            if (string.IsNullOrWhiteSpace(req.EmpId))
                return Results.BadRequest(new { error = "员工 ID 不能为空" });

            string empKey = req.EmpId.Trim();
            bool hasIntId = int.TryParse(empKey, out int empIdInt);

            // ✅ 不依赖 db.P_EmployeeTabs，直接用 Set<T>
            var emp = await db.Set<P_EmployeeTab>().FirstOrDefaultAsync(x =>
                (hasIntId && x.employeeid == empIdInt) || // 修正: Employeeid -> employeeid
                x.LoginAccount == empKey ||               // 保持不变
                x.p_emp_no == empKey ||                   // 修正: PEmpNo -> p_emp_no
                x.EmployeeName == empKey,                 // 保持不变
                ct);

            if (emp is null)
            {
                Console.WriteLine($"[Debug] ❌ 未找到员工: '{empKey}'");
                return Results.NotFound(new { error = "找不到该员工" });
            }

            // A. 更新 Guid (带容错处理)
            if (!string.IsNullOrWhiteSpace(req.FileId) && Guid.TryParse(req.FileId, out Guid fileGuid))
            {
                emp.AvatarFileId = fileGuid;
                Console.WriteLine($"[Debug] ✅ AvatarFileId 已更新为: {fileGuid}");
            }
            else
            {
                Console.WriteLine($"[Debug] ⚠️ FileId 格式无效或为空: '{req.FileId}' (跳过 GUID 更新)");
            }

            // B. 更新路径
            emp.p_emp_photo = req.FileUrl; // 修正: PEmpPhoto -> p_emp_photo
            Console.WriteLine($"[Debug] ✅ p_emp_photo 已更新为: {req.FileUrl}");

            // C. 保存并确认
            int rows = await db.SaveChangesAsync(ct);
            Console.WriteLine($"[Debug] 💾 数据库保存完成，受影响行数: {rows}");

            return Results.Ok(new
            {
                msg = "头像更新成功",
                empId = emp.employeeid,        // 修正: Employeeid -> employeeid
                newAvatarUrl = emp.p_emp_photo,// 修正: PEmpPhoto -> p_emp_photo
                fileId = emp.AvatarFileId
            });
        });

        // ==========================================
        // 2. 全量员工列表 (GET) - 适配前端本地搜索
        // ==========================================
        group.MapGet("/list", async (AppDbContext db, IWebHostEnvironment env, CancellationToken ct) =>
        {
            try
            {
                // ✅ 不依赖 db.P_EmployeeTabs
                var list = await db.Set<P_EmployeeTab>()
                    .AsNoTracking()
                    .Where(x => x.UseFlag == 1) // 仅在职 (保持不变)
                    .OrderBy(x => x.p_emp_no)   // 修正: PEmpNo -> p_emp_no
                    .Select(x => new
                    {
                        employeeId = x.employeeid,   // 修正: Employeeid -> employeeid
                        employeeName = x.EmployeeName,
                        pEmpNo = x.p_emp_no,         // 修正: PEmpNo -> p_emp_no
                        displayName = $"{x.EmployeeName} ({x.p_emp_no})" // 修正: PEmpNo -> p_emp_no
                    })
                    .ToListAsync(ct);

                return Results.Ok(list);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] 获取员工列表失败: {ex}");
                var msg = env.IsDevelopment() ? ex.Message : "Server error";
                return Results.Problem(msg);
            }
        });

        // ==========================================
        // 3. 获取员工详情 (GET)
        // ==========================================
        group.MapGet("/{id}", async (string id, AppDbContext db, CancellationToken ct) =>
        {
            var key = (id ?? "").Trim();
            bool hasIntId = int.TryParse(key, out int empIdInt);

            var emp = await db.Set<P_EmployeeTab>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    (hasIntId && x.employeeid == empIdInt) || // 修正: Employeeid -> employeeid
                    x.LoginAccount == key,
                    ct);

            if (emp is null) return Results.NotFound();

            return Results.Ok(new
            {
                emp.employeeid,            // 修正: Employeeid -> employeeid
                emp.EmployeeName,
                fullAvatarUrl = emp.p_emp_photo, // 修正: PEmpPhoto -> p_emp_photo
                emp.AvatarFileId
            });
        });

        return app;
    }
}

public class UpdateAvatarRequest
{
    public string EmpId { get; set; } = "";
    public string FileId { get; set; } = "";
    public string FileUrl { get; set; } = "";
}