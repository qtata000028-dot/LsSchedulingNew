<div align="center">
<img width="1200" height="475" alt="GHBanner" src="https://github.com/user-attachments/assets/0aa67016-6eaf-458a-adb2-6e31a0763ed6" />
</div>

# LsScheduling - 前端开发文档（VS2026 / .NET 8）

这份前端代码来自 Google AI Studio 下载包，已做了适配与二次整合：

- ✅ 移除 Tailwind CDN，改为本地 Tailwind 编译
- ✅ 默认启用 Vite 代理 `/api` → 后端，前端不必处理 CORS
- ✅ 支持 `.env.local` 配置：本机 / cloudflared 临时 HTTPS / Supabase 动态配置
- ✅ 接口服务层统一封装（登录、员工、订单、APS 排产）

## 1) 技术栈与结构概览

- React 18 + React Router（Hash Router）
- Vite + TypeScript
- Tailwind CSS（本地编译）
- Supabase（可选，用于读取 cloudflared 动态地址）

目录结构（核心）：

- `App.tsx`：路由入口（Hash Router）
- `services/`：接口封装
- `views/`：页面视图
- `components/`：通用组件
- `layouts/`：布局组件

## 2) 前置要求

- Node.js 18+（建议 Node 20 LTS）
- 后端：.NET 8（TunnelBackend）

## 3) 快速开始（本地开发）

1. VS → **File → Open → Folder**，选择本项目根目录（含 `package.json`）
2. 安装依赖：

```bash
npm install
```

3. 复制 `.env.example` 为 `.env.local`（按你的后端端口改一下）：

```txt
VITE_PROXY_TARGET=http://localhost:5000
VITE_DEV_PORT=3000
```

4. 启动前端：

```bash
npm run dev
```

默认会在 `http://localhost:3000` 启动。

## 4) 配置说明（.env.local）

`.env.local` 仅用于本机开发，不要提交到仓库。可选项如下：

```txt
# 方案 A（推荐）：Vite 代理到后端。
VITE_PROXY_TARGET=http://localhost:5000
VITE_DEV_PORT=3000

# 方案 B：不走代理，直接指定后端基地址（例如 cloudflared 的临时 HTTPS）。
# 设置后会跳过 Supabase 查询。
# VITE_BACKEND_BASE_URL=https://xxxxx.trycloudflare.com

# 可选：后端授权/校验密钥（X-API-KEY）
VITE_API_KEY=lserp2026wyftool

# 可选：从 Supabase 读“当前后端公网地址”记录（比如 home-pc）
VITE_SUPABASE_URL=
VITE_SUPABASE_KEY=
VITE_TUNNEL_NAME=home-pc

# 可选：后端默认地址（兜底）
VITE_DEFAULT_BACKEND_URL=http://localhost:5000
```

配置优先级（从高到低）：

1. `VITE_BACKEND_BASE_URL`（强制指定）
2. 开发环境：使用当前页面 `window.location.origin`（配合 Vite 代理）
3. Supabase 动态地址（`tunnel_endpoints.public_url`）
4. `VITE_DEFAULT_BACKEND_URL`（兜底）

## 5) 后端本地运行建议（.NET 8）

- 让后端监听 `http://localhost:5000`（或把 `.env.local` 的 `VITE_PROXY_TARGET` 改成你实际端口）
- 前端所有请求都走 `/api/...`，会被 Vite 代理转发到后端

## 6) cloudflared 临时 HTTPS（可选）

如果你想让前端直接连 cloudflared 的临时 HTTPS（不走 Vite 代理），在 `.env.local` 增加：

```txt
VITE_BACKEND_BASE_URL=https://xxxxx.trycloudflare.com
```

此时前端会跳过 Supabase 查询，直接使用这个固定基地址。

## 7) 路由与页面

路由采用 Hash 模式，入口见 `App.tsx`：

- `/login`：登录页
- `/dashboard`：主界面（默认重定向到 `/dashboard/steps`）
- `/dashboard/steps`：排产步骤页

## 8) 接口与服务层

所有请求默认携带 `X-API-KEY`，值来自 `VITE_API_KEY`（未配置时使用内置默认值）。

`services/authService.ts`：

- `GET /api/employees/list?q=keyword`：搜索员工
- `POST /api/auth/login`：登录
- `POST /api/files`：上传文件
- `POST /api/employees/update-avatar`：绑定头像
- 动态 BaseUrl 逻辑：代理 / cloudflared / Supabase / 兜底地址

`services/orderService.ts`：

- `GET /api/orders?page=1&pageSize=20&keyword=`：订单列表
- `POST /api/orders/{billId}/analyze`：APS 分析

`services/apsScheduleService.ts`：

- `GET /api/aps/schedule/months?includeAll=false`：可排产月份
- `POST /api/aps/schedule/run`：执行排产

## 9) 常用脚本

- `npm run dev`：本地开发
- `npm run build`：构建生产包
- `npm run preview`：本地预览构建产物

## 10) 常见问题

- 如果请求 404 或跨域：确认后端端口与 `.env.local` 中的 `VITE_PROXY_TARGET` 一致。
- 如果登录失败：检查 `VITE_API_KEY` 与后端配置一致。
- 如果 cloudflared 地址失效：更新 `VITE_BACKEND_BASE_URL` 或 Supabase 中的 `public_url`。
