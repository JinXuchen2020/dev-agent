import { test as base } from 'playwright-bdd';

/**
 * 每个场景独立的错误收集器。挂在 page 上（而非模块级变量），
 * 保证 fullyParallel 下多场景并行时各场景错误互不串扰。
 */
export type FlowErrors = {
  jsErrors: string[];
  httpErrors: { status: number; url: string }[];
};

export const test = base.extend<{ flowErrors: FlowErrors }>({
  flowErrors: async ({ page }, use) => {
    const flowErrors: FlowErrors = { jsErrors: [], httpErrors: [] };
    page.on('pageerror', (e) => flowErrors.jsErrors.push(e.message));
    page.on('response', (r) => {
      if (r.status() >= 400) flowErrors.httpErrors.push({ status: r.status(), url: r.url() });
    });
    await use(flowErrors);
  },
});
