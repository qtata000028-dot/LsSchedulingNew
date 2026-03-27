# SQL 调用整理

基于当前源码整理，范围包括前端 `services/`、页面入口、以及后端 `TunnelBackend/`。

说明：
- `原生 SQL / 存储过程`：源码里明确写出来的 SQL。
- `EF Core 生成 SQL（近似）`：根据 LINQ/`SaveChanges` 推导的 SQL 语义，字段顺序和参数名运行时可能略有差异。
- 当前后端连接的是 SQL Server，配置见 `TunnelBackend/appsettings.json`，数据库名为 `lserp_MK`。

## 1. 前端直接查库

### 1.1 动态获取后端地址
- 位置：`services/authService.ts`
- 条件：仅在没有 `VITE_BACKEND_BASE_URL`、不是本地 dev 同源、且配置了 Supabase 时才会走这条查询
- 调用方式：Supabase JS
- 等价 SQL：

```sql
SELECT public_url
FROM tunnel_endpoints
WHERE name = @TUNNEL_NAME
LIMIT 1;
```

备注：
- 这里不是走你自己的后端，而是前端直接查 Supabase。
- 仓库里的 `TunnelBackend/scripts/supabase_public_reset_and_cloudflared.sql` 建的是 `CloudflaredEndpoints`，和这里查的 `tunnel_endpoints` 名字对不上。

## 2. 登录相关

### 2.1 强制登录时，先清理旧登录记录
- 前端入口：`components/LoginForm.tsx` -> `services/authService.ts` -> `POST /api/auth/login`
- 后端位置：`TunnelBackend/Features/Auth/AuthEndpoints.cs`
- 类型：原生 SQL

```sql
DELETE FROM p_LoginHostInfotab
WHERE OperatorId = @UserId
  AND Tagid = 1;
```

### 2.2 登录校验存储过程
- 前端入口：同上
- 后端位置：`TunnelBackend/Features/Auth/AuthEndpoints.cs`
- 类型：存储过程

```sql
EXEC @ReturnValue = P_Login_pr
    @operatorid = @UserId,
    @pwd        = @EncryptedPassword;
```

### 2.3 登录成功后，补查员工资料
- 前端入口：同上
- 后端位置：`TunnelBackend/Features/Auth/AuthEndpoints.cs`
- 类型：EF Core 生成 SQL（近似）

```sql
SELECT TOP (1) *
FROM P_EmployeeTab
WHERE (@IsInt = 1 AND employeeid = @EmpIdInt)
   OR LoginAccount = @CleanUserId
   OR p_emp_no = @CleanUserId
   OR EmployeeName = @CleanUserId;
```

## 3. 员工相关

### 3.1 获取员工列表
- 前端入口：`components/LoginForm.tsx` 初始化时调用 `getEmployeeListService('')`
- 后端接口：`GET /api/employees/list`
- 后端位置：`TunnelBackend/Features/Employees/EmployeeEndpoints.cs`
- 类型：EF Core 生成 SQL（近似）

```sql
SELECT employeeid, EmployeeName, p_emp_no
FROM P_EmployeeTab
WHERE UseFlag = 1
ORDER BY p_emp_no;
```

备注：
- 前端会传 `?q=...`，但后端当前没有把 `q` 下推到 SQL，实际上是全量查在职员工后由前端本地过滤。

### 3.2 更新头像前，先查员工
- 前端入口：头像上传绑定流程 -> `updateEmployeeAvatarService`
- 后端接口：`POST /api/employees/update-avatar`
- 后端位置：`TunnelBackend/Features/Employees/EmployeeEndpoints.cs`
- 类型：EF Core 生成 SQL（近似）

```sql
SELECT TOP (1) *
FROM P_EmployeeTab
WHERE (@HasIntId = 1 AND employeeid = @EmpIdInt)
   OR LoginAccount = @EmpKey
   OR p_emp_no = @EmpKey
   OR EmployeeName = @EmpKey;
```

### 3.3 更新员工头像字段
- 前端入口：同上
- 后端接口：`POST /api/employees/update-avatar`
- 后端位置：`TunnelBackend/Features/Employees/EmployeeEndpoints.cs`
- 类型：EF Core 生成 SQL（近似）

```sql
UPDATE P_EmployeeTab
SET AvatarFileId = @AvatarFileId,
    p_emp_photo = @FileUrl
WHERE employeeid = @employeeid;
```

### 3.4 获取员工详情
- 前端：当前项目里没看到明确调用
- 后端接口：`GET /api/employees/{id}`
- 后端位置：`TunnelBackend/Features/Employees/EmployeeEndpoints.cs`
- 类型：EF Core 生成 SQL（近似）

```sql
SELECT TOP (1) *
FROM P_EmployeeTab
WHERE (@HasIntId = 1 AND employeeid = @EmpIdInt)
   OR LoginAccount = @Key;
```

## 4. 文件上传

### 4.1 上传文件后写入文件记录表
- 前端入口：头像上传第一步 `uploadFileService`
- 后端接口：`POST /api/files`
- 后端位置：`TunnelBackend/Features/Files/FileEndpoints.cs`
- 类型：EF Core 生成 SQL（近似）

```sql
INSERT INTO Sys_FileRecord
    (Id, FileName, StoredFileName, ContentType, FileSize, UploadTime, UploaderId)
VALUES
    (@Id, @FileName, @StoredFileName, @ContentType, @FileSize, @UploadTime, @UploaderId);
```

## 5. APS 排程

### 5.1 获取排程月份列表
- 前端入口：`views/Steps.tsx` -> `services/apsScheduleService.ts` -> `fetchApsMonths`
- 后端接口：`GET /api/aps/schedule/months`
- 后端位置：`TunnelBackend/Features/Orders/OrderEndpoints.cs`
- 类型：原生 SQL

```sql
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
```

### 5.2 运行 APS 排程前，拉平订单 + 工艺 + 产能数据
- 前端入口：`views/Steps.tsx` -> `services/apsScheduleService.ts` -> `runApsSchedule`
- 后端接口：`POST /api/aps/schedule/run`
- 后端位置：`TunnelBackend/Features/Orders/OrderEndpoints.cs`
- 类型：原生 SQL

```sql
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

    ISNULL(p.mes_prolist_manhour, 0) AS PieceManMinute,
    ISNULL(p.mes_prolist_manhour01, 0) AS PrepMinute,
    ISNULL(p.mes_prolist_manhour02, 0) AS DebugMinute,

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
    AND (@FromYm IS NULL OR (dt.yearid * 100 + dt.monthid) >= @FromYm)
    AND (@ToYm   IS NULL OR (dt.yearid * 100 + dt.monthid) <= @ToYm)

ORDER BY
    dt.yearid, dt.monthid, a.crm_lco_billdocument_id, a.crm_lco_id, p.mes_prolist_no;
```

## 6. 当前代码里存在，但现在并不会执行的 SQL 入口

### 6.1 `SqlGateway`
- 文件：`TunnelBackend/Features/SqlGateway/SqlGatewayEndpoints.cs`
- 作用：允许前端提交任意 `SELECT/WITH` 查询
- 当前状态：
  - `Program.cs` 没有注册 `app.MapSqlGatewayEndpoints()`
  - `appsettings.json` 里 `SqlGateway.Enabled = false`
- 结论：当前运行态不会执行这里的 SQL

### 6.2 `orderService.ts` / `Orders.tsx`
- 文件：`services/orderService.ts`、`views/Orders.tsx`
- 前端请求的是：

```http
GET  /api/orders?page=...&pageSize=20&keyword=...
POST /api/orders/{billId}/analyze
```

- 但当前后端源码里没有对应 `/api/orders` 接口，`App.tsx` 也没有把 `Orders.tsx` 挂到路由上。
- 结论：这部分现在是残留/未接通代码，当前不会落到任何 SQL。

## 7. 初始化脚本（不是运行时调用）

### 7.1 SQL Server 建表脚本
- 文件：`TunnelBackend/scripts/sqlserver_create_tables.sql`
- 主要创建：`Files`、`Departments`、`Employees`、`ProcessRoutes`、`ProcessRouteSteps`、`Orders`、`OrderDrawings`
- 结论：这是独立初始化脚本，当前业务代码没有直接调用这些表。

### 7.2 Supabase 初始化脚本
- 文件：`TunnelBackend/scripts/supabase_public_reset_and_cloudflared.sql`
- 主要创建：`public."CloudflaredEndpoints"`
- 结论：也是初始化脚本，不是运行时 SQL。

