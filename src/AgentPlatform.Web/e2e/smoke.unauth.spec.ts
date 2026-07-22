import { test, expect } from '@playwright/test';

// 受保护路由（不含需要真实实体 ID 的详情页）
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

// 已知无害的 console.error（DevTools 提示 / antd 内部告警等）
const BENIGN = [
  /Download the React DevTools/i,
  /Ant Design/i,
  /Warning: ReactDOM/i,
  /not wrapped in act/i,
  /antd v5 support React/i,
];

test('login page renders without console errors', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', (m) => {
    if (m.type() === 'error' && !BENIGN.some((r) => r.test(m.text()))) errors.push(m.text());
  });
  page.on('pageerror', (e) => errors.push(e.message));

  await page.goto('/login');
  await expect(page).toHaveURL(/\/login$/);
  expect(errors, `console errors:\n${errors.join('\n')}`).toEqual([]);
});

test.describe('protected routes redirect to /login when unauthenticated', () => {
  for (const route of PROTECTED) {
    test(`redirects: ${route}`, async ({ page }) => {
      const errors: string[] = [];
      page.on('console', (m) => {
        if (m.type() === 'error' && !BENIGN.some((r) => r.test(m.text()))) errors.push(m.text());
      });
      page.on('pageerror', (e) => errors.push(e.message));

      await page.goto(route);
      await expect(page).toHaveURL(/\/login/);
      expect(errors, `console errors:\n${errors.join('\n')}`).toEqual([]);
    });
  }
});
