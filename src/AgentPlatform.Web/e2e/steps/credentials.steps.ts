import { createBdd } from 'playwright-bdd';
import { expect } from '@playwright/test';
import { test } from './fixtures';

const { When, Then } = createBdd(test);

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
