import { createBdd } from 'playwright-bdd';
import { expect } from '@playwright/test';
import { test } from './fixtures';

const { When, Then } = createBdd(test);

When('我点击新建智能体', async ({ page }) => {
  await page.getByRole('button', { name: '新建智能体' }).click();
});

When('我在智能体表单填写名称 {string}', async ({ page }, name: string) => {
  await page.getByLabel('名称').fill(name);
});

// F29: 允许工具多选（antd Select mode=multiple）。用 typeahead 过滤 + Enter 选择，
// 规避 antd dropdown 动画导致的 click 稳定性抖动。
When('我在智能体表单勾选允许工具 {string}', async ({ page }, tool: string) => {
  await page.getByLabel('允许工具').click();
  await page.keyboard.type(tool, { delay: 60 });
  await page.keyboard.press('Enter');
  await page.keyboard.press('Escape');
});

// F29: 定位智能体卡片并点击「运行」按钮。
// 卡片容器用 .entity-card（Card 组件根类名）+ hasText 定位，不依赖 antd Space 等内部嵌套层级
// （e7641bd 曾把标题从 Card title 移入 body，ancestor::div[1] 随之失效）。
// antd 二字按钮 accessible name 会插入空格（"运 行"），用宽松正则 /运\s*行/ 兼容。
// .first()：历史重名智能体存在时取第一张卡片。
When('我点击智能体 {string} 的运行按钮', async ({ page }, name: string) => {
  const card = page.locator('.entity-card', { hasText: name }).first();
  await card.getByRole('button', { name: /运\s*行/ }).click();
});

// F29: 运行弹窗目标输入（TextArea placeholder 以「输入目标」开头）。
When('我在运行弹窗输入目标 {string}', async ({ page }, goal: string) => {
  await page.getByPlaceholder(/输入目标/).fill(goal);
});

// 等待预算从测试级 timeout 推导（@timeout 标签 → testInfo.timeout），而不是写死常量：
// 写死常量会再次出现「步骤等 90s、测试 30s 就被强杀」的错位（F29 首挂根因）。
// 预留 RESERVED_MS 给前置步骤（登录 / 建智能体 / 填目标 / 发起运行）。
// 可用环境变量 E2E_AGENTIC_ANSWER_TIMEOUT（毫秒）覆盖，便于本地慢网调优。
const ANSWER_RESERVED_MS = 30_000;

const resolveAnswerTimeout = (testTimeout: number): number => {
  // 测试总预算减去前置步骤预留，得到等待「最终回答」的安全上限。
  const budget = Math.max(30_000, testTimeout - ANSWER_RESERVED_MS);
  // 环境变量可覆盖，但绝不允许超过 budget（否则再次陷入「步骤等更久、测试先被强杀」的错位）。
  const env = Number(process.env.E2E_AGENTIC_ANSWER_TIMEOUT);
  if (Number.isFinite(env) && env > 0) return Math.min(env, budget);
  return budget;
};

Then('运行弹窗显示最终回答', async ({ page, $testInfo }) => {
  // 真实 key 下自主运行走真实 LLM：编排器先 RouteAsync 探测工具调用、再审 RouteStreamAsync 逐 token 返回，
  // 单次真实调用在 CI 网络下常 > 20s，原 20s 超时易误判。且若模型返回 429/错误，runError 置位、
  // 「最终回答」区块永不渲染，需抛出真实失败原因而非静默超时。
  // 故改为等待「终态」：最终回答区块 或 错误告警任一先出现；若先出现错误，抛真实原因以便诊断。
  const answer = page.getByText('最终回答', { exact: true });
  const error = page.locator('.ant-alert-error');
  await expect(answer.or(error)).toBeVisible({ timeout: resolveAnswerTimeout($testInfo.timeout) });
  if (await error.isVisible()) {
    const msg = (await error.innerText()).trim();
    throw new Error(`智能体运行失败：${msg}`);
  }
});

Then('智能体创建成功', async ({ page }) => {
  await expect(page.getByText('已创建智能体', { exact: true })).toBeVisible({ timeout: 10000 });
});

Then('页面出现智能体 {string}', async ({ page }, name: string) => {
  // .first()：历史重名智能体存在时仍断言「至少可见一张卡片」。
  await expect(page.getByText(name).first()).toBeVisible();
});

When('我删除智能体 {string}', async ({ page }, name: string) => {
  // 卡片容器用 .entity-card（Card 组件根类名）+ hasText 定位，不依赖卡片内部嵌套层级。
  // antd 二字按钮 accessible name 会插入空格（"删 除"），用宽松正则 /删\s*除/ 兼容。
  const card = page.locator('.entity-card', { hasText: name }).first();
  await card.getByRole('button', { name: /删\s*除/ }).click();
  // Popconfirm 气泡内的确认按钮（common.delete="删除"），作用域限定避免误点触发按钮。
  await page.locator('.ant-popconfirm').getByRole('button', { name: /删\s*除/ }).click();
});

Then('智能体已删除 {string}', async ({ page }, name: string) => {
  await expect(page.getByText(name)).toHaveCount(0, { timeout: 10000 });
});
