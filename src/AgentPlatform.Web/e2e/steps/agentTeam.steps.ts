import { createBdd } from 'playwright-bdd';
import { expect } from '@playwright/test';
import { test } from './fixtures';

const { When, Then } = createBdd(test);

// 与 common.steps.ts 一致：antd 按钮 accessible name 兼容图标前缀与中间空格，点击第一个匹配。
function looseName(name: string): RegExp {
  const escaped = name
    .split('')
    .map((c) => c.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
    .join('\\s*');
  return new RegExp(escaped);
}

When('我点击第一个工作流的 编辑 按钮', async ({ page }) => {
  // 列表页每张工作流卡片含一个「编辑」按钮；点击第一个即进入其编辑页（id 已存在，
  // 后续「保存并运行」走 runExistingWorkflow(id, preset) 真实 DAG 协商运行路径）。
  const editBtn = page.getByRole('button', { name: looseName('编辑') }).first();
  await expect(editBtn, '未找到工作流编辑按钮').toBeVisible();
  await editBtn.click();
});

Then('画布含 Critic 评审节点', async ({ page }) => {
  // 脚手架生成的评审节点标签为英文 "Critic"（NodePalette 显示中文「评审」，不冲突）。
  await expect(page.getByText('Critic', { exact: true }), '画布未出现 Critic 评审节点').toBeVisible();
});

Then('画布显示协商模式指示', async ({ page }) => {
  // 含 Critic 节点（或显式选协商）时，工具栏出现「协商模式 · 评审收敛」Tag。
  await expect(
    page.getByText('协商模式 · 评审收敛'),
    '画布未显示协商模式指示',
  ).toBeVisible();
});

When('我在名称框输入 {string}', async ({ page }, name: string) => {
  await page.getByPlaceholder('工作流名称').fill(name);
});
