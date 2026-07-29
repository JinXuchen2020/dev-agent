import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// F15 · 前端测试配置。jsdom 环境支撑 React 组件测试；setupFiles 注入 jest-dom 匹配器。
export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    css: false,
  },
});
