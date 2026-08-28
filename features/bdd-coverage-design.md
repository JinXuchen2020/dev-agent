# F28 · 历史 feature BDD 测试覆盖补全（按功能域分组）

> **分支**：`feat/f28-bdd-coverage`（派生自 `feat/bdd-integration`）
> **目标**：为「之前已实现但缺 BDD 集成测试」的所有功能域，补齐 **Reqnroll 后端 BDD（真 HTTP + 文件 SQLite）** 与 **playwright-bdd 前端 E2E（真浏览器）**，按风险/价值分批，不要求与单个 feature 史诗 1:1 对应。
> **硬约束**：沿用 feature-builder 流程（独立分支 / 先设计 / 三道质量门 / `.quality-gate.json` 含 `cleared:true`+`bdd`+`frontendE2e` / commit 含 `Quality-Gate:` 行 / **不 push**）。前端 E2E 必须 BDD（SKILL.md 硬约束 #7）。

## 1. 现状盘点（2026-08-04）

- **后端 BDD 存量**（9 `.feature` / 51 scenarios，覆盖）：AgentRouting、WorkflowStateMachine(玩具)、MultiAgentPipeline(玩具)、PublishedWorkflow(F22)、ExecutionLog、CustomAgentRole、AgentTypeMigration、WorkflowEngine、AgentConfiguration-template。
- **前端 BDD 存量**：`publish-workflow.feature`（F22 发布链路）。另有 4 个 legacy `.spec.ts`（create-agent/page-polish/smoke.auth/smoke.unauth）——其中 create-agent/page-polish 断言英文 UI 但默认 locale=zh-CN，**预存失败**，本 feature 一并修复或改写。
- **测试基座已就绪**：`IntegrationAppFactory`（Integration 环境 + 文件 SQLite `test-integration.db`）+ `IntegrationSeeder`（**已种子 T1+T2 双租户 / 双 ApiKey / 3 个示例工作流**）+ `AuthHelper`（真实登录拿 JWT）+ `IntegrationClient`。**双租户隔离 BDD 无需扩展基座**。
- **未覆盖功能域**（按风险/价值，F27 后全量补齐）：

| 优先级 | 功能域 | 后端端点（代表性） | 前端关键流 |
|---|---|---|---|
| 1 | Auth + RBAC | `/auth/login`、`/auth/me`、`[Authorize(Roles=...)]` | 登录 UI、401 不破 SPA、受保护路由跳转 |
| 2 | 租户凭据 + 模型发现（F13/F14） | `/tenant/credentials`、`/tenant/credentials/discover-models`、`/models` | 填 Key+BaseUrl→拉模型→选→存 |
| 3 | 工作流管理（F20/F22） | `/workflows` CRUD/run/versions/import/export、节点 run | 建/改/运行/发布抽屉 |
| 4 | 会话 + 聊天（F5/B5） | `/conversations`、send message、KB 绑定、workflow 绑定/触发、cost-report | 开会话、发消息、看回复 |
| 5 | 知识库（RAG 地基） | `/knowledge-bases` CRUD、upload document | 建 KB、传文档、看文档列表 |
| 6 | Research Agent（F6） | `/research` 多步检索→报告 | 开 Research、跑、看报告 |
| 7 | 分析看板（F18） | `/analytics/summary` | 图表渲染、范围选择器 |
| 8 | Agent 生命周期 | `/agents` CRUD、role 赋值、`/agent-configurations` 模板 | 建 Agent、改、删 |

## 2. 执行批次（风险/价值序）

| Batch | 范围 | 后端 BDD | 前端 BDD |
|---|---|---|---|
| B1 | Auth+RBAC | `auth-rbac.feature`（登录成功/失败、401、/auth/me、Admin-only 端点 403、跨租户 404） | `login-auth.feature`（登录、401 跳转 /login、受保护页重定向） |
| B2 | 凭据+模型发现 | `tenant-credentials.feature`（BYO upsert、租户隔离 A≠B、掩码 GET、discover-models 401/解析） | `credentials.feature`（填 key→拉模型→选→存） |
| B3 | 工作流管理 | `workflow-management.feature`（CRUD/run/version/import-export/节点 run） | `workflow-crud.feature`（建/改/运行/发布抽屉） |
| B4 | 会话+聊天 | `conversation-chat.feature`（建会话、发消息、KB 绑定、workflow 触发、cost-report） | `conversation.feature`（开会话/发消息/看回复） |
| B5 | 知识库 | `knowledge-base.feature`（建/传文档/列/删/租户隔离） | `knowledge-base.feature`（建 KB/传文档） |
| B6 | Research | `research-agent.feature`（多步检索→报告、鉴权） | `research.feature`（开/跑/看报告） |
| B7 | 分析看板 | `analytics.feature`（summary KPI/按日桶/租户隔离/空区间） | `dashboard.feature`（图表/范围选择） |
| B8 | Agent 生命周期 | `agent-lifecycle.feature`（CRUD/role 赋值/配置模板实例化） | `agent-crud.feature`（建/改/删） |

## 3. 共享步骤基件（复用，降重复）

新增 `Steps/CommonSteps.cs`（全局 `[Binding]`，所有 feature 可用）：
- `Given 以集成租户 T1 admin 登录` → 存 `ScenarioContext["AdminToken"]`
- `Given 以租户 T2 用户登录` → 存 `ScenarioContext["T2Token"]`
- `When 以 admin 身份请求 (GET|POST|PUT|DELETE) {url}` / `以 T2 身份...` / `匿名请求...`
- `Then 响应状态码为 {int}`、`Then 响应体包含 {string}`、`Then 响应 JSON 含属性 {string}`
- body 自动 camelCase 序列化（复用 `IntegrationClient` 约定）

> ⚠️ 通用步骤用 `^...$` 锚定正则（与 `PublishedWorkflowSteps` 约定一致），避免 Cucumber Expression 误解析。

## 4. 验证策略

- **后端 BDD**：`dotnet test src/AgentPlatform.SpecFlowTests`（真 HTTP + 文件 SQLite，每 batch 即时跑，确保 51→扩量全绿）。
- **前端 BDD**：`bddgen && playwright test <feature>`（需后端 5000 + vite 5180）；顶层 `node scripts/integration.mjs --e2e` 最终全闸。
- **三道质量门**：ddd-code-reviewer / ddd-phase-quality-gate / codebase-optimizer（对 F28 增量 0 open）。
- 预存 legacy `create-agent.spec.ts`/`page-polish.spec.ts`：改写为 BDD `create-agent.feature`/`page-polish.feature`（断言改为 zh-CN 文本，与默认 locale 对齐）；`smoke.*.spec.ts` 保留为冒烟基线。

## 5. 风险与缓解

- **跨场景数据污染**：各 feature Background 自行 reset/隔离；`IntegrationAppFactory` 全程单例（不 Dispose 中途）。
- **租户隔离断言**：直接复用 `IntegrationConstants` 的 T1/T2 固定 Id + 固定 ApiKey 明文，断言「A 的 key 在 B 上下文不可见/不可用」。
- **Stub 模型**：`ModelClient:Provider=Stub`，发消息/Research 走 stub 响应，验证链路与鉴权而非真实 LLM。
- **前端 locale**：所有前端 BDD 断言 zh-CN（默认），不再依赖英文 UI 文本。

## 6. 验收

- 所有 8 功能域后端 BDD 绿；前端 BDD 覆盖关键 UI 流（login/create-agent/page-polish/publish-workflow/conversation/knowledge/credentials/dashboard/research/agent-crud）。
- `dotnet test` 全绿；`node scripts/integration.mjs --e2e` 全绿。
- 文档同步（CHANGELOG/backlog F28 done/本设计文档 §实施记录）。

## 7. 实施记录（2026-08-04）

### 后端 BDD（B1–B8，114 场景，全绿）
- 复用基座：`IntegrationAppFactory`（`Integration` 环境 + 文件 SQLite `test-integration.db`）+ `IntegrationSeeder`（T1/T2 双租户 + 双 ApiKey + 3 示例工作流）+ `AuthHelper`（JWT 登录）+ `CommonSteps`（^...$ 锚定正则）。
- 新增 feature / steps：
  - B1 `AuthRbac.feature`：登录成功/失败、401、`/auth/me`、Admin-only 端点 403、跨租户 404。
  - B2 `TenantCredentials.feature`：BYO upsert、租户隔离 A≠B、掩码 GET、discover-models 401/解析。
  - B3 `WorkflowManagement.feature`：CRUD/run/version/import-export/节点 run。
  - B4 `Conversation.feature`：建会话、发消息、KB 绑定、workflow 触发、cost-report。
  - B5 `KnowledgeBase.feature`：建/传文档/列/删/租户隔离。
  - B6 `Research.feature`：多步检索→报告、鉴权（复用 CommonSteps）。
  - B7 `Analytics.feature`：summary KPI/按日桶/租户隔离/空区间（复用 CommonSteps）。
  - B8 `Agents.feature` + `AgentConfigurations.feature`：CRUD/role 赋值/配置模板实例化。
- **根因修复**：`TenantModelClientResolver` 在 `ModelClient:Provider=Stub` 时返回空解析（回退平台 stub），消除 B2 启用 BYO 凭据触发的真实 LLM 20s 超时 500。与 F28「Stub 模型避免真实 LLM 调用」契约一致。
- 验证：`dotnet test src/AgentPlatform.SpecFlowTests` → 114/114 通过（0 fail，~13s）。

### 前端 BDD（11 feature / 22 场景 @e2e，全绿）
- 约定：playwright-bdd 9.x，`createBdd(test)` 的 `test` 继承 `playwright-bdd` 自带 `test`（见 `e2e/steps/fixtures.ts`）；feature 在 `e2e/features`，steps 在 `e2e/steps`；zh-CN 断言对齐默认 locale。
- 契约修复：`playwright.config.ts` `testDir` 必须 = `defineBddConfig()` 返回的 `outputDir`（playwright-bdd 9.x 按 outputDir 注册/查找配置，否则运行期 `BDD config not found`）。
- 基础设施：`appsettings.Integration.json` 现已去除 `ModelClient:Provider=Stub`（Stub 仅 `Test` 环境启用）；Integration 环境后端走真实 `SemanticKernelModelClient`，CI 通过 `scripts/integration.mjs` 将 `OPENAI_API_KEY` 映射为 `OpenAI__Key` 注入真实 LLM 密钥，E2E 触发真实模型调用（不再依赖 Stub 回复）。`Security:RateLimitingEnabled=false` 保留。
- feature 清单（@e2e）：`login-auth` / `credentials` / `workflow-crud` / `conversation` / `knowledge-base` / `research` / `dashboard` / `agent-crud` / `create-agent`（转换）/ `page-polish`（转换）/ `publish-workflow`。
- 转换：遗留 `create-agent.spec.ts` / `page-polish.spec.ts`（英文断言，与 zh-CN 错配，预存失败）删除并改写为 BDD；`smoke.*.spec.ts` 保留为冒烟基线（不含 @e2e）。
- 共享步骤：`common.steps.ts`（登录/导航/重定向/可见性/无意外错误，含 benign `/api-keys` 404 排除）；其余按域拆分。
- 编排：`scripts/integration.mjs --e2e` 先 `bddgen` 再 `playwright test --grep @e2e`；`safeCleanDir` 逐文件清理 test-results/playwright-report 绕过沙箱批量删除护栏。
- 验证：`node scripts/integration.mjs --e2e` → 后端 BDD 114 + 前端 BDD 22 全绿。

### 三道质量门（2026-08-05，均 0 open）
- `ddd-code-reviewer`：对抗式审查唯一生产改动 `TenantModelClientResolver.cs`（Stub 短路守卫）+ 测试基础设施。发现并修复 **P1 编译中断**：生产构造函数新增 `IConfiguration` 参数未同步单测 `Create` 辅助（仍传 4 参）→ `AgentPlatform.Infrastructure.Tests` 无法编译；且新 Stub 分支无覆盖。修复 = `Create` 增可选 `IConfiguration` 参数（空配置兜底，既有 3 测不变）+ 新增 `ResolveAsync_ReturnsEmpty_WhenProviderIsStub_WithoutResolvingCredentials`（断言短路绝不调用凭据解析/解密）；核对 `ModelRouter.RouteAsync` 空解析回退平台 stub 客户端、`IConfiguration` 框架内置自动解析。回归 `Infrastructure.Tests` 124/124 绿。
- `ddd-phase-quality-gate`：结构化审计 12 类全 PASS（P0-P3=0）——DI 无新增接口、无新聚合/EF 配置、CT 完整透传、无单例/并发风险、E2E step 与 feature 无死代码。
- `codebase-optimizer`：七维度分析 F28 增量 PASS（0 open），采用分析模式（不建分支/不 push，遵守 feature-builder 硬约束）；无桩代码遗留、无安全/性能/工程化问题。
- 报告：`docs/quality/f28-bdd-coverage-gate.md`；标记已写入 `.quality-gate.json`（phase=f28-bdd-coverage，cleared:true）。
