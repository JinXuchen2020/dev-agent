import { createBdd } from 'playwright-bdd';
import { expect } from '@playwright/test';
import { test } from './fixtures';

const { Then } = createBdd(test);

Then('工作流列表渲染', async ({ page }) => {
  await expect(page.getByRole('heading', { name: '工作流' })).toBeVisible();
  // 夹具工作流（Integration 环境播种）卡片应可见。
  await expect(page.getByText('Integration Fixture Workflow')).toBeVisible();
});

Then('提示请输入工作流名称', async ({ page }) => {
  await expect(page.getByText('请输入工作流名称')).toBeVisible({ timeout: 10000 });
});

Then('执行日志状态筛选控件可见', async ({ page }) => {
  await expect(page.getByRole('combobox', { name: '筛选状态' })).toBeVisible();
});
