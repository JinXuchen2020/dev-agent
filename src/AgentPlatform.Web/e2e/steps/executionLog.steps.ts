import { createBdd } from 'playwright-bdd';
import { expect } from '@playwright/test';
import { test } from './fixtures';

const { When, Then } = createBdd(test);

// 与后端集成种子严格对齐（src/AgentPlatform.SpecFlowTests/IntegrationConstants.cs）：
// FailedExecutionLogId / FailedExecutionStepName —— 直连该日志详情，避免列表排序受其它场景新增日志影响。
const FAILED_LOG_ID = '66666666-6666-6666-6666-666666666601';
const FAILED_STEP_NAME = 'BDD Failing Step';

function looseName(name: string): RegExp {
  const escaped = name
    .split('')
    .map((c) => c.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
    .join('\\s*');
  return new RegExp(escaped);
}

When('我打开失败执行日志的详情页', async ({ page }) => {
  await page.goto(`/execution-logs/${FAILED_LOG_ID}`);
  await page.waitForSelector('text=' + FAILED_STEP_NAME, { timeout: 15000 });
});

Then('执行日志详情显示步骤明细标签', async ({ page }) => {
  await expect(page.getByRole('tab', { name: looseName('步骤明细') })).toBeVisible();
});

When('我切到回放诊断标签', async ({ page }) => {
  await page.getByRole('tab', { name: looseName('回放诊断') }).click();
});

Then('回放诊断显示执行路径时间线', async ({ page }) => {
  await expect(page.getByText(looseName('执行路径'))).toBeVisible({ timeout: 15000 });
  // 三个节点均在重建路径中（Start/Generate/失败步）。
  await expect(page.getByText(FAILED_STEP_NAME)).toBeVisible();
});

Then('回放诊断标注失败节点', async ({ page }) => {
  await expect(page.getByText(looseName('发现 1 个失败节点'))).toBeVisible();
  await expect(page.getByText('失败', { exact: true }).first()).toBeVisible();
});

Then('回放诊断披露数据缺口', async ({ page }) => {
  // 缺失信息必须显式呈现，不能把「无数据」读成「无问题」。
  await expect(page.getByText(looseName('数据缺口'))).toBeVisible();
  await expect(page.getByText('真实入参')).toBeVisible();
});

When('我展开回放路径中的第一个节点', async ({ page }) => {
  await page.locator('.ant-collapse-header').first().click();
});

Then('节点详情显示输入输出与错误栏', async ({ page }) => {
  await expect(page.getByText('输入（推断）')).toBeVisible();
  await expect(page.getByText('输出', { exact: false }).first()).toBeVisible();
  await expect(page.getByText('错误', { exact: true }).first()).toBeVisible();
});
