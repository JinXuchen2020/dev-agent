import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
      '/scalar': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
  build: {
    // 供应商分包：把体积大户从首屏主包拆出，避免单 chunk 1.38MB（O6）。
    // react-vendor / antd / xyflow 各自独立 chunk，配合 App.tsx 的路由级 React.lazy
    // 实现按需加载。Vite 8（rolldown）的 manualChunks 仅支持函数形式。
    chunkSizeWarningLimit: 1200,
    rollupOptions: {
      output: {
        manualChunks(id: string) {
          if (!id.includes('node_modules')) return undefined;
          if (id.includes('@xyflow')) return 'xyflow';
          if (id.includes('antd') || id.includes('@ant-design')) return 'antd';
          if (
            id.includes('react-router') ||
            id.includes('node_modules/react-dom') ||
            id.includes('node_modules/react/') ||
            id.includes('node_modules/scheduler')
          ) {
            return 'react-vendor';
          }
          return undefined;
        },
      },
    },
  },
});
