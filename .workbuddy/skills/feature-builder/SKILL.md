---
name: feature-builder
description: 全栈 feature 端到端自主开发流程（AgentPlatform 项目：.NET 9 后端 + React19/TS/Vite 前端）。从 features/ 设计枢纽取一个 feature（或用户即时指令），完成后端（Domain/Application/Infrastructure/Api）+ 前端（AgentPlatform.Web）+ 前后端联调与模型一致性校验，再依次跑 ddd-code-reviewer、ddd-phase-quality-gate、codebase-optimizer 三道质量门禁，全绿后同步更新项目文档（CHANGELOG/BLUEPRINT/appendices 等至最新实现），落 .quality-gate.json（cleared:true）并自动 git commit（不含 push）。**前端 E2E 由 CI (`ci.yml` 的 `e2e` job) 驱动，本地不再跑 E2E**。This skill should be used when 用户要求「端到端实现一个完整 feature」「从 backlog 取任务做全栈开发并自动 check-in」「开发前后端联动功能且保证模型一致性」。
agent_created: true
---

# feature-builder — 全栈 feature 端到端自主开发

本 skill 是「功能自主迭代」架构的**全栈生成层**：在已建的质量门禁（守门员）之上，让代理自主把一个 feature 从设计文档变成可运行、经验证、并经三道质量门禁后安全 check-in 的真实功能。是 `feature-dev`（仅前端）的全栈兄弟 skill。

## 何时用
- 用户要求「端到端实现 X feature」「从 backlog 取任务做全栈开发并自动 check-in」「做一个前后端联动功能」。
- 用户希望一次性完成后端 + 前端 + 联调 + 模型一致性 + 质量门禁 + 提交。
- 不适用：纯前端小修 → 用 `feature-dev`；仅做代码审查/优化 → 直接用对应质量 skill。

## 前置约定（项目硬约束，必读）
1. **features/ 是跨栈设计枢纽**：任何新 feature **先写设计文档** `features/<feature-id>.md`（目标 / 前后端接口契约 / 数据模型 / 验收标准 / 风险点），再进入实现。这是红线，不是可选项。
2. **质量门禁契约**：改动 `src/` 的提交必须把 `.quality-gate.json` 一起暂存，且 `cleared:true`、含 `codebaseOptimizer` 字段，否则 pre-commit 拒绝提交。详见 `references/quality-gate-contract.md`。
3. **提交信息必须含 `Quality-Gate:` 行**（pre-commit 约定）。
4. 后端分层、前端设计令牌、API camelCase 约定见下方「复用清单」。
5. **每个 feature 独立分支（硬约束）**：开始任何开发动作（含写设计文档、改 src/、跑质量门禁）之前，**先新建并切换到专属分支** `feat/<feature-id>`（例：F1 → `feat/f1-frontend-data-truth`），整个流程的所有改动、设计文档、质量报告、`.quality-gate.json` 全部落在该分支。分支从当前所在分支（触发前建议先切回 main/master）新建，**不主动 merge、不主动 push**（merge/push 由用户另行决定）。本约束的目的一是隔离每个 feature 的改动便于 review，二是避免像 F1 试跑那样误落在他人遗留分支（如 `codebase-optimizer/2026-07-23`）。
6. **完成 feature 后必须同步更新文档（硬约束）**：代码与三道质量门全绿后、check-in 之前，**必须**核对并把「现有项目文档」同步到最新代码，杜绝历史漂移。重点核查 `README.md`、`CHANGELOG.md`、`AGENT_PLATFORM_BLUEPRINT.md`、`appendices/*.md`、`features/backlog.md`、`docs/`；凡文档仍描述已被本 feature 改变的旧机制（如 `localStorage`+`Bearer`、`ASP.NET Core Identity`、`Refresh Token`、未实现的接口、`待阶段X落地`/`未完成` 的模块、昨是今非的 API 示例/DTO 字段）→ 必须改为真实实现；`CHANGELOG.md` 顶部补本 feature 版本条目。文档改动**随本 feature 一起 commit 在该 `feat/<feature-id>` 分支**（doc-only 提交也须在该分支，绝不单独落 master/其他分支）。详见流程 Phase 5。
7. **前端 E2E 必须 BDD 驱动（硬约束）**：任何触及 UI 的 feature，必须配套 **playwright-bdd** 风格的 BDD 前端 E2E——在 `src/AgentPlatform.Web/e2e/features/*.feature` 写 Gherkin 场景，在 `src/AgentPlatform.Web/e2e/steps/*.steps.ts` 写步骤定义（用 `playwright-bdd` 的 `createBdd(test)`，`test` 必须 `extend` 自 `playwright-bdd` 自带的 `test`，**不能**用 `@playwright/test` 的 `test`）。**E2E 测试仅在 CI 中运行（`ci.yml` 的 `e2e` job）**，本地开发**不再跑 E2E**（去掉原 `node scripts/qa.mjs --e2e` 与 `bddgen && playwright test` 本地执行）。CI 中 `bddgen` → `playwright test` 全绿后才允许合入。既有的 `smoke.*.spec.ts` 属冒烟基线，不在本约束内。每个 UI feature 的 BDD E2E 至少覆盖一条核心用户路径（参考 `e2e/features/publish-workflow.feature`）。

## 流程（严格顺序）

### Phase 0 — 取 feature 与设计
0. **建专属分支（硬约束，第一动作）**：在确定 feature-id 后、做任何写操作之前，执行分支创建并切换：
   - 分支名 `feat/<feature-id>`（如 `feat/f1-frontend-data-truth`）。用小写、连字符分隔；feature-id 取 backlog 史诗 id 或用户指定短名。
   - 命令：`git checkout -b feat/<feature-id>`（或 `git switch -c feat/<feature-id>`）。若已在该分支则跳过。
   - **校验**：`git branch --show-current` 应返回 `feat/<feature-id>`，确认后续所有改动都在该分支上。此步失败（如分支已存在且非目标）→ 先 `git branch -D feat/<feature-id>` 再建，或改用带日期后缀的变体，但不得落在非 feature 分支上。
1. 读 `features/backlog.md` 取最靠前 `open` 任务；或采用用户当轮明确指令；或用户已提供的 `features/<feature-id>.md`。
2. 若还没有设计文档：**先写 `features/<feature-id>.md`**（含接口契约前后端双方、数据模型、验收标准、风险点）。
   - **高风险管理**：若 feature 涉及接口契约变更、鉴权/角色、路由结构、或对后端有破坏性改动、删数据 → 先把设计文档 + 选项汇报给用户确认，等明确指令后再动手（见 §护栏）。纯新增、不破坏既有契约的 feature 可直接进入实现。
3. 把 backlog 任务状态改 `doing`（Edit backlog 文件）。

### Phase 1 — 后端实现（DDD 分层）
- 顺序：Domain（实体/聚合/值对象/领域事件）→ Application（CQRS handler / 接口定义）→ Infrastructure（EF 配置 / 仓储 / 外部服务实现）→ Api（Controllers / DTO）。
- 新聚合或表变更 → **必须** `dotnet ef migrations add`（注意：EF 工具生成 block-scoped namespace，项目强制 file-scoped，需加 `#pragma warning disable IDE0161`；`dotnet-ef` 在 `~/.dotnet/tools`，使用前 `export PATH="$HOME/.dotnet/tools:$PATH"`）。漏迁移会导致开发/现网 SQLite 拿不到表/列。
- API DTO：明确请求/响应模型，字段 **camelCase**；用 `[Authorize(Roles=...)]` 标注所需角色。
- 复用既有设施：TenantProvider（per-request 多租户）、ApiKeyEncryptionService、审计 handler、IVectorStore 等。不写死密钥/连接串，用配置。
- 不重写基础设施，不重构无关代码。

### Phase 2 — 前端实现
- 复用：路由 `src/App.tsx`、API 契约 `src/services/api.ts`、设计令牌 `src/theme/tokens.ts`、组件 `src/components/`、类型 `src/types/index.ts`。
- 加 UI 复用 Card / PageHeader / StatusBadge / StatCard / BarChart；新页面接 `ProtectedRoute`；表单用 antd `Form` + `Modal`。
- 后端 JSON 为 camelCase，请求体用 camelCase。

### Phase 3 — 模型一致性校验（本 skill 核心新增）
保证前后端数据契约对齐，是 check-in 前的硬门槛：
1. 列出本 feature 涉及的所有后端 DTO / 响应模型（Controller 入参/出参、Application 契约）。
2. 对齐前端 `src/types/index.ts` 与 `src/services/api.ts`：字段名、类型、可空性、枚举值逐一对应。
3. 若后端暴露 OpenAPI/Swagger（`/swagger/v1/swagger.json` 或 NSwag 生成客户端）→ 拉取并 diff；否则人工逐字段比对。
4. 运行 `tsc --noEmit` 确保前端类型编译通过；运行 `dotnet build` + 相关单测确保后端编译/测试通过。
5. 联调：本地起 Api（默认端口）+ Web dev server（5173，proxy 到 Api）。新增受保护路由须在 `e2e/smoke.auth.spec.ts` 的 `PROTECTED` 列表里，**新 UI 交互需在 CI 的 BDD E2E 中覆盖**（在 `e2e/features/*.feature` + `e2e/steps/*.steps.ts` 中编写，由 CI 的 `e2e` job 运行 `bddgen && playwright test` 验证，见硬约束 #7）。

### Phase 4 — 质量门禁（三道，顺序跑，check-in 前置硬条件）
依次调用以下独立 skill（通过 Skill 工具），按其指引执行并消费结论，每道门跑完修复至通过：
1. **ddd-code-reviewer** —— 对抗式代码审查，消灭 open findings 至 0。
2. **ddd-phase-quality-gate** —— 阶段结构门（P0/P1/P2/P3 = 0 open；把 checklist 嵌入 feature 设计文档 §6）。
3. **codebase-optimizer** —— 多轮代码优化（stub 替换/生产就绪），跑到 0 open。
- 三道门结论写入 `.quality-gate.json`（字段与格式见 `references/quality-gate-contract.md`）：`phase`=feature-id、`reviewer`/`structureGate`/`codebaseOptimizer` 各填 PASSED 摘要、`cleared`=true、`reportRef`=`docs/quality/<feature-id>-gate.md`、`notes`=实现摘要（Phase 5 过渡期 `codebaseOptimizer` 可写 `not_run`，Phase 7 提交前需 `PASSED`）。
- 写 `docs/quality/<feature-id>-gate.md` 质量报告。

### Phase 5 — 文档同步（硬门槛，check-in 前置）
代码与三道质量门全绿后、Phase 6 提交之前，**必须**把现有项目文档同步到最新代码，杜绝历史漂移（feature-builder 硬约束 #6，不是可选项）：
1. **核查范围**：`README.md`、`CHANGELOG.md`、`AGENT_PLATFORM_BLUEPRINT.md`、`appendices/*.md`、`features/backlog.md`、`docs/`。用 Grep 扫关键词定位漂移：被本 feature 改变的旧机制描述（如 `localStorage`+`Bearer`、`ASP.NET Core Identity`、`Refresh Token`、`auth/refresh`、未实现的接口、`待阶段X落地`/`未完成` 模块、昨是今非的 API 示例 / DTO 字段 / 聚合清单）。
2. **改到真实实现**：凡文档仍描述已被本 feature 改变的旧行为 → 改为真实代码现状；本 feature 新增的聚合 / 接口 / 配置须补进对应文档（如 `appendices/core-aggregates.md` 聚合清单、`appendices/api-spec.md` 接口表、`AGENT_PLATFORM_BLUEPRINT.md` 对应小节）。
3. **CHANGELOG**：顶部补本 feature 版本条目（日期、改动点、质量门结果、关键测试数、已知残留）。
4. **doc-only 也落 feature 分支**：文档改动随本 feature 一起 `git add` 并在 Phase 6 该 feature 分支提交；若量较大拆第二个 commit，也**必须在该 `feat/<feature-id>` 分支**，绝不单独落 master/其他分支。
5. **不造文档**：只同步真实存在的代码状态，不写未实现的功能、不夸大。

### Phase 6 — 自动 check-in（仅 commit，不 push）
- 当前已在 Phase 0 建好的 `feat/<feature-id>` 分支上，本阶段**不切换分支**，commit 自然落在该 feature 分支（含 Phase 5 的文档同步改动）。
1. `git add` 所有 src/ 改动 + `.quality-gate.json` + 设计文档 + 质量报告 + **Phase 5 的文档同步改动**（**必须一起暂存**，pre-commit 才放行）。
2. 提交信息格式：
   ```
   feat(<feature-id>): <一句话描述>

   Quality-Gate: ddd-code-reviewer + ddd-phase-quality-gate + codebase-optimizer PASSED (cleared:true)
   - 后端：...
   - 前端：...
   - 模型一致性：字段/类型/枚举已对齐，tsc + 联调通过
   - 文档：已同步 CHANGELOG/BLUEPRINT/appendices 至最新实现
   ```
3. `git commit`（**不 push**；push 由用户另行决定）。
4. 失败处理：若 pre-commit 拒绝（.quality-gate.json 未带 / cleared 非 true / 缺 codebaseOptimizer 字段）→ 回到 Phase 4 补齐后重提。
5. 文档改动量较大时可拆第二个 commit（仍在该 feature 分支，信息标注 `docs:`），但**两个 commit 都不得落 master**。

### Phase 7 — 收尾
- backlog 任务标 `done`。
- 中文总结：做了什么、改了哪些文件、模型一致性如何校验、质量门是否全绿、**文档同步了哪些**、遗留风险。
- 重启本地 5173 dev server 让改动对用户生效（proxy 等配置类改动必须重启）。

## 护栏（不可越界）
- **高风险停下问人**：接口契约变更、鉴权/角色、路由结构、破坏性后端改动、删数据 → 先汇报选项等确认，不自动改。
- 不破坏现有功能：三道质量门全绿才算完成；E2E 由 CI 验证。
- 不借机重构无关代码。
- 绝不自创需求（只做 backlog / 用户指令 / 设计文档里的）。
- 不 push（除非用户明确要求）；也不主动 merge 到其他分支——feature 分支保持独立，留给用户 review/合并。
- 模型一致性是硬门槛：前后端字段/类型/枚举必须对齐，tsc + 联调通过才算。

## 复用清单（减少决策）
- **后端**：分层 Architecture；TenantProvider（per-request 多租户隔离）；ApiKeyEncryptionService（AES-GCM）；审计 handler（业务 4 + KeyUsed/KeyRotation/KeyRevoked）；IVectorStore（PgVectorStore/InMemoryVectorStore，三方法带 tenantId）；EF 迁移铁律见 Phase 1。
- **前端**：主色 `#1A73E8`；语义 `success #34A853` / `warning #FBBC04` / `error #EA4335`；文本 `colors.textPrimary` / `colors.textMuted`；封装 `Card`（非 antd Card）、`PageHeader`（含 `actions` 槽）、`StatusBadge`、`StatCard`、`BarChart`；登录态 `appStore.isAuthenticated`（JWT 由 httpOnly Cookie `ap_access_token` 承载，**非 localStorage**；`api.ts` 用 `withCredentials`，401 派发 `auth:unauthorized` 事件，SSE 用 `credentials:'include'`）；e2e 用本机 Edge（`channel:'msedge'`）。
- **质量门**：ddd-code-reviewer / ddd-phase-quality-gate / codebase-optimizer 为本 skill Phase 4 调用对象；其结论契约见 `references/quality-gate-contract.md`。
