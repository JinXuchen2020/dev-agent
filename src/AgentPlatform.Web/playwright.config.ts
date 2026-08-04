import { defineConfig } from '@playwright/test';
import { defineBddConfig } from 'playwright-bdd';

const PORT = 5180;

// 前端 E2E 统一走 BDD（Gherkin）：.feature 在 e2e/features，步骤在 e2e/steps。
// defineBddConfig 在配置加载时把 BDD 配置写入 env，并将生成的测试写入 e2e/.features-gen（已被 .gitignore 忽略）。
// 运行方式：先 `bddgen` 生成，再 `playwright test`（见 package.json 的 e2e 脚本 / scripts/integration.mjs）。
// testDir 仍指向 ./e2e，使既有的非 BDD smoke 规格（smoke.*.spec.ts）也能被收集。
defineBddConfig({
  features: 'e2e/features/**/*.feature',
  steps: 'e2e/steps/**/*.ts',
  outputDir: 'e2e/.features-gen',
});

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
