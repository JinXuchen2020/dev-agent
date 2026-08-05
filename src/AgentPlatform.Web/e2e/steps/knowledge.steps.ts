import { createBdd } from 'playwright-bdd';
import { expect } from '@playwright/test';
import { test } from './fixtures';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const { When, Then } = createBdd(test);

When('我点击新建知识库', async ({ page }) => {
  // antd 图标按钮：accessible name 为 "plus 新建知识库"，需用宽松匹配。
  await page.getByRole('button', { name: /新\s*建\s*知\s*识\s*库/ }).click();
});

When('我在知识库表单填写名称 {string}', async ({ page }, name: string) => {
  await page.getByLabel('知识库名称').fill(name);
});

Then('知识库创建成功', async ({ page }) => {
  await expect(page.getByText('知识库已创建', { exact: true })).toBeVisible({ timeout: 10000 });
});

When('我打开知识库 {string}', async ({ page }, name: string) => {
  await page.getByText(name).first().click();
});

When('我上传文档 {string}', async ({ page }, fileName: string) => {
  const filePath = path.join(os.tmpdir(), fileName);
  fs.writeFileSync(filePath, 'E2E 知识库文档内容，用于验证自动切分入库。');
  // 详情页 antd Upload 渲染隐藏的 input[type=file]，直接 setInputFiles 触发上传。
  await page.locator('input[type=file]').setInputFiles(filePath);
});

Then('文档入库成功', async ({ page }) => {
  await expect(page.getByText(/已切分入库/)).toBeVisible({ timeout: 20000 });
});
