import { test, expect } from '@playwright/test';

const API = process.env.API_BASE || 'http://localhost:5000';

// 只需登录态即可渲染、不依赖具体实体 ID 的路由
const PROTECTED = [
  '/',
  '/agents',
  '/conversations',
  '/workflows',
  '/workflows/new',
  '/agent-roles',
  '/agent-configurations',
  '/execution-logs',
  '/api-keys',
];

const BENIGN = [
  /Download the React DevTools/i,
  /Ant Design/i,
  /Warning: ReactDOM/i,
  /not wrapped in act/i,
  /antd v5 support React/i,
];

test.describe('authenticated routes render with dev login', () => {
  test('each protected route loads and stays put', async ({ page, request }) => {
    const loginRes = await request.post(`${API}/api/dev/login`, { data: { role: 'Admin' } });
    test.skip(loginRes.status() !== 200, `dev login unavailable (status ${loginRes.status()})`);

    const body = (await loginRes.json()) as { token?: string };
    const token = body.token;
    test.skip(!token, 'dev login returned no token');

    // 监听器只注册一次；循环内重置 errors，避免重复注册到同一 page 造成累积误报
    const errors: string[] = [];
    page.on('console', (m) => {
      if (m.type() === 'error' && !BENIGN.some((r) => r.test(m.text()))) errors.push(m.text());
    });
    page.on('pageerror', (e) => errors.push(e.message));

    for (const route of PROTECTED) {
      errors.length = 0;
      await page.goto('/login');
      await page.evaluate((t) => localStorage.setItem('auth_token', t), token);
      await page.goto(route);

      // 已登录：不应被踢回 /login
      expect(page.url(), `route ${route} redirected to login`).not.toContain('/login');
      expect(errors, `console errors on ${route}:\n${errors.join('\n')}`).toEqual([]);
    }
  });
});
