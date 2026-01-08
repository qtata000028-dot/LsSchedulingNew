import path from 'path';
import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ mode }) => {
    // 读取 .env*（建议自定义变量都用 VITE_ 前缀）
    const env = loadEnv(mode, '.', '');

    // ✅ 开发阶段用 Vite 代理 /api 到 .NET 8 后端，前端代码只写 /api 即可
    // 默认端口按 ASP.NET Core 常见默认：http://localhost:5000
    // 你也可以在 .env.local 里设置：VITE_PROXY_TARGET=http://localhost:xxxx
    const proxyTarget = (env.VITE_PROXY_TARGET || 'http://localhost:5000').replace(/\/$/, '');
    const devPort = Number(env.VITE_PORT || 3000);

    return {
      server: {
        port: devPort,
        host: '0.0.0.0',
        proxy: {
          '/api': {
            target: proxyTarget,
            changeOrigin: true,
            secure: false,
          },
        },
      },
      plugins: [react()],
      define: {
        'process.env.API_KEY': JSON.stringify(env.GEMINI_API_KEY),
        'process.env.GEMINI_API_KEY': JSON.stringify(env.GEMINI_API_KEY)
      },
      resolve: {
        alias: {
          '@': path.resolve(__dirname, '.'),
        }
      }
    };
});
