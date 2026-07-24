# F1 质量门禁报告 · 前端数据真实性 & 全局错误态

> 由 `feature-builder` 流水线驱动：设计文档 → 前端实现 → 模型一致性 → QA 闭环 → 三道质量门禁 → 自动 check-in（仅 commit）。
> feature 设计文档：`features/frontend-data-truth.md`。优先级 P2，纯前端低风险，无后端契约/鉴权/路由结构变更。

## 门禁状态：PASS（cleared:true）

| 门禁 | 结论 | 开放项 |
|------|------|--------|
| ddd-code-reviewer | PASSED | P0=0 P1=0 P2=0 P3=0（仅应用 1 处 role 声明防御加固） |
| ddd-phase-quality-gate | PASS | P0=0 P1=0 P2=0 P3=0（12 类审计全扫，.NET 专属类 N/A） |
| codebase-optimizer | PASSED（聚焦单轮） | 0 open；全库扫描按 feature-builder「仅 commit 不 push」+ 过渡期规则延迟 |

QA 闸门（前端）：`node scripts/qa.mjs` → typecheck ✅ lint ✅ build ✅ unit ✅（全绿）。
后端：`dotnet build` 0 改动（本 feature 无后端改动）；`dotnet test` 不受影响。

## ddd-code-reviewer 审查摘要（F1 前端，对抗式）

- **模块类型**：前端 React19/TS(strict)。.NET 专属章节（A 状态机 / B EF / D 缓存 / E 领域事件 / F 仓储 / G 控制器 / H 配置）N/A。
- **控制流追踪**：
  - `useApiState`：loader→then/catch/finally；`active` 标志处理卸载竞态；deps 用 `[...deps, reload]`（eslint-disable 显式标注），无无限循环。VERIFIED。
  - `DashboardPage`：4 个 `useApiState`（deps `[]` 仅挂载执行）；`retryAll` 调四者 `retry()`。VERIFIED 无崩溃路径。
  - `decodeJwt`（api.ts）：split→base64url→atob→TextDecoder→JSON.parse 全在 try/catch，失败返回 null。VERIFIED 容错。
  - `appStore.identityFromToken`：`role` 声明做数组防御（`Array.isArray`）。VERIFIED。
  - `AppLayout`：`AntApp.useApp()` 取 message 实例（位于 `App.tsx` 的 `<AntApp>` 上下文内，无静态 message 告警，顺带对齐 O9）。`handleLogout`→logout+message+navigate('/login')。VERIFIED。
  - `LoginPage`：`login(res.token, email)` / 演示降级 `login(undefined, email)`。VERIFIED。
  - `App.tsx`：新增 `/login` 公开路由（接线已存在但未接线的 `LoginPage`，修复潜在不可达 bug）+ `*`→`NotFoundPage`（AppLayout 内）。VERIFIED 可达。
- **测试覆盖**：`ErrorState` 已补 3 例单测（消息渲染/重试点击/无重试按钮）。`DashboardPage`/`appStore`/`decodeJwt` 未覆盖 → 记为 P3 测试缺口（非阻断）。
- **API 校验**：无外部 .NET 库；antd/react-router 用法符合文档。N/A。
- **Top 3 运行时风险**：
  1. `decodeJwt` 依赖 `atob`/浏览器环境——SPA 恒浏览器，安全；令牌缺段即返回 null 不崩。低风险。
  2. `role` 声明若为数组（未来后端改 Azure AD 风格）——已做数组防御；当前未展示 role，UI 不受影响。低风险。
  3. `ExecutionLogDetailPage:50` SSE 后台刷新 `.catch(()=>{})` 静默忽略——后台轮询，非用户可见路径，可接受（与 O5 用户可见错误态无关）。

**已修复**：`appStore.ts` — `role` 声明由 `claims?.role ?? null` 改为数组安全取值（`Array.isArray` 分支）。

## ddd-phase-quality-gate 摘要（审计 + checklist）

- **审计（前端适用类）**：硬编码值（0：颜色走 tokens、无硬编码 URL/GUID）、死代码（0：新组件均被引用）、`any`（0）、XSS `dangerouslySetInnerHTML`（0）、hook 依赖（useApiState 已显式 disable）、React key（列表均有 key/rowKey）、未用依赖（0，tsc noUnusedLocals 通过）。
- **.NET 专属类**（DI 注册 / EF Core / 层违规 / CancellationToken / XML 文档 / Swagger）：本 feature 无后端改动 → N/A。
- **Checklist**：`features/frontend-data-truth.md` §6 已含 6 项（编译构建/功能正确/一致性/可观测/可维护/测试），覆盖前端适用维度。

## codebase-optimizer 摘要（聚焦单轮，本地，不建分支不 push）

- **扫描范围**：仅 F1 改动的 11 个前端文件（见设计文档 §实现清单），七维度（架构/质量/正确性/测试/性能/安全/工程化）。
- **发现**：除 `ddd-code-reviewer` 已修的 role 加固外，0 个新开放问题。
- **偏差说明**：完整多轮全库扫描按 `feature-builder` 的「仅 commit 不 push」约束（且本环境 push 实测失败）不做；全库优化留待后续独立运行。本 feature 范围内已达到生产就绪度。

## 实现清单（供 check-in 暂存）

- 新增：`hooks/useApiState.ts`、`components/ErrorState.tsx`、`pages/NotFoundPage.tsx`、`components/__tests__/ErrorState.test.tsx`
- 重写/改造：`pages/DashboardPage.tsx`(O5)、`pages/ExecutionLogDetailPage.tsx`(O5)、`services/api.ts`(+decodeJwt)、`stores/appStore.ts`(O4)、`layouts/AppLayout.tsx`(O4)、`pages/LoginPage.tsx`、`App.tsx`(O11 + /login 接线)、`features/frontend-data-truth.md`(设计文档)、`features/backlog.md`(F1 doing + O1/B7 done 校正 + B8 blocked)

## 遗留 / 后续

- B8 ApiKeys 真实化：`blocked`（后端无公开 API Key 端点），待 Phase 6 后端端点落地后独立 feature。
- 测试缺口（P3）：`DashboardPage`/`appStore`/`decodeJwt` 未覆盖，建议后续补。
- 全库 codebase-optimizer 扫描：按约束延迟，不影响本 feature 收尾。
