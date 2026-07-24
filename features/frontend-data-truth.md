# F1 · 前端数据真实性 & 全局错误态（feature 设计文档）

> 本文件是 `feature-builder` 的 Phase 0 产出，对应 `features/backlog.md` 的 Tier 1 史诗 F1。
> 优先级：P2。风险：低（纯前端，无后端契约/鉴权/路由结构变更，无破坏性改动）。
> 关联子项：B7 / O1 / O4 / O5 / O11（原 backlog 编号）；B8（ApiKeys 真实化）因后端无公开端点 → blocked。

## 1. 目标
让 UI 展示真实登录身份、失败时给出可见错误态与重试入口，消除「身份信息不展示/不同源」「静默吞错白屏」「无 404 兜底」三类前端数据真实性缺陷。

## 2. 现状核准（与 backlog 漂移校正）
经代码走查（2026-07-23），backlog 部分编号已漂移，实际状态如下：
- **O1（顶层 ErrorBoundary）**：✅ 已完成。`components/ErrorBoundary.tsx` 存在且已在 `App.tsx:32` 包裹全部 `<Routes>`。无需重做。
- **B7（Dashboard 假数据）**：✅ 已完成。`DashboardPage` 现走真实 `getAgents / getWorkflows / getExecutionLogs`，backlog 行号为旧版本。无需重做。
- **O4（装饰性搜索/租户切换）**：装饰元素已不存在于 `AppLayout`（顶栏仅留侧栏开关）。残留问题 = 真实登录用户身份未在界面体现（`userEmail` 恒为 null，刷新后丢失）。
- **O5（静默吞错）**：❌ 仍开放。`DashboardPage:16-19` 与 `ExecutionLogDetailPage:50` 用 `.catch(() => {})` 静默吞错；`KnowledgeBasesPage`/`WorkflowCanvasPage` 等已用 `message.error` 可见反馈（不计入）。
- **O11（404 兜底）**：❌ 仍开放。`App.tsx` 无 `*` catch-all，无 NotFound 页。

## 3. 前后端接口契约
**本 feature 不新增任何后端接口、不修改既有契约。** 复用：
- `GET /api/v1/agents`、`GET /api/v1/workflows`、`GET /api/v1/execution-logs`（Dashboard 真实指标）
- `GET /api/v1/execution-logs/{id}`（ExecutionLogDetail；错误态改造）
- `POST /api/v1/auth/dev-login` → 返回 `{ token }`，JWT 声明：`sub`/`name`=邮箱、`role`（见 `DevLoginEndpoint.cs:35-39`；注意 dev-login 令牌**无 tenant_id 声明**，故本 feature 不展示 tenant）
- 读 JWT 仅做客户端解码展示，**不修改鉴权方案**（httpOnly/SameSite 属 F2/O8，不在此处理）。

## 4. 数据模型 / 前端状态
- 新增 `useApiState<T>` hook：返回 `{ data, loading, error, retry }`，统一 loading/error/retry 语义，内部复用既有 `getErrorMessage`。
- 新增 `ErrorState` 组件：antd `Alert`(error) + 「重试」按钮，作为统一错误态出口。
- `appStore` 扩展：`userEmail`、`role`、`tenantId` 字段；`login(token?, email?)` 优先从 JWT 解码真实声明回填；store 初始化时若 localStorage 已有 token 则解码回填（解决刷新后身份丢失）。
- 新增 `NotFoundPage`：antd `Result`(404) + 返回首页按钮。

## 5. 验收标准
- [ ] Dashboard：任一指标接口失败时显示错误 Alert + 重试按钮，重试可恢复；不再有无提示白屏。
- [ ] ExecutionLogDetail：详情加载失败显示错误态 + 重试，而非静默空白。
- [ ] 任意未知路由（如 `/nope`）渲染 NotFound 页而非白屏。
- [ ] 登录后顶栏右侧展示真实邮箱（来自 JWT）；刷新页面后邮箱仍保留；「退出登录」可清 token 并跳登录页。
- [ ] `tsc --noEmit` / lint / build / unit 全绿；`node scripts/qa.mjs --e2e` 全绿。
- [ ] 无新增后端改动，既有后端测试不受影响。
- [ ] B8（ApiKeys 真实化）维持 blocked：后端公开端点（GET/POST/DELETE /api-keys、复制密钥）未就绪，标 blocked 不实现，待后端 F5/ApiKeys 端点落地后单独 feature。

## 6. 阶段质量 Checklist（ddd-phase-quality-gate 嵌入）
- [ ] P0 编译/构建：前端 `npm run build` 通过，无 TS/lint error。
- [ ] P0 功能正确：错误态、404、身份展示三项验收全部达成（手动/ e2e 验证）。
- [ ] P1 一致性：新增 hook/组件类型完备；无 `any`；与现有 `colors`/`message` 约定一致。
- [ ] P1 可观测：错误态含可读 message（经 `getErrorMessage`）；无 console 静默吞错残留于受改文件。
- [ ] P2 可维护：复用既有 `ErrorBoundary`/`tokens`，未引入重复错误态实现。
- [ ] P3 测试：受改页 QA 全绿；如新增组件则补最小单测（ErrorState/useApiState）。

## 7. 风险点
- 仅客户端解码 JWT 声明用于展示，**不做签名校验**（展示用途，后端仍是鉴权权威）；若令牌无 `email` 声明则回退到登录输入框邮箱。
- 不触碰 `auth_token` 存储方式（localStorage）→ 不引入 XSS 新风险；httpOnly 迁移归属 F2。
- NotFound 路由置于 `AppLayout` 内（带侧栏壳），保持导航一致；不新增受保护路由，故无需改 e2e `PROTECTED` 列表。
