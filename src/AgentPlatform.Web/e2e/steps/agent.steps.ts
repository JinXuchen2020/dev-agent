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

// F29: 定位智能体卡片并点击「运行」按钮（卡片标题上溯容器，与删除按钮同法）。
// antd 二字按钮 accessible name 会插入空格（"运 行"），用宽松正则 /运\s*行/ 兼容。
// .first()：历史重名智能体存在时取第一张卡片。
When('我点击智能体 {string} 的运行按钮', async ({ page }, name: string) => {
  const card = page.getByText(name, { exact: true }).first().locator('xpath=ancestor::div[1]');
  await card.getByRole('button', { name: /运\s*行/ }).click();
});

// F29: 运行弹窗目标输入（TextArea placeholder 以「输入目标」开头）。
When('我在运行弹窗输入目标 {string}', async ({ page }, goal: string) => {
  await page.getByPlaceholder(/输入目标/).fill(goal);
});

Then('运行弹窗显示最终回答', async ({ page }) => {
  // 运行完成后展示「最终回答」区块；stub 模型直接返回文本，1 次迭代即结束。
  await expect(page.getByText('最终回答', { exact: true })).toBeVisible({ timeout: 20000 });
});

Then('智能体创建成功', async ({ page }) => {
  await expect(page.getByText('已创建智能体', { exact: true })).toBeVisible({ timeout: 10000 });
});

Then('页面出现智能体 {string}', async ({ page }, name: string) => {
  // .first()：历史重名智能体存在时仍断言「至少可见一张卡片」。
  await expect(page.getByText(name).first()).toBeVisible();
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
