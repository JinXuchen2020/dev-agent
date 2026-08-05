import { defineConfig } from '@playwright/test';
import { defineBddConfig } from 'playwright-bdd';

const PORT = 5180;

// 前端 E2E 统一走 BDD（Gherkin）：.feature 在 e2e/features，步骤在 e2e/steps。
// defineBddConfig 在配置加载时把 BDD 配置写入 env（PLAYWRIGHT_BDD_CONFIGS，按 outputDir 作 key），
// 并将生成的测试写入 e2e/.features-gen（已被 .gitignore 忽略）。
// ⚠️ 关键契约（playwright-bdd 9.x）：Playwright 的 project.testDir 必须等于 BDD outputDir，
// 否则运行期 getConfigFromEnv(testDir) 按 outputDir 查不到配置 → "BDD config not found"。
// 故此处捕获 defineBddConfig 的返回值（即解析后的 outputDir）作为 testDir。
// 运行方式：先 `bddgen` 生成，再 `playwright test`（见 package.json 的 e2e 脚本 / scripts/integration.mjs）。
// 既有的非 BDD smoke 规格（smoke.*.spec.ts）不含 @e2e，不计入闸门；需手动冒烟可显式 `playwright test e2e/smoke.unauth.spec.ts`。
const bddOutputDir = defineBddConfig({
  features: 'e2e/features/**/*.feature',
  steps: 'e2e/steps/**/*.ts',
  outputDir: 'e2e/.features-gen',
});

export default defineConfig({
  testDir: bddOutputDir,
  timeout: 30000,
  expect: { timeout: 8000 },
  // F28：前端 E2E 共享同一 Integration 后端（文件 SQLite + 单 admin 账号），
  // 且本沙箱 Edge 在多 worker 并行时会因 8 个浏览器实例内存压力崩溃（Target crashed）。
  // 单 worker 串行执行：1 个浏览器实例，确定性 + 稳定。
  fullyParallel: false,
  workers: 1,
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
