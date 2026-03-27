# TunnelBackend (.NET 10 / VS2026) - SQL Server + 上传 + Supabase(存 cloudflared URL)

干净、可维护的后端基线：
- SQL Server：业务表（员工/部门/订单/工艺路线/上传文件记录）
- 本地磁盘上传：文件落盘到 App_Data/uploads
- Supabase（可选）：只存一张表 CloudflaredEndpoints（cloudflared 外网 URL）
- API Key：外网访问必须带 X-API-KEY（不要把 key 写死到前端）

## 1) 必填：SQL Server 连接串
`appsettings.json` -> ConnectionStrings:Default
也可以用环境变量：ConnectionStrings__Default

## 2) API Key
开发环境：appsettings.Development.json 里默认 key：dev-key-change-me
生产环境建议用环境变量：
- ApiKeyAuth__Keys__0=prod-key-abcdef

## 3) 运行
F5 或 dotnet run
Swagger：http://localhost:5111/swagger
健康检查：GET /health、GET /health/db

## 4) Supabase（可选）
执行 scripts/supabase_public_reset_and_cloudflared.sql
然后设置环境变量：
- Supabase__Url
- Supabase__ServiceRoleKey

## 5) 重建 SQL Server 表（可选，会清空数据）
scripts/sqlserver_create_tables.sql
