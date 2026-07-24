# F3 · 页面交互打磨 — 质量门禁报告

> 分支：`feat/f3-page-polish`　|　feature-builder 全栈流水线　|　日期：2026-07-24
> 报告引用：`.quality-gate.json` → `docs/quality/f3-page-polish-gate.md`

## 概述

F3 为**纯前端** feature（无后端契约/文件变更），目标是消除列表/筛选/表单交互的打磨类缺陷：
B9 AgentConfigurations YAML 详情抽屉、B10 状态枚举前端映射、B11 Workflows 快速运行错误处理、
Conversations 搜索/状态筛选、O12 列表服务端分页、O13 请求取消与卸载安全。

根因澄清（B10）：`AgentPlatform.Api/Program.cs` 仅配置 `JsonNamingPolicy.CamelCase`、**未注册
`JsonStringEnumConverter`**，故所有枚举按**整数**序列化（`WorkflowState` Pending=0…RolledBack=5；
`ConversationStatus` Active=0/Closed=1/Archived=2）。原前端用小写字符串做 color map 的 key 永远 miss，
且筛选下拉裸传小写字面量。修复一律在**前端建状态映射表**，不改动后端序列化（避免契约破坏性变更）。

## 三道质量门禁结论

| 门禁 | 结论 | 摘要 |
| --- | --- | --- |
| ddd-code-reviewer | **PASSED** | 对抗式审查 F3 前端改动；发现 1 P3（ConversationsPage `getKnowledgeBases` 未透传 AbortSignal，取消不对称）→ 已修复；P0/P1/P2 = 0。前端 `node scripts/qa.mjs` 四道闸门全 PASS |
| ddd-phase-quality-gate | **PASS** | P0=0 P1=0 P2=0 P3=0。12 类审计中 .NET DDD 专属（DI/EF/Swagger/XML/蓝图漂移）因无后端改动全空；前端相关（硬编码值 / 并发 AbortController / 死代码 / 空值守卫）全扫 0 open。checklist 已嵌入 `features/page-polish.md` §6 |
| codebase-optimizer | **PASSED** | Round F3-01，0 open。七维度扫描 F3 前端改动：架构复用 `Card/PageHeader/StatusBadge/mapWorkflowStatus`、代码质量 0 `any`+strict、正确性 错误兜底+取消、测试 新增 `e2e/page-polish.spec.ts`、性能 服务端分页+AbortController、安全 无 XSS/无硬编码密钥、工程化 lint 净/无死代码 |

## 模型一致性校验（Phase 3）

- 后端 `GET /agent-configurations`、`/workflows`、`/execution-logs` 已支持 `skip/take/totalCount`；
  `getAgentConfigurations/getWorkflows/getExecutionLogs` 已对齐透传。
- `api.ts` 新增 `AbortSignal` 参数（4 个列表 getter）+ `status` 参数放宽 `string | number` 以接纳整数枚举值。
- 状态字段（整数枚举）由 `src/status.ts` 单一事实源映射为标签+色，前后端枚举序严格一致。
- `tsc --noEmit` + `node scripts/qa.mjs`（typecheck/lint/build/unit）**全绿**。

## 改动文件清单

- `src/AgentPlatform.Web/src/status.ts`（新增）— 状态枚举↔展示 单一事实源。
- `src/AgentPlatform.Web/src/services/api.ts` — 4 个列表 getter 支持 `signal`；`status` 参数放宽。
- `src/AgentPlatform.Web/src/pages/AgentConfigurationsPage.tsx` — B9 YAML 抽屉 + O12 + O13。
- `src/AgentPlatform.Web/src/pages/ExecutionLogsPage.tsx` — B10 + O12 + O13。
- `src/AgentPlatform.Web/src/pages/WorkflowsPage.tsx` — B10/B11 + O12 + O13。
- `src/AgentPlatform.Web/src/pages/ConversationsPage.tsx` — 搜索/状态筛选 + O13。
- `src/AgentPlatform.Web/e2e/smoke.auth.spec.ts`、`create-agent.spec.ts` — 修正为 F2 cookie 鉴权（消除历史漂移）。
- `src/AgentPlatform.Web/e2e/page-polish.spec.ts`（新增）— F3 交互冒烟。

## QA 结果与 e2e 说明

- 前端四道闸门（typecheck / lint / build / unit）：**PASS**（qa.mjs OVERALL PASS）。
- **e2e 闸门（Phase 4）本次会话未执行**：需要本地后端（`:5000`）+ Playwright Edge 浏览器，且
  `Program.cs` 启动时会因 `Security:JwtSecretKey` 为 dev 默认而抛错、需环境变量覆盖。本会话未拉起
  后端实时运行 e2e。已将 stale 的 e2e 规格修正为当前 cookie 鉴权，并新增 `page-polish.spec.ts`，
  待后端可用时即可由 `node scripts/qa.mjs --e2e` 执行。此为环境限制，非 feature 缺陷。

## 已知残留（非阻断）

- e2e 实时闸门需后端环境（见上）；规格已就绪。
- Conversations 搜索/筛选为客户端（后端 `/conversations` 返回全量数组、无分页/筛选参数），符合现状、不越界。
