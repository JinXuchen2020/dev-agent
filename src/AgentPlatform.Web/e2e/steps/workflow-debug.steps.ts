import { createBdd } from 'playwright-bdd';
import { expect } from '@playwright/test';
import { test } from './fixtures';

const { When, Then } = createBdd(test);

// antd 按钮 accessible name 含图标前缀与可能的中文字符间空格，统一用逐字 \s* 正则兼容。
// 见 common.steps.ts 的 looseName 说明。
function looseName(name: string): RegExp {
  const escaped = name
    .split('')
    .map((c) => c.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
    .join('\\s*');
  return new RegExp(escaped);
}

When('I open the workflow detail for the fixture workflow {string}', async ({ page }, name: string) => {
  const cardTitle = page.getByText(name, { exact: true }).first();
  await expect(cardTitle, `fixture workflow '${name}' not visible on /workflows`).toBeVisible();
  // 点击卡片标题（非按钮）→ EntityCardGrid 整卡跳转至详情页。
  await cardTitle.click();
  await expect(page, 'should navigate to workflow detail').toHaveURL(/\/workflows\/[^/]+$/, {
    timeout: 10000,
  });
});

When('I open the debugger for that workflow', async ({ page }) => {
  await page.getByRole('button', { name: looseName('工作流调试器') }).click();
  await expect(page, 'should navigate to debugger').toHaveURL(/\/workflows\/[^/]+\/debug$/, {
    timeout: 10000,
  });
});

Then('the debugger start control is visible', async ({ page }) => {
  await expect(page.getByRole('button', { name: looseName('开始调试') })).toBeVisible();
});

When('I start a debug session', async ({ page }) => {
  await page.getByRole('button', { name: looseName('开始调试') }).click();
  await expect(
    page.getByTestId('debug-variables'),
    'variables panel should appear after starting a session',
  ).toBeVisible({ timeout: 10000 });
});

Then('a debug session is started and variables panel shows', async ({ page }) => {
  await expect(page.getByTestId('debug-variables')).toBeVisible();
});

When('I step the debugger', async ({ page }) => {
  await page.getByRole('button', { name: looseName('单步执行') }).click();
});

Then('the debug variables panel is shown', async ({ page }) => {
  await expect(page.getByTestId('debug-variables')).toBeVisible();
});
