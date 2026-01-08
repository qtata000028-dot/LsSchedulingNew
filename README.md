<div align="center">
<img width="1200" height="475" alt="GHBanner" src="https://github.com/user-attachments/assets/0aa67016-6eaf-458a-adb2-6e31a0763ed6" />
</div>

# LsScheduling - 本地运行（VS2026 / .NET 8）

这份前端代码来自 Google AI Studio 下载包，已做了适配：

- ✅ 移除 Tailwind CDN，改为 **本地 Tailwind 编译**（运行更稳定）
- ✅ 默认启用 **Vite 代理 /api → 后端**，前端不必处理 CORS
- ✅ 支持 `.env.local` 配置：本机 / cloudflared 临时 HTTPS / Supabase 动态配置

## 1) 前置要求

- Node.js 18+（建议 Node 20 LTS）
- 后端：.NET 8（TunnelBackend）

## 2) 在 Visual Studio 2026 打开并运行前端

1. VS → **File → Open → Folder**，选择本项目根目录（含 `package.json`）
2. 打开 Terminal，执行：

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

## 3) 后端本地运行建议（.NET 8）

- 让后端监听 `http://localhost:5000`（或把 `.env.local` 的 `VITE_PROXY_TARGET` 改成你实际端口）
- 前端所有请求都走 `/api/...`，会被 Vite 代理转发到后端

## 4) 使用 cloudflared 临时 HTTPS（可选）

如果你想让前端直接连 cloudflared 的临时 HTTPS（不走 Vite 代理），在 `.env.local` 增加：

```txt
VITE_BACKEND_BASE_URL=https://xxxxx.trycloudflare.com
```

此时前端会跳过 Supabase 查询，直接使用这个固定基地址。
