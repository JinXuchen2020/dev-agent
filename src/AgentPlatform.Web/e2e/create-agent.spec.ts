import { test, expect } from '@playwright/test';

// Agents 页列表渲染冒烟：登录态下能正常加载并渲染表格，且无 console error。
// 依赖后端 5000 真实登录(cookie 鉴权)。Agents 页本身无「新建」按钮(创建走其他入口)，
// 故此处只验证列表渲染；后端不可用时整体 skip。
const API = process.env.API_BASE || 'http://localhost:5000';
const EMAIL = 'admin@acme.io';
const PASSWORD = 'Admin@123456';

const BENIGN = [/favicon/i, /antd v5 support React/i, /Download the React DevTools/i, /canceled/i];

test.describe('agents list smoke', () => {
  test('agents page renders table without console errors', async ({ page }) => {
    const errors: string[] = [];
    page.on('console', (m) => {
      if (m.type() === 'error' && !BENIGN.some((r) => r.test(m.text()))) errors.push(m.text());
    });
    page.on('pageerror', (e) => errors.push(e.message));

    const resp = await page.request.post(`${API}/api/v1/auth/login`, {
      data: { email: EMAIL, password: PASSWORD },
    });
    test.skip(resp.status() !== 200, 'backend auth login unavailable');

    await page.goto('/agents');
    await expect(page.getByRole('columnheader', { name: 'Name' })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Role' })).toBeVisible();
    await expect(errors, `console errors:\n${errors.join('\n')}`).toEqual([]);
  });
});
