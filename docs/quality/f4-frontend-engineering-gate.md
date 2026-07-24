# F4 · 前端工程化（性能/可维护性/可访问性）— 质量门禁报告

> 分支：`feat/f4-frontend-engineering`　|　feature-builder 全栈流水线　|　日期：2026-07-24
> 报告引用：`.quality-gate.json` → `docs/quality/f4-frontend-engineering-gate.md`

## 概述

F4 为**纯前端** feature（无后端契约/文件变更），目标是补齐前端的工程化短板：
O6 路由级拆包（`React.lazy` + `manualChunks`）、O9 静态 `message` → `App.useApp()`、
O10 死代码清理（`userRole` 字段）、O14 可访问性（`aria-label`）、O7 关键页单测。

现状核对：F1–F3 已修复了本史诗最初引用的多处漂移（导航已用 antd `Menu` 而非 `div onClick`、
`ConversationsPage` 搜索已接服务端、编辑器节点已可编辑/删除）。本 feature 仅做剩余真实的工程化缺口，
不重复实现、不引入新路由/鉴权结构。

## 三道质量门禁结论

| 门禁 | 结论 | 摘要 |
| --- | --- | --- |
| ddd-code-reviewer | **PASSED** | 对抗式审查 F4 前端改动；详查 3 处最易出错点（lazy 页面 `App.useApp()` 上下文合法性、`useApiState` 重跑不依赖 `loader` 标识故无无限循环、`manualChunks` 函数式在 Windows 路径下的匹配）——均无缺陷，0 open。前端 `node scripts/qa.mjs` 四道闸门全 PASS |
| ddd-phase-quality-gate | **PASS** | P0=0 P1=0 P2=0 P3=0。12 类审计中 .NET DDD 专属（DI/EF/Swagger/XML/蓝图漂移）因纯前端改动全空；前端相关（硬编码值 `theme/tokens` / 并发 zustand 不可变更新 / 死代码 `userRole` 已清 / 空值守卫 `strict`+`?.`）全扫 0 open。checklist 已嵌入 `features/frontend-engineering.md` §6 |
| codebase-optimizer | **PASSED** | Round F4-01，0 open。七维度扫描 F4 前端改动：架构 复用 `Card/PageHeader/StatusBadge/AntApp`、代码质量 0 `any`+`strict`（grep 全仓 0 处 `any`/`@ts-ignore`）、正确性 错误兜底经 `App.useApp()`、测试 新增 4 单测、性能 路由级懒加载+供应商分包、安全 0 `dangerouslySetInnerHTML`/无硬编码密钥、工程化 `lint` 净/无死代码。`console.*` 命中均为合法错误日志（ErrorBoundary/API/SSE），非桩非死代码 |

## 模型一致性校验（Phase 3）

- 纯前端 feature，无后端 DTO/契约变更；一致性校验等价于类型与构建通过。
- `tsc --noEmit` **0 error**（含 `verbatimModuleSyntax`/`noUnusedLocals` 严格）；`vite build` 成功产出分包。
- 8 个页面的 `message` 静态调用全部改为 `App.useApp()`（grep 全仓 0 处静态 `message.`，9 处 `useApp`），
  消除 antd 静态 message 的 context 丢失告警。

## 改动文件清单

- `src/AgentPlatform.Web/vite.config.ts` — `manualChunks` 函数式：拆 `react-vendor` / `antd` / `xyflow` 三块供应商分包（O6）。
- `src/AgentPlatform.Web/src/App.tsx` — 全部页面改 `React.lazy` + `<Suspense fallback={<Spin/>}>`（O6）。
- `src/AgentPlatform.Web/src/pages/LoginPage.tsx`、`WorkflowCanvasPage.tsx`、`ApiKeysPage.tsx`、
  `ConversationDetailPage.tsx`、`ConversationsPage.tsx`、`KnowledgeBaseDetailPage.tsx`、
  `KnowledgeBasesPage.tsx` — `message` 静态导入改 `App.useApp()`（O9）。
- `src/AgentPlatform.Web/src/stores/appStore.ts` — 移除从未被读取的死字段 `userRole`（接口 + 5 处赋值）（O10）。
- `src/AgentPlatform.Web/src/layouts/AppLayout.tsx`（侧栏折叠按钮）、
  `src/AgentPlatform.Web/src/pages/ConversationsPage.tsx`（会话搜索）、
  `src/AgentPlatform.Web/src/pages/ConversationDetailPage.tsx`（聊天输入）— 补 `aria-label`（O14）。
- `src/AgentPlatform.Web/src/stores/__tests__/appStore.test.ts`（新增）— 鉴权态迁移 5 例。
- `src/AgentPlatform.Web/src/hooks/__tests__/useApiState.test.ts`（新增）— 加载/错误/retry/卸载安全 4 例。
- `src/AgentPlatform.Web/src/pages/__tests__/LoginPage.test.tsx`（新增）— 渲染/演示登录/401 失败 3 例。
- `src/AgentPlatform.Web/src/pages/__tests__/NotFoundPage.test.tsx`（新增）— 404 文案+返回 3 例。

## 构建产物验证（O6）

`vite build` 产出独立 chunk（大小降序）：

| chunk | 大小 |
| --- | --- |
| antd | ~1.0 MB（独立可缓存供应商） |
| react-vendor | ~220 KB |
| xyflow | ~176 KB |
| api | ~47 KB（共享） |
| WorkflowCanvasPage | ~15 KB（懒加载） |
| 其余页面 | 1–4 KB（懒加载） |
| index（主包） | ~9 KB |

首屏不再有单 chunk 1.38MB；供应商与页面按需并行加载。

## QA 结果与 e2e 说明

- 前端四道闸门（typecheck / lint / build / unit）：**PASS**（`node scripts/qa.mjs` OVERALL PASS）。
- **e2e 闸门（Phase 4）：未执行** —— 本项目 e2e 依赖本地后端（`:5000`）+ Web（`:5180`）实例，
  本沙箱无可用后端，故仅跑 `typecheck/lint/build/unit` 四道可离线闸门。路由级懒加载不影响既有
  e2e 规格（Playwright `waitFor` 文本即可等待 chunk 就绪）；待有后端环境补跑 `node scripts/qa.mjs --e2e`。

## 已知残留（非阻断）

- 画布节点面板为拖拽式 `div`（`NodePalette`），键盘不可达，属 DAG 画布交互范畴，超出 F4 工程化范围，记录为已知限制，未在此修。
- e2e 需后端环境补跑（见上）。
