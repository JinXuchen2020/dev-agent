import { defineConfig } from '@playwright/test';

const PORT = 5180;

export default defineConfig({
  testDir: './e2e',
  timeout: 30000,
  expect: { timeout: 8000 },
  fullyParallel: true,
  reporter: [
    ['list'],
    ['json', { outputFile: 'playwright-report/results.json' }],
    ['html', { outputFolder: 'playwright-report', open: 'never' }],
  ],
  use: {
    baseURL: `http://localhost:${PORT}`,
    trace: 'on-first-retry',
  },
  // 用本机 Edge 驱动，免下载 chromium。
  // 注意：Playwright 1.61 在 headless 下若用 devices['Desktop Edge'] 会回落去要
  // chromium_headless_shell（channel 不生效）；必须显式写 channel:'msedge'。
  // 本机 Edge 在 puppeteer 场景会秒退，但 Playwright channel 驱动实测可用。
  projects: [
    {
      name: 'edge',
      use: {
        channel: 'msedge',
        viewport: { width: 1280, height: 720 },
      },
    },
  ],
  webServer: {
    command: 'npm run dev -- --port 5180 --strictPort',
    url: `http://localhost:${PORT}`,
    reuseExistingServer: true,
    timeout: 120000,
  },
});
