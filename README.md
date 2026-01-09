<div align="center">
<img width="1200" height="475" alt="GHBanner" src="https://github.com/user-attachments/assets/0aa67016-6eaf-458a-adb2-6e31a0763ed6" />
</div>

# LsScheduling - 前端开发文档（VS2026 / .NET 8）

这份前端代码来自 Google AI Studio 下载包，已做了适配：

- ✅ 移除 Tailwind CDN，改为本地 Tailwind 编译
- ✅ 默认启用 Vite 代理 `/api` → 后端，前端不必处理 CORS
- ✅ 支持 `.env.local` 配置：本机 / cloudflared 临时 HTTPS / Supabase 动态配置

## 1) 前置要求

- Node.js 18+（建议 Node 20 LTS）
- 后端：.NET 8（TunnelBackend）

## 2) 快速开始（本地开发）

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

## 3) 配置说明（.env.local）

`.env.local` 仅用于本机开发，不要提交到仓库。可选项如下：

```txt
# 方案 A（推荐）：Vite 代理到后端。
VITE_PROXY_TARGET=http://localhost:5000
VITE_DEV_PORT=3000

# 方案 B：不走代理，直接指定后端基地址（例如 cloudflared 的临时 HTTPS）。
# 设置后会跳过 Supabase 查询。
# VITE_BACKEND_BASE_URL=https://xxxxx.trycloudflare.com

# 可选：后端授权/校验密钥
VITE_API_KEY=lserp2026wyftool

# 可选：从 Supabase 读“当前后端公网地址”记录（比如 home-pc）
VITE_SUPABASE_URL=
VITE_SUPABASE_KEY=
VITE_TUNNEL_NAME=home-pc
```

## 4) 后端本地运行建议（.NET 8）

- 让后端监听 `http://localhost:5000`（或把 `.env.local` 的 `VITE_PROXY_TARGET` 改成你实际端口）
- 前端所有请求都走 `/api/...`，会被 Vite 代理转发到后端

## 5) cloudflared 临时 HTTPS（可选）

如果你想让前端直接连 cloudflared 的临时 HTTPS（不走 Vite 代理），在 `.env.local` 增加：

```txt
VITE_BACKEND_BASE_URL=https://xxxxx.trycloudflare.com
```

此时前端会跳过 Supabase 查询，直接使用这个固定基地址。

## 6) 常用脚本

- `npm run dev`：本地开发
- `npm run build`：构建生产包
- `npm run preview`：本地预览构建产物
