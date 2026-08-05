import { createBdd } from 'playwright-bdd';
import { expect } from '@playwright/test';
import { test } from './fixtures';

const { Given, When, Then } = createBdd(test);

// antd 按钮的 accessible name 有两种「变体」会让精确字符串匹配落空：
//  1) 图标 + 文本按钮：name = "图标名 文本"（如 "plus 添加模型凭据"、"send 发送"）；
//  2) 部分纯文本按钮：name 在两字间插入空格（如 "保 存"、"登 录"、"新 建"、"发 布"）。
// 统一用「逐字 \s*」正则（默认子串匹配），既兼容图标前缀，也兼容中间空格。
// 多匹配时点击最后一个（模态框 footer 通常最后渲染，正是提交目标）。
function looseName(name: string): RegExp {
  const escaped = name
    .split('')
    .map((c) => c.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
    .join('\\s*');
  return new RegExp(escaped);
}

// 与后端 Integration 夹具 / CredentialSettings 约定一致（见 bdd-integration-design.md / DatabaseInitializer）。
const API = process.env.API_BASE || 'http://localhost:5000';
const EMAIL = 'admin@acme.io';
const PASSWORD = 'Admin@123456';

// 后端不可用时整体 skip（与 F27 既有 e2e 约定一致，避免 CI 缺后端时误红）。
Given('集成后端可达', async ({ page }) => {
  const health = await page.request.get(`${API}/health`);
  test.skip(health.status() !== 200, `backend unavailable (health ${health.status()})`);
});

Given('集成后端可达且我已以 admin 登录', async ({ page }) => {
  const loginRes = await page.request.post(`${API}/api/v1/auth/login`, {
    data: { email: EMAIL, password: PASSWORD },
  });
  // 后端不可用时整体 skip（Playwright cookie 按 host 匹配，:5000 登录写入的 cookie 会随
  // :5180 的同域 API 调用自动携带，等价于 UI 登录后的鉴权态）。
  test.skip(loginRes.status() !== 200, `backend auth login unavailable (status ${loginRes.status()})`);
});

When('我打开 {string}', async ({ page }, path: string) => {
  await page.goto(path);
});

When('我未登录访问 {string}', async ({ page }, path: string) => {
  await page.goto(path);
});

When('我在登录页输入邮箱 {string} 与密码 {string}', async ({ page }, email: string, password: string) => {
  await page.getByPlaceholder('admin@acme.io').fill(email);
  await page.getByPlaceholder('请输入密码').fill(password);
});

When('我点击登录按钮', async ({ page }) => {
  await page.getByRole('button', { name: looseName('登录') }).click();
});

When('我点击按钮 {string}', async ({ page }, name: string) => {
  // 见 looseName：兼容 antd 图标前缀与中间空格；多匹配点击最后一个（模态框 footer）。
  const loc = page.getByRole('button', { name: looseName(name) });
  await loc.last().click();
});

When('我点击文本 {string}', async ({ page }, text: string) => {
  // 用于非 button 控件（如 antd Segmented 分段选项「近 30 天」）。
  // 同样用宽松匹配规避 antd 的空格变体。
  await page.getByText(looseName(text), { exact: true }).click();
});

Then('我被重定向到 {string}', async ({ page }, path: string) => {
  // "/" 表示仪表盘根路径：匹配以 "/" 结尾的 URL；其余按子路径子串匹配。
  const re = path === '/' ? /\/$/ : new RegExp(path.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'));
  await expect(page).toHaveURL(re, { timeout: 10000 });
});

Then('页面显示 {string}', async ({ page }, text: string) => {
  await expect(page.getByText(text, { exact: false })).toBeVisible();
});

Then('页面显示标题 {string}', async ({ page }, text: string) => {
  await expect(page.getByRole('heading', { name: text })).toBeVisible();
});

Then('没有意外的 JS 或 HTTP 错误发生', async ({ flowErrors }) => {
  // 已知缺口：/api/v1/api-keys 后端尚未实现对应 controller（前端 ApiKeysPage 未完工特性），
  // openXxx 调 getApiKeys 返回 404，前端 .catch 优雅降级为空列表——排除该 404。
  const unexpectedHttp = flowErrors.httpErrors.filter(
    (e) => !(e.status === 404 && /api-keys/i.test(e.url)),
  );
  expect(
    unexpectedHttp,
    `unexpected HTTP errors:\n${unexpectedHttp.map((e) => `${e.status} ${e.url}`).join('\n')}`,
  ).toEqual([]);
  expect(flowErrors.jsErrors, `JS errors:\n${flowErrors.jsErrors.join('\n')}`).toEqual([]);
});
