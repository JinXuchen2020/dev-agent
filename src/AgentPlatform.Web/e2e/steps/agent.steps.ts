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

Then('智能体创建成功', async ({ page }) => {
  await expect(page.getByText('已创建智能体')).toBeVisible({ timeout: 10000 });
});

Then('页面出现智能体 {string}', async ({ page }, name: string) => {
  await expect(page.getByText(name)).toBeVisible();
});

When('我删除智能体 {string}', async ({ page }, name: string) => {
  // 智能体卡片用自定义 Card 组件，根元素是普通 <div>（无 .ant-card class），无法用 .ant-card 定位。
  // 改为从 agent 标题文本上溯到其所在卡片容器（标题的直接父 div），再点其中的删除按钮。
  // antd 二字按钮 accessible name 会插入空格（"删 除"），用宽松正则 /删\s*除/ 兼容。
  const card = page.getByText(name, { exact: true }).locator('xpath=ancestor::div[1]');
  await card.getByRole('button', { name: /删\s*除/ }).click();
  // Popconfirm 气泡内的确认按钮（common.delete="删除"），作用域限定避免误点触发按钮。
  await page.locator('.ant-popconfirm').getByRole('button', { name: /删\s*除/ }).click();
});

Then('智能体已删除 {string}', async ({ page }, name: string) => {
  await expect(page.getByText(name)).toHaveCount(0, { timeout: 10000 });
});
