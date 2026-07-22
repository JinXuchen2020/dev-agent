import { test, expect } from '@playwright/test';

// 验证 Agents 页「新建 Agent」按钮已接通：点击打开表单弹窗，且无 console error。
// 不提交，避免污染后端数据。依赖后端 5000 的 /api/dev/login 可用。
const BENIGN = [/favicon/i, /antd v5 support React/i];

test.describe('agents create dialog', () => {
  test('open create dialog without console errors', async ({ page }) => {
    const errors: string[] = [];
    page.on('console', (m) => {
      if (m.type() === 'error' && !BENIGN.some((r) => r.test(m.text()))) errors.push(m.text());
    });
    page.on('pageerror', (e) => errors.push(e.message));

    const resp = await page.request.post('/api/dev/login', { data: { role: 'Admin' } });
    if (!resp.ok()) {
      test.skip(true, 'backend /api/dev/login unavailable');
      return;
    }
    const { token } = await resp.json();
    await page.goto('/login');
    await page.evaluate((t) => localStorage.setItem('auth_token', t), token);
    await page.goto('/agents');

    await expect(page.getByRole('button', { name: /新建 Agent/ })).toBeVisible();
    await page.getByRole('button', { name: /新建 Agent/ }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await expect(dialog.getByText('名称', { exact: true })).toBeVisible();
    // 不提交，关闭即可（antd 中文按钮文本带字间距，如「取 消」，正则允许空格）
    await dialog.getByRole('button', { name: /取\s*消/ }).click();

    await expect(errors, `console errors:\n${errors.join('\n')}`).toEqual([]);
  });
});
