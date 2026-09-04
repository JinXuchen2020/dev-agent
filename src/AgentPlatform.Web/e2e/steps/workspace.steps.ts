import { createBdd } from 'playwright-bdd';
import { expect } from '@playwright/test';
import { test } from './fixtures';

const { When, Then } = createBdd(test);

// 与 common.steps.ts 一致：antd 控件的 accessible name 存在图标前缀 / 中间空格变体，
// 统一用「逐字 \s*」宽松正则匹配。
function looseName(name: string): RegExp {
  const escaped = name
    .split('')
    .map((c) => c.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
    .join('\\s*');
  return new RegExp(escaped);
}

When('我在顶栏工作空间管理菜单中新建工作空间 {string}', async ({ page }, name: string) => {
  // 顶栏管理按钮（aria-label = t('workspace.manage')），Admin-only。
  await page.getByRole('button', { name: looseName('管理工作空间') }).click();
  // Dropdown 菜单项「新建工作空间」。
  await page.getByText('新建工作空间', { exact: true }).click();
  // antd Modal（role=dialog）内填 名称 / 描述，提交。
  const dialog = page.getByRole('dialog');
  // antd 必填项 label 渲染为「* 名称」→ 用正则子串（credentials.steps 同款已验证写法）。
  await dialog.getByRole('textbox', { name: /名称/ }).fill(name);
  // okText 显式取 common.confirm=「确认」（antd 旧默认文案为「确定」，两汉字间空格由 CSS 插入，
  // 可及名可能是「确 定」/「确定」）→ 两者都容忍，避免再次卡 60s。
  await dialog.getByRole('button', { name: /确\s*[认定]/ }).click();
  // 等待模态关闭 + 列表刷新（创建成功提示由 WorkspaceSwitcher message.success 触发）。
  await expect(page.getByRole('dialog')).toBeHidden({ timeout: 10000 });
});

Then('工作空间切换器包含 {string}', async ({ page }, name: string) => {
  // 打开 Select 下拉（aria-label = t('workspace.label')），断言选项出现。
  await page.getByRole('combobox', { name: '工作空间' }).click();
  await expect(page.getByRole('option', { name: new RegExp(name) })).toBeVisible({ timeout: 10000 });
  await page.keyboard.press('Escape');
});

When('我选择工作空间 {string}', async ({ page }, name: string) => {
  await page.getByRole('combobox', { name: '工作空间' }).click();
  await page.getByRole('option', { name: new RegExp(name) }).click();
});
