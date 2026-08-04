import { test, expect } from '@playwright/test';

// F22 全链路 UI E2E：在 Workflows 页发布一个 Completed 工作流，
// 验证发布 Drawer 显示 slug 与调用端点，并用 ApiKey 经端点发起调用。
//
// 数据来源：独立运行的 Integration 后端（dotnet run --environment Integration --no-launch-profile）
// 经 DatabaseInitializer 播种固定夹具——「Integration Fixture Workflow」(Completed)
// 与已知明文 ApiKey（integration-fixture-key-0001）。前端 dev server 由
// playwright.config.ts 的 webServer 自动拉起（端口 5180）。
//
// 后端不可用时整体 skip（与仓库内既有 e2e 一致，避免 CI 环境缺后端时误红）。
const API = process.env.API_BASE || 'http://localhost:5000';
const EMAIL = 'admin@acme.io';
const PASSWORD = 'Admin@123456';
const API_KEY = process.env.E2E_API_KEY || 'integration-fixture-key-0001';
const FIXTURE_WORKFLOW_NAME = 'Integration Fixture Workflow';

// 发布按钮文案：en-US = "Publish"，zh-CN = "发布"（实际渲染为 "发 布"，中间带空格，
// 疑为 antd/i18n 渲染产物）。三者皆可匹配，避免依赖默认语言与渲染细节。
const PUBLISH_RE = /^(Publish|发\s*布)$/;

// 不依赖 console 错误监听：改用 page.on('response') 做精确 HTTP 错误断言（见末尾），
// 以 URL 精确允许已知未完工缺口 /api/v1/api-keys 的 404，同时捕获发布链路内的任何其它错误。

test.describe('publish workflow via UI and invoke endpoint (F22)', () => {
  test('publish a completed workflow and call its Api endpoint', async ({ page, request }) => {
    // 1) 登录（cookie 鉴权，页面与 page.request 共享存储；写入 httpOnly ap_access_token）
    const loginRes = await page.request.post(`${API}/api/v1/auth/login`, {
      data: { email: EMAIL, password: PASSWORD },
    });
    test.skip(loginRes.status() !== 200, `backend auth login unavailable (status ${loginRes.status()})`);

    const jsErrors: string[] = [];
    const httpErrors: { status: number; url: string }[] = [];
    page.on('pageerror', (e) => jsErrors.push(e.message));
    page.on('response', (r) => {
      if (r.status() >= 400) httpErrors.push({ status: r.status(), url: r.url() });
    });

    // 2) 打开 Workflows 页
    await page.goto('/workflows');

    // 夹具工作流卡片：实体卡片网格中每张卡含工作流名 + 一个 Publish/发布 按钮。
    // 自定义 Card 组件是普通 div（无 ant-card 类），故以「含夹具名 + 含发布按钮」定位。
    const card = page
      .locator('div')
      .filter({ hasText: FIXTURE_WORKFLOW_NAME })
      .filter({ has: page.getByRole('button', { name: PUBLISH_RE }) })
      .first();
    await expect(card, `fixture workflow '${FIXTURE_WORKFLOW_NAME}' not visible on /workflows`).toBeVisible();

    // 3) 点击卡片内 Publish 打开发布 Drawer
    await card.getByRole('button', { name: PUBLISH_RE }).click();

    const drawer = page.locator('.ant-drawer-content').last();
    await expect(drawer, 'publish drawer did not open').toBeVisible();

    // Drawer 可能已处于「已发布」态（slug 可见）或「未发布」表单态
    const slugCode = drawer.locator('code').first();
    let slug = '';
    if ((await slugCode.count()) > 0) {
      slug = (await slugCode.innerText()).trim();
    } else {
      // 未发布：点击 Drawer 内主发布按钮（无绑定 Key，接受任意有效 ApiKey）
      await drawer.getByRole('button', { name: PUBLISH_RE }).click();
      await expect(slugCode, 'slug did not appear after publish').toBeVisible({ timeout: 10000 });
      slug = (await slugCode.innerText()).trim();
    }
    expect(slug.length, 'published slug must be non-empty').toBeGreaterThan(0);

    // 端点文本显示（Api 模式）：POST /api/v1/published-workflows/{slug}
    await expect(
      drawer.getByText(/POST \/api\/v1\/published-workflows\//),
      'publish endpoint text not shown',
    ).toBeVisible();

    // 4) 用 ApiKey 经端点调用：验证鉴权 + slug 路由可达（非 401 坏密钥 / 非 404 未知 slug）。
    //    工作流实际执行依赖 LLM/沙箱，此处仅做可达性断言，不耦合执行结果。
    const runRes = await request.post(`${API}/api/v1/published-workflows/${slug}`, {
      headers: { 'X-Api-Key': API_KEY },
      data: {},
    });
    expect(runRes.status(), `run status ${runRes.status()} (expected not 401/404)`).not.toBe(401);
    expect(runRes.status(), `run status ${runRes.status()} (expected not 404)`).not.toBe(404);

    // 已知缺口：/api/v1/api-keys 后端尚未实现对应 controller（前端 ApiKeysPage 未完工特性），
    // openPublish 调 getApiKeys 返回 404，前端 .catch 优雅降级为空列表——与 smoke.auth.spec.ts 一致排除。
    // 发布链路本身（登录 / 发布 / 端点调用）不允许任何其它 HTTP 错误。
    const unexpectedHttp = httpErrors.filter(
      (e) => !(e.status === 404 && /api-keys/i.test(e.url)),
    );
    expect(
      unexpectedHttp,
      `unexpected HTTP errors:\n${unexpectedHttp.map((e) => `${e.status} ${e.url}`).join('\n')}`,
    ).toEqual([]);
    expect(jsErrors, `JS errors:\n${jsErrors.join('\n')}`).toEqual([]);
  });
});
