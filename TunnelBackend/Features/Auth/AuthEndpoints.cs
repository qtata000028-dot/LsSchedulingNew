using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TunnelBackend.Data;
using TunnelBackend.Features.Entities;

namespace TunnelBackend.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth (登录验证)");

        group.MapPost("/login", async (LoginRequest req, AppDbContext db, CancellationToken ct) =>
        {
            // 1. 基础校验
            if (string.IsNullOrWhiteSpace(req.UserId) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { error = "账号或密码不能为空" });

            // 2. 复刻旧系统的加密逻辑
            string encryptedPassword = EncryptPasswordLegacy(req.Password);

            // 3. 准备连接
            string connStr = db.Database.GetConnectionString()
                             ?? throw new Exception("数据库连接字符串未配置");

            int loginResultCode = -999; // 默认一个错误码

            // 4. 执行原生 ADO.NET
            await using (SqlConnection conn = new SqlConnection(connStr))
            {
                await conn.OpenAsync(ct);

                // A. 强制登录逻辑 (如果前端传了 true，先删掉旧登录记录)
                if (req.IsConstraint)
                {
                    const string deleteSql = "DELETE FROM p_LoginHostInfotab WHERE OperatorId = @UserId AND Tagid = 1";
                    await using (SqlCommand cmdDel = new SqlCommand(deleteSql, conn))
                    {
                        cmdDel.Parameters.AddWithValue("@UserId", req.UserId.Trim());
                        await cmdDel.ExecuteNonQueryAsync(ct);
                    }
                }

                // B. 调用存储过程 P_Login_pr
                await using (SqlCommand cmd = new SqlCommand("P_Login_pr", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    // 设置参数
                    cmd.Parameters.Add("@ReturnValue", SqlDbType.Int).Direction = ParameterDirection.ReturnValue;
                    cmd.Parameters.Add("@operatorid", SqlDbType.VarChar).Value = req.UserId.Trim();
                    cmd.Parameters.Add("@pwd", SqlDbType.VarChar, 100).Value = encryptedPassword;

                    // 如果需要 @IsEncrypt 参数，取消下面注释
                    // cmd.Parameters.Add("@IsEncrypt", SqlDbType.VarChar, 100).Value = "1";

                    await cmd.ExecuteNonQueryAsync(ct);

                    // C. 获取返回值
                    if (cmd.Parameters["@ReturnValue"].Value != DBNull.Value)
                        loginResultCode = Convert.ToInt32(cmd.Parameters["@ReturnValue"].Value);
                }
            }

            // =======================================================
            // 5. 根据你的标准判断结果 (0:成功, 1:密码错, 2:已登录)
            // =======================================================
            switch (loginResultCode)
            {
                case 0:
                    {
                        // 1. 清洗
                        var cleanUserId = (req.UserId ?? "").Trim();

                        // 2. 尝试转成数字
                        bool isInt = int.TryParse(cleanUserId, out int empIdInt);

                        Console.WriteLine($"[Debug] 正在查找用户: String='{cleanUserId}', Int={empIdInt}, IsInt={isInt}");

                        // 3. ✅ 修正：使用新生成的 P_EmployeeTab 类名和字段名
                        var emp = await db.Set<P_EmployeeTab>()
                            .AsNoTracking()
                            .FirstOrDefaultAsync(x =>
                                (isInt && x.employeeid == empIdInt) || // 修正：Employeeid -> employeeid
                                x.LoginAccount == cleanUserId ||       // 保持不变
                                x.p_emp_no == cleanUserId ||           // 修正：PEmpNo -> p_emp_no
                                x.EmployeeName == cleanUserId,         // 保持不变
                                ct);

                        // 4. 获取名字
                        string finalName = cleanUserId;
                        if (emp != null)
                        {
                            if (!string.IsNullOrWhiteSpace(emp.EmployeeName))
                                finalName = emp.EmployeeName;
                            else if (!string.IsNullOrWhiteSpace(emp.p_emp_EmployeeName)) // 修正：PEmpEmployeeName -> p_emp_EmployeeName
                                finalName = emp.p_emp_EmployeeName;
                            else if (!string.IsNullOrWhiteSpace(emp.p_emp_no))           // 修正：PEmpNo -> p_emp_no
                                finalName = emp.p_emp_no;
                        }
                        else
                        {
                            Console.WriteLine("[Debug] 警告：存储过程验证通过，但 EF Core 没查到该用户详细信息！");
                        }

                        return Results.Ok(new
                        {
                            code = 0,
                            msg = "登录成功",
                            success = true,
                            userId = cleanUserId,
                            userName = finalName,
                            fullAvatarUrl = emp?.p_emp_photo, // 修正：PEmpPhoto -> p_emp_photo
                            token = Guid.NewGuid().ToString()
                        });
                    }

                case 1:
                    return Results.Ok(new
                    {
                        code = 1,
                        success = false,
                        error = "密码不正确"
                    });

                case 2:
                    return Results.Ok(new
                    {
                        code = 2,
                        success = false,
                        error = "该账号已在别处登录",
                        needConstraint = true
                    });

                default:
                    return Results.Ok(new
                    {
                        code = loginResultCode,
                        success = false,
                        error = $"登录失败 (错误代码: {loginResultCode})"
                    });
            }
        });

        return app;
    }

    /// <summary>
    /// 旧版加密复刻 ("2006" + pwd + "New System" -> SHA1)
    /// </summary>
    private static string EncryptPasswordLegacy(string password)
    {
        if (string.IsNullOrEmpty(password)) return "";
        string salted = "2006" + password + "New System";
        byte[] bytePassword = Encoding.ASCII.GetBytes(salted);
        using (SHA1 sha = SHA1.Create())
        {
            byte[] dataHashed = sha.ComputeHash(bytePassword);
            return BitConverter.ToString(dataHashed).Replace("-", "");
        }
    }
}

public class LoginRequest
{
    public string UserId { get; set; } = "";
    public string Password { get; set; } = "";
    public bool IsConstraint { get; set; } = false;
}