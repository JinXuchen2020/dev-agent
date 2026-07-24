import { test, expect } from '@playwright/test';

// 登录态用真实 cookie 鉴权（F2 之后：POST /api/v1/auth/login 写入 httpOnly
// Cookie `ap_access_token`，前端经 withCredentials 自动携带，不再读 localStorage）。
const API = process.env.API_BASE || 'http://localhost:5000';
const EMAIL = 'admin@acme.io';
const PASSWORD = 'Admin@123456';

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

test.describe('authenticated routes render with cookie auth', () => {
  test('each protected route loads and stays put', async ({ page }) => {
    // page.request 与 page 共享同一 cookie 存储，登录后页面导航自动携带 ap_access_token
    const loginRes = await page.request.post(`${API}/api/v1/auth/login`, {
      data: { email: EMAIL, password: PASSWORD },
    });
    test.skip(loginRes.status() !== 200, `auth login unavailable (status ${loginRes.status()})`);

    const errors: string[] = [];
    page.on('console', (m) => {
      if (m.type() === 'error' && !BENIGN.some((r) => r.test(m.text()))) errors.push(m.text());
    });
    page.on('pageerror', (e) => errors.push(e.message));

    for (const route of PROTECTED) {
      errors.length = 0;
      await page.goto(route);

      // 已登录：不应被踢回 /login
      expect(page.url(), `route ${route} redirected to login`).not.toContain('/login');
      expect(errors, `console errors on ${route}:\n${errors.join('\n')}`).toEqual([]);
    }
  });
});
