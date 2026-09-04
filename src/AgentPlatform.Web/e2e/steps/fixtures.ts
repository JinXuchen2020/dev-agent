import { test as base } from 'playwright-bdd';

/**
 * 每个场景独立的错误收集器。挂在 page 上（而非模块级变量），
 * 保证 fullyParallel 下多场景并行时各场景错误互不串扰。
 */
export type FlowErrors = {
  jsErrors: string[];
  httpErrors: { status: number; url: string }[];
};

/**
 * F40 回放诊断场景的本地状态：跨步骤传递本场景自造的 workflowId 与定位到的执行日志 logId。
 * 放这里而非步骤文件内 extend，是因为 playwright-bdd 只认本文件导出的那一个 `test` 实例
 * （步骤里再 base.extend 会让 bddgen 报 "Can't guess test instance"）。
 */
export type ReplayState = {
  workflowId: string;
  logId: string;
};

export const test = base.extend<{ flowErrors: FlowErrors; replay: ReplayState }>({
  flowErrors: async ({ page }, use) => {
    const flowErrors: FlowErrors = { jsErrors: [], httpErrors: [] };
    page.on('pageerror', (e) => flowErrors.jsErrors.push(e.message));
    page.on('response', (r) => {
      if (r.status() >= 400) flowErrors.httpErrors.push({ status: r.status(), url: r.url() });
    });
    await use(flowErrors);
  },
  replay: async ({}, use) => {
    // 每用例一个全新可变对象 → 场景之间不串数据（并行安全）。
    await use({ workflowId: '', logId: '' });
  },
});
