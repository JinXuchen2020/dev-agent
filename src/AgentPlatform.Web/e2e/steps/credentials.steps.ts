import { createBdd } from 'playwright-bdd';
import { expect } from '@playwright/test';
import { test } from './fixtures';

const { When, Then } = createBdd(test);

// 与后端 Integration 夹具 / common.steps.ts 约定一致（DatabaseInitializer 播种的已知明文 ApiKey）。
const API = process.env.API_BASE || 'http://localhost:5000';
const API_KEY = process.env.E2E_API_KEY || 'integration-fixture-key-0001';

// 凭据表单位于 antd Modal（role="dialog"）内。表单字段的 label 形如「* 名称」「* 模型名称」，
// 用 getByLabel('名称') 会同时命中名称输入框与模型名称下拉（子串匹配），触发严格模式。
// 因此按 role 区分：名称 / API Key 是 textbox，Provider / 模型名称是 combobox，再用正则限定。
When('我在凭据表单填写名称 {string}', async ({ page }, name: string) => {
  await page.getByRole('dialog').getByRole('textbox', { name: /名称/ }).fill(name);
});

When('我在凭据表单选择 Provider {string}', async ({ page }, provider: string) => {
  const dialog = page.getByRole('dialog');
  // antd Select 的 Provider 字段可能已有默认值（Integration 环境下默认即 OpenAI）。
  // 若下拉已显示该 Provider 为选中项（selection-item span），无需再展开选择，直接返回，
  // 否则展开下拉并从选项点击（选中项点击会让下拉闪关，导致 option 不可见，故先判定）。
  const selected = dialog.locator('.ant-select-selection-item').first();
  if (await selected.count() > 0 && (await selected.innerText()).includes(provider)) {
    return;
  }
  const select = dialog.getByRole('combobox', { name: /Provider/ });
  await select.click({ force: true });
  await page.getByRole('option', { name: new RegExp(provider) }).click();
});

When('我在凭据表单填写 API Key {string}', async ({ page }, key: string) => {
  await page.getByRole('dialog').getByRole('textbox', { name: /API Key/ }).fill(key);
});

When('我在凭据表单填写模型名称 {string}', async ({ page }, model: string) => {
  await page.getByRole('dialog').getByRole('combobox', { name: /模型名称/ }).fill(model);
});

Then('凭据表单显示', async ({ page }) => {
  await expect(page.getByRole('dialog').getByRole('textbox', { name: /名称/ })).toBeVisible();
});

Then('页面出现凭据 {string}', async ({ page }, name: string) => {
  // 保存后 dialog 关闭，凭据以卡片形式出现在页面主体（model Tab 下）。
  await expect(page.getByText(name)).toBeVisible();
});

When('我删除测试模型凭据以恢复租户状态', async ({ request }) => {
  // 测试隔离（2026-08-28 修复 workflow-debug / publish-workflow 连环失败）：
  // 本场景创建的 BYO 凭据（假 key + gpt-4o + 空 BaseUrl → api.openai.com）会因
  // ModelRouter「BYO 候选优先」污染默认租户，使后续所有真实 LLM 调用（conversation 之外的
  // workflow 运行 / debug/step）改走这条必失败的凭据 → CI 401/500。故保存断言后立即删除，
  // 恢复租户回平台模型（CI 注入的真实 key）。走独立 request 夹具 + fixture ApiKey，
  // 不挂在 page 上，避免污染 flowErrors 收集。
  const list = await request.get(`${API}/api/v1/tenant/credentials?category=0`, {
    headers: { 'X-Api-Key': API_KEY },
  });
  expect(list.status(), `list credentials status ${list.status()}`).toBe(200);
  const creds = (await list.json()) as { id: string; name: string }[];
  const target = creds.find((c) => c.name === 'E2E 测试模型凭据');
  if (!target) return; // 租户已干净（未创建成功或已清理）
  const del = await request.delete(`${API}/api/v1/tenant/credentials/${target.id}`, {
    headers: { 'X-Api-Key': API_KEY },
  });
  // DELETE 成功返回 204 No Content（controller 返回 NoContent），也兼容 200。
  expect([200, 204], `delete credential status ${del.status()}`).toContain(del.status());
});
