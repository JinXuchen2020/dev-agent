import { createBdd } from 'playwright-bdd';
import { expect } from '@playwright/test';
import { test } from './fixtures';

const { Given, When, Then } = createBdd(test);

// 与后端 Integration 夹具一致（见 features/bdd-integration-design.md / DatabaseInitializer）。
const API = process.env.API_BASE || 'http://localhost:5000';
const EMAIL = 'admin@acme.io';
const PASSWORD = 'Admin@123456';
const API_KEY = process.env.E2E_API_KEY || 'integration-fixture-key-0001';
const PUBLISH_RE = /^(Publish|发\s*布)$/;

Given('the integration backend is reachable and I am authenticated as admin', async ({ page }) => {
  const loginRes = await page.request.post(`${API}/api/v1/auth/login`, {
    data: { email: EMAIL, password: PASSWORD },
  });
  // 后端不可用时整体 skip（与仓库内既有 e2e 一致，避免 CI 环境缺后端时误红）。
  test.skip(loginRes.status() !== 200, `backend auth login unavailable (status ${loginRes.status()})`);
});

When('I open the Workflows page', async ({ page }) => {
  await page.goto('/workflows');
});

When('I publish the fixture workflow {string}', async ({ page }, name: string) => {
  // 夹具工作流卡片：列表用 antd <Card title={name}> 渲染，根为 .ant-card，每张卡含一个 Publish/发布 按钮。
  // 直接按 .ant-card + 夹具名定位（hasText 子串匹配卡片标题），避免用「祖先 div + hasText」泛型匹配——
  // 当列表存在多张卡（如其他 E2E 留下的工作流）时，祖先网格容器会含多个发布按钮，触发 strict mode 冲突。
  const card = page.locator('.ant-card', { hasText: name }).first();
  await expect(card, `fixture workflow '${name}' not visible on /workflows`).toBeVisible();

  // 点击卡片内 Publish 打开发布 Drawer
  await card.getByRole('button', { name: PUBLISH_RE }).click();

  const drawer = page.locator('.ant-drawer-content').last();
  await expect(drawer, 'publish drawer did not open').toBeVisible();

  // Drawer 可能已处于「已发布」态（slug 可见）或「未发布」表单态
  const slugCode = drawer.locator('code').first();
  if ((await slugCode.count()) === 0) {
    // 未发布：点击 Drawer 内主发布按钮（无绑定 Key，接受任意有效 ApiKey）
    await drawer.getByRole('button', { name: PUBLISH_RE }).click();
    await expect(slugCode, 'slug did not appear after publish').toBeVisible({ timeout: 10000 });
  }
});

Then('the publish drawer shows a non-empty slug and the API endpoint text', async ({ page }) => {
  const drawer = page.locator('.ant-drawer-content').last();
  const slugCode = drawer.locator('code').first();
  const slug = (await slugCode.innerText()).trim();
  expect(slug.length, 'published slug must be non-empty').toBeGreaterThan(0);

  // 端点文本显示（Api 模式）：POST /api/v1/published-workflows/{slug}
  await expect(
    drawer.getByText(/POST \/api\/v1\/published-workflows\//),
    'publish endpoint text not shown',
  ).toBeVisible();
});

When('I invoke the published workflow endpoint with the fixture API key', async ({ page, request }) => {
  const drawer = page.locator('.ant-drawer-content').last();
  const slug = (await drawer.locator('code').first().innerText()).trim();

  // 用 ApiKey 经端点调用：验证鉴权 + slug 路由可达（非 401 坏密钥 / 非 404 未知 slug）。
  // 工作流实际执行依赖 LLM/沙箱，此处仅做可达性断言，不耦合执行结果。
  const runRes = await request.post(`${API}/api/v1/published-workflows/${slug}`, {
    headers: { 'X-Api-Key': API_KEY },
    data: {},
  });
  expect(runRes.status(), `run status ${runRes.status()} (expected not 401/404)`).not.toBe(401);
  expect(runRes.status(), `run status ${runRes.status()} (expected not 404)`).not.toBe(404);
});

Then('no unexpected HTTP or JS errors occurred during the flow', async ({ flowErrors }) => {
  // 已知缺口：/api/v1/api-keys 后端尚未实现对应 controller（前端 ApiKeysPage 未完工特性），
  // openPublish 调 getApiKeys 返回 404，前端 .catch 优雅降级为空列表——与 smoke.auth.spec.ts 一致排除。
  // 发布链路本身（登录 / 发布 / 端点调用）不允许任何其它 HTTP 错误。
  const unexpectedHttp = flowErrors.httpErrors.filter(
    (e) => !(e.status === 404 && /api-keys/i.test(e.url)),
  );
  expect(
    unexpectedHttp,
    `unexpected HTTP errors:\n${unexpectedHttp.map((e) => `${e.status} ${e.url}`).join('\n')}`,
  ).toEqual([]);
  expect(flowErrors.jsErrors, `JS errors:\n${flowErrors.jsErrors.join('\n')}`).toEqual([]);
});
