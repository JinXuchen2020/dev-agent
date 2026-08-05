import { createBdd } from 'playwright-bdd';
import { expect } from '@playwright/test';
import { test } from './fixtures';

const { When, Then } = createBdd(test);

When('我在对话输入框输入 {string}', async ({ page }, text: string) => {
  await page.getByLabel('输入消息').fill(text);
});

When('我点击发送', async ({ page }) => {
  // antd 图标按钮：accessible name 为 "send 发送"，需用宽松匹配。
  await page.getByRole('button', { name: /发\s*送/ }).click();
});

Then('收到智能体回复', async ({ page }) => {
  // Integration 后端 ModelClient:Provider=Stub，回复固定为 appsettings.Integration.json 的 StubResponse。
  await expect(page.getByText('Integration test stub response.', { exact: false })).toBeVisible({
    timeout: 20000,
  });
});

Then('状态筛选控件可见', async ({ page }) => {
  await expect(page.getByRole('combobox', { name: '状态筛选' })).toBeVisible();
});

Then('搜索框控件可见', async ({ page }) => {
  await expect(page.getByPlaceholder('搜索 ID / 智能体 / 工作流 / 知识库')).toBeVisible();
});
