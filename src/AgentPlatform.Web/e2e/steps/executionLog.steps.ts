import { createBdd } from 'playwright-bdd';
import { expect } from '@playwright/test';
import { test } from './fixtures';

/**
 * F40 执行日志回放诊断 E2E。
 *
 * 数据策略（CI 修复 2026-09-03）：**场景自造数据**，不依赖任何进程级种子——
 * 原实现等待 `IntegrationSeeder` 播种的失败日志，但该 seeder 只在 SpecFlow 进程内运行，
 * 前端 E2E 后端是真实 `dotnet run --environment Integration`，只有 DatabaseInitializer 的
 * Integration 夹具（ApiKey + 工作流），那条日志在此进程里根本不存在（CI 表现为 waitForSelector 超时）。
 * 现改为：API 建一个**仅含 Start→End 的工作流**（无 LLM 节点，故不触真实模型、毫秒级完成）→
 * 运行 → 从列表按 workflowId 精确定位其执行日志 → 打开详情看回放诊断。
 */
const API = process.env.API_BASE || 'http://localhost:5000';

const { When, Then } = createBdd(test);

function uuid(): string {
  // Node 19+ 全局 crypto.randomUUID（CI 用 setup-node 20.x）。
  return globalThis.crypto.randomUUID();
}

When('我经 API 创建一个不含模型节点的工作流并运行 {string}', async ({ page, replay }, name: string) => {
  // 关键：Start/End 属结构节点、被编排器排除在可执行节点之外（SequentialOrchestrator.cs:378），
  // 不产生 ExecutionLogEntry；因此中间放一个 **Variable** 节点（纯内存 set，无 LLM）——
  // E2E 后端跑真实模型（F41 起无 stub 兜底），故必须完全避开模型节点。
  const startId = uuid();
  const varId = uuid();
  const endId = uuid();

  // 用 import 建流（只建不跑）：POST /workflows 会顺带执行一次，将留下两条日志且
  // TotalSteps 建档时恒 0（WorkflowStartedEventHandler:53 明示）无法区分 → 单条日志才确定。
  const imported = await page.request.post(`${API}/api/v1/workflows/import`, {
    data: {
      name,
      initialContext: '{}',
      nodes: [
        { id: startId, type: 0, name: 'E2E Start', position: { x: 0, y: 0 }, config: '{}' },
        { id: varId, type: 11, name: 'E2E Set Var', position: { x: 0, y: 120 },
          config: JSON.stringify({ mode: 'set', name: 'e2eMarker', value: 'replay-e2e' }) },
        { id: endId, type: 1, name: 'E2E End', position: { x: 0, y: 240 }, config: '{}' },
      ],
      edges: [
        { id: uuid(), source: startId, target: varId },
        { id: uuid(), source: varId, target: endId },
      ],
    },
  });
  expect(imported.ok(), `import failed: ${imported.status()} ${await imported.text()}`).toBe(true);
  const created = await imported.json();
  replay.workflowId = (created.id ?? created.workflowId) as string;
  expect(replay.workflowId).toBeTruthy();

  // 唯一的这一次运行 → 恰好一条执行日志（含 1 条 Variable 节点记录）
  const run = await page.request.post(`${API}/api/v1/workflows/${replay.workflowId}/run`, { data: {} });
  expect(run.ok() || run.status() === 202, `run failed: ${run.status()} ${await run.text()}`).toBe(true);
});

When('我打开该工作流最新的执行日志详情', async ({ page, replay }) => {
  expect(replay.workflowId).toBeTruthy();

  const list = await page.request.get(`${API}/api/v1/execution-logs?take=100`);
  expect(list.ok(), `list failed: ${list.status()}`).toBe(true);
  const body = await list.json();
  const items: Array<{ id: string; workflowId: string; startedAt: string }> = body.items ?? body;
  const mine = items
    .filter((i) => i.workflowId === replay.workflowId)
    .sort((a, b) => Date.parse(b.startedAt) - Date.parse(a.startedAt));
  // 本场景经 import 建流（不跑）+ 单次 run ⇒ 恰好一条日志；多于一条说明隔离被破坏。
  expect(mine.length, 'expected exactly one execution log for the scenario workflow').toBe(1);
  replay.logId = mine[0].id;

  await page.goto(`/execution-logs/${replay.logId}`);
});

Then('执行日志详情显示步骤明细标签', async ({ page }) => {
  await expect(page.getByRole('tab', { name: /步骤明细/ })).toBeVisible({ timeout: 15000 });
});

When('我切到回放诊断标签', async ({ page }) => {
  await page.getByRole('tab', { name: /回放诊断/ }).click();
});

Then('回放诊断显示执行路径时间线', async ({ page }) => {
  await expect(page.getByText(/执行路径/)).toBeVisible({ timeout: 15000 });
  // 报告条目来自真实日志记录：Start/End 是结构节点不写条目，故只断言被执行的 Variable 节点。
  await expect(page.getByText('E2E Set Var')).toBeVisible();
});

Then('回放诊断给出明确的结论横幅', async ({ page }) => {
  // 三态之一：有失败 / 无失败 / 信息不完整 —— 不允许空白或只 spinner。
  await expect(page.getByText(/个失败节点|未发现失败节点|信息不完整/).first()).toBeVisible();
});

Then('回放诊断披露数据缺口', async ({ page }) => {
  // 平台不落每节点真实入参 → 该缺口码恒存在，UI 必须显式提示而非静默留白。
  await expect(page.getByText(/数据缺口/)).toBeVisible();
  await expect(page.getByText(/真实入参未落库/)).toBeVisible();
});

When('我展开回放路径中的第一个节点', async ({ page }) => {
  const header = page.locator('.ant-collapse-header').first();
  await expect(header).toBeVisible();
  await header.click();
});

Then('节点详情显示输入输出与错误栏', async ({ page }) => {
  await expect(page.getByText('输入（推断）')).toBeVisible();
  await expect(page.getByText('错误', { exact: true }).first()).toBeVisible();
});
