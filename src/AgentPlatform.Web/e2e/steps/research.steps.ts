import { createBdd } from 'playwright-bdd';
import { expect } from '@playwright/test';
import { test } from './fixtures';

const { When, Then } = createBdd(test);

When('我在调研输入框输入 {string}', async ({ page }, text: string) => {
  await page.getByPlaceholder(/输入要调研的问题/).fill(text);
});

When('我点击开始调研', async ({ page }) => {
  await page.getByRole('button', { name: '开始调研' }).click();
});

Then('调研报告已生成', async ({ page }) => {
  // 报告标题 "调研报告" 仅在报告流式返回后出现；未配置 SerpApi 时各检索会失败但报告仍基于规划内容生成。
  await expect(page.getByText('调研报告')).toBeVisible({ timeout: 30000 });
});
