import { createBdd } from 'playwright-bdd';
import { expect } from '@playwright/test';
import { test } from './fixtures';

const { When, Then } = createBdd(test);

// 与 common.steps.ts 同款宽松匹配：兼容 antd 图标前缀与两字间空格变体。
function looseName(name: string): RegExp {
  const escaped = name
    .split('')
    .map((c) => c.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
    .join('\\s*');
  return new RegExp(escaped);
}

// 工作流卡片网格中，每个卡片都带「版本历史」等按钮；点击第一个卡片上的目标按钮。
When('我点击第一个工作流的 {string} 按钮', async ({ page }, label: string) => {
  await page.getByRole('button', { name: looseName(label) }).first().click();
});

// 版本抽屉（admin 可见「存为版本」操作）已打开。
Then('版本抽屉显示 {string}', async ({ page }, text: string) => {
  await expect(page.getByText(text, { exact: false })).toBeVisible({ timeout: 10000 });
});
