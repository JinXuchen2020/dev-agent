import { test, expect } from '@playwright/test';

const API = process.env.API_BASE || 'http://localhost:5000';
const EMAIL = 'admin@acme.io';
const PASSWORD = 'Admin@123456';

test.describe('F3 page interaction polish', () => {
  test.beforeEach(async ({ page }) => {
    const loginRes = await page.request.post(`${API}/api/v1/auth/login`, {
      data: { email: EMAIL, password: PASSWORD },
    });
    test.skip(loginRes.status() !== 200, `auth login unavailable (status ${loginRes.status()})`);
  });

  test('conversations: search + status filter controls render and server-filter works', async ({ page }) => {
    await page.goto('/conversations');
    await expect(page.getByText('Conversations', { exact: true })).toBeVisible();
    // antd Select exposes both a div and an inner input with the same aria-label;
    // the combobox role targets the interactive control unambiguously.
    await expect(page.getByRole('combobox', { name: '状态筛选' })).toBeVisible();
    const search = page.getByPlaceholder('搜索 ID / Agent / 工作流 / 知识库');
    await expect(search).toBeVisible();

    // 触发一次服务端搜索，验证新 q 过滤参数被正确带上
    await search.fill('nope-not-exist');
    await search.press('Enter');
    await expect(page.getByText('暂无会话记录')).toBeVisible();
  });

  test('workflows: quick run with empty name warns', async ({ page }) => {
    await page.goto('/workflows');
    await expect(page.getByRole('heading', { name: 'Workflows' })).toBeVisible();
    await page.getByRole('button', { name: 'Quick Run' }).click();
    // 等弹窗出现再点 Run，避免竞态（exact 区分 "Quick Run" 与 "Run"）
    const runBtn = page.getByRole('button', { name: 'Run', exact: true });
    await expect(runBtn).toBeVisible();
    await runBtn.click();
    await expect(page.getByText('请输入工作流名称')).toBeVisible({ timeout: 10000 });
  });

  test('execution-logs: status filter select renders', async ({ page }) => {
    await page.goto('/execution-logs');
    await expect(page.getByRole('heading', { name: 'Execution Logs' })).toBeVisible();
    await expect(page.getByRole('combobox', { name: 'Filter status' })).toBeVisible();
  });
});
