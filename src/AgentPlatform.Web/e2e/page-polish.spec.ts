import { test, expect } from '@playwright/test';

// F3 · 页面交互打磨 的端到端冒烟：验证新增/修复的交互控件存在且不抛 console error。
// 依赖后端 5000 的真实登录（cookie 鉴权）可用；后端不可用时整体 skip。
const API = process.env.API_BASE || 'http://localhost:5000';
const EMAIL = 'admin@acme.io';
const PASSWORD = 'Admin@123456';

const BENIGN = [/favicon/i, /antd v5 support React/i, /Download the React DevTools/i];

async function loginOrSkip(page: import('@playwright/test').Page): Promise<void> {
  const resp = await page.request.post(`${API}/api/v1/auth/login`, {
    data: { email: EMAIL, password: PASSWORD },
  });
  test.skip(resp.status() !== 200, `auth login unavailable (status ${resp.status()})`);
}

test.describe('F3 page interaction polish', () => {
  test('conversations: search + status filter controls render', async ({ page }) => {
    const errors: string[] = [];
    page.on('console', (m) => {
      if (m.type() === 'error' && !BENIGN.some((r) => r.test(m.text()))) errors.push(m.text());
    });
    page.on('pageerror', (e) => errors.push(e.message));

    await loginOrSkip(page);
    await page.goto('/conversations');

    // 搜索框与状态筛选下拉均存在
    await expect(page.getByPlaceholder('搜索 ID / Agent / 工作流 / 知识库')).toBeVisible();
    await expect(page.getByPlaceholder('状态筛选')).toBeVisible();

    // 输入关键字不抛错（数据为空时显示空态，不崩溃）
    await page.getByPlaceholder('搜索 ID / Agent / 工作流 / 知识库').fill('demo');
    await expect(errors, `console errors:\n${errors.join('\n')}`).toEqual([]);
  });

  test('workflows: quick run with empty name warns', async ({ page }) => {
    const errors: string[] = [];
    page.on('console', (m) => {
      if (m.type() === 'error' && !BENIGN.some((r) => r.test(m.text()))) errors.push(m.text());
    });
    page.on('pageerror', (e) => errors.push(e.message));

    await loginOrSkip(page);
    await page.goto('/workflows');

    await page.getByRole('button', { name: 'Quick Run' }).click();
    const modal = page.getByRole('dialog');
    await expect(modal).toBeVisible();
    // 空名直接点 Run → 应出现 warning 提示，且弹窗保持打开
    await modal.getByRole('button', { name: 'Run' }).click();
    await expect(page.getByText('请输入工作流名称')).toBeVisible();
    await expect(modal).toBeVisible();

    await expect(errors, `console errors:\n${errors.join('\n')}`).toEqual([]);
  });

  test('execution-logs: status filter select renders', async ({ page }) => {
    const errors: string[] = [];
    page.on('console', (m) => {
      if (m.type() === 'error' && !BENIGN.some((r) => r.test(m.text()))) errors.push(m.text());
    });
    page.on('pageerror', (e) => errors.push(e.message));

    await loginOrSkip(page);
    await page.goto('/execution-logs');
    await expect(page.getByPlaceholder('Filter status')).toBeVisible();
    await expect(errors, `console errors:\n${errors.join('\n')}`).toEqual([]);
  });

  test('agent-configurations: view drawer shows YAML', async ({ page }) => {
    const errors: string[] = [];
    page.on('console', (m) => {
      if (m.type() === 'error' && !BENIGN.some((r) => r.test(m.text()))) errors.push(m.text());
    });
    page.on('pageerror', (e) => errors.push(e.message));

    await loginOrSkip(page);
    await page.goto('/agent-configurations');

    const viewBtn = page.getByRole('button', { name: 'View' }).first();
    // 若种子未生成任何配置，跳过抽屉断言
    if (await viewBtn.isVisible().catch(() => false)) {
      await viewBtn.click();
      const drawer = page.getByRole('dialog');
      await expect(drawer).toBeVisible();
      await expect(drawer.getByText('YAML Configuration')).toBeVisible();
    }
    await expect(errors, `console errors:\n${errors.join('\n')}`).toEqual([]);
  });
});
