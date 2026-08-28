# 前端 E2E 质量门 — agentic-run.feature「最终回答」超时（聚焦修复）

- **日期**：2026-08-28
- **关联 feature**：`src/AgentPlatform.Web/e2e/features/agentic-run.feature`（F29 自主智能体运行）
- **失败现象**：`Then 运行弹窗显示最终回答` → `page.getByText('最终回答', { exact: true })` 在 20s 内 `element(s) not found`；26 passed / 1 failed。
- **方向**：遵循用户裁定「E2E 用真实 key，不用 stub，文档需更新」。

## 根因（代码实证，非猜测）

1. **真实 LLM 延迟 > 20s 硬超时**：`AgentRunPage` 的真实运行路径为 `POST /api/v1/agents/{id}/runs/stream`（SSE）。后端 `AgenticOrchestrator.RunGoalStreamCoreAsync` 在「无工具调用即结束」分支会**连续发两次模型请求**：先 `RouteAsync`（探测工具调用），再 `RouteStreamAsync`（逐 token 返回最终答案）。真实 key 下单次调用在 CI 网络已有可见延迟（同批次 conversation E2E 真实 LLM 调用整体 ~1.4m），两轮叠加极易超过原 20s 超时 → 「最终回答」区块尚未渲染即被判失败。
2. **错误被静默吞没（误导性失败）**：若真实模型返回 429/限流或任何运行期异常，控制器写 `error` 事件 → 前端 `runError` 置位 → `AgentRunPage` 中「最终回答」区块（位于 `runSteps.length>0 || runAnswer || runSummary` 条件渲染块内）**永不渲染**。原断言只等成功文案，于是以「找不到最终回答」超时结束，掩盖了真实失败原因（如 429），诊断困难。

## 修复

文件：`src/AgentPlatform.Web/e2e/steps/agent.steps.ts`

将 `运行弹窗显示最终回答` 步骤由「死等单条文案 20s」改为「等待运行终态」：

- 用 `page.getByText('最终回答', { exact: true }).or(page.locator('.ant-alert-error'))` 等待**任一先可见**，超时放宽到 90s（覆盖真实 LLM 双调用延迟）。
- 若先出现错误告警（`runError` 置位），用 `error.innerText()` 取出真实失败原因并 `throw`，使 CI 直接报「智能体运行失败：<真实原因>」而非「找不到最终回答」。
- 不改变任何后端/前端业务代码，纯 E2E 断言加固；步骤名不变，既有 `.features-gen` spec 绑定不受影响。

## 质量门评估（聚焦修复，未做全库多轮扫描）

- **ddd-code-reviewer（对抗式审查）**：PASSED。变更仅 E2E 步骤断言逻辑；`Locator.or()`（Playwright 1.62.1 支持）+ `innerText()` 为标准 API；无新增接口/DI/聚合改动；0 open。
- **ddd-phase-quality-gate（结构门）**：PASSED。步骤为 playwright-bdd 标准 `Then` 定义，绑定既有 `AgentRunPage` 终态（成功区块 / `.ant-alert-error`）；无分层违规；0 open。
- **codebase-optimizer（七维度，scoped）**：PASSED (Round 1, 0 open, scoped to this fix)。架构（断言与运行终态语义一致）、代码质量（注释说明双调用延迟与错误吞没根因链）、正确性（错误先现时抛真实原因，避免静默超时）、测试（等待策略覆盖真实 LLM 延迟 + 错误诊断）、性能（仅放宽容忍度，无逻辑负担）、安全（无）、工程化（Playwright 1.62.1 兼容，无新依赖）。未执行全库多轮扫描（聚焦修复，同 prior `phase-6-frontend-e2e` 先例）。

## 验证建议

- 真实 key CI 重跑 `agentic-run.feature`：预期 27/27（原 26 passed + 本修复项通过）。
- 若仍失败且报「智能体运行失败：…」，则已定位为真实模型/路由问题（如 429 限流或 agent 工具循环），需查后端 `AgenticOrchestrator` 或 `ModelRouter` 运行日志，而非测试本身。

## 报告引用

`.quality-gate.json` → `reportRef: docs/quality/phase-6-frontend-e2e-agentic-run-gate.md`
