# 质量门 · 前端新增代码评审报告（frontend-additions）

- 评审对象：`src/AgentPlatform.Web/` 下本次新增的前端文件（LoginPage / ApiKeysPage / ConversationsPage / components / theme / test / e2e / 配置）及为使其可编译而补齐的基础层（`api.ts` / `types/index.ts` / `appStore.ts` / `AgentsPage` / `WorkflowDetailPage`）。
- 评审技能：`ddd-code-reviewer`（对抗式，清单适配 React/TS）+ `ddd-phase-quality-gate`（结构卫生，适配前端）+ 前端 QA 闭环 `scripts/qa.mjs`（typecheck/lint/build/unit，等价于 codebase-optimizer 的前端健康检查）。
- 结论：**cleared: true**，0 open findings。

## Findings（评审中发现的缺陷，已全部修复）

| Severity | Category | File:Line | Finding | Fix |
|----------|----------|-----------|---------|-----|
| P0 | 集成漂移 | ApiKeysPage / ConversationsPage / LoginPage | 新增页引用了 `api.ts`/`types`/`store` 中**不存在的导出**（`ApiKey`/`Conversation`/`getApiKeys`/`getConversations`/`createConversation`/`devLogin`/`isAuthenticated`/`login`），导致 typecheck 全量失败，前端根本无法编译。 | 在 `types/index.ts` 补 `ApiKey`/`Conversation`；`api.ts` 补 `devLogin`/`getApiKeys`/`getConversations`/`createConversation`；`appStore.ts` 补 `isAuthenticated`/`login`/`logout`/`userEmail`。 |
| P1 | 契约错位 | src/test/AgentsPage.contract.test.tsx | 契约测试用**扁平** `roleCode`/`modelProvider`/`modelName` 构造 `Agent`，与真实 `Agent`（`role:{roleCode}` + `modelEndpoint:{modelId}`）不符，测试恒失败且掩盖真实回归。 | 测试样本改为嵌套 `role:{roleCode}` + `modelEndpoint:{modelId}`，断言 `gpt-4o`（modelId）而非 `OpenAI / gpt-4o`。 |
| P1 | 类型缺口 | types/index.ts | `Agent` 缺 `modelEndpoint`，导致 `AgentsPage` 只能用 `as unknown as` 强转读取模型列。 | `Agent` 增加 `modelEndpoint?: { modelId: string }`，`AgentsPage` 移除强转。 |
| P2 | 死代码/误导 | LoginPage.tsx | 密码框被收集但**从不发送或校验**（后端 dev-login 仅校验邮箱），提示语「任意密码」属误导。 | 移除密码框与误导提示，明确「开发演示登录：admin@acme.io（免密）」。 |
| P2 | 健壮性 | ConversationsPage.tsx | `id.length` 与 `new Date(d)` 在 `id`/`createdAt` 缺失时会白屏。 | 加空值保护（`id ? ... : '-'`、`d ? ... : '-'`）。 |
| P2 | 死导入 | WorkflowDetailPage.tsx | `message` 被导入但从未使用（TS6133）。 | 移除未用导入。 |

## Control Flow Analysis
- 入口：`LoginPage.handleLogin` → `devLogin` → 成功写 `auth_token` + `login(email)`，失败降级本地演示会话。`devLogin` 当前命中后端 `/auth/dev-login`（需 `Security:DevLoginEnabled`）。
- Dead ends：无新增。ApiKeysPage 的「轮换/吊销」按钮仍显式提示「需后端 API 支持」（已在 `features/backlog.md` P2 跟踪，非隐藏桩）；ConversationsPage 行点击无详情路由（B5，属未实现 feature，非本提交范围）。
- Unregistered interfaces：无（前端无 DI 容器）。

## Test Coverage
- 单测：`statusTone` + `StatusBadge` 通过；`AgentsPage.contract` 修复后通过。
- 缺失边角：`getApiKeys`/`getConversations` 在后端无对应端点时走 `.finally` 空列表降级（已处理，不崩）；e2e 需 Playwright 浏览器，本次未跑（`qa.mjs` 默认不含 `--e2e`）。

## Top 3 Runtime Risks（评审时识别，均已修复或降级处理）
1. 集成漂移导致整前端编译失败 — 已通过补齐基础层导出解决（P0）。
2. `Agent` 模型列因类型缺口渲染空白 — 已补 `modelEndpoint` 并去强转（P1）。
3. 登录密码字段误导用户以为已校验 — 已移除（P2）。

## 不在本提交范围（已在 features/backlog.md 跟踪，非隐藏）
- B1 工作流编辑态失效、B2 SSE 无法带 JWT、B3 SSE 无限重连、B4 context 解析白屏（均位于已提交的历史文件）。
- O1–O14 优化项（ErrorBoundary、401 SPA 化、打包拆包、单测覆盖率等）。
- 这些为既有代码漂移，本次提交仅评审**新增代码**，不覆盖已入库历史文件的质量状态。
