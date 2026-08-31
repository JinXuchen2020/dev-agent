# F35 · 多工作空间隔离（Workspace）设计文档

> 来源：backlog 延后项（F26 企业增强 · S1「Workspace v1 不做，独立排期」）。
> 风险等级：🔴 高风险（全聚合加 WorkspaceId + query filter 修改 + TenantProvider 体系扩展 + 前端全局切换）。
> 分支：`feat/f35-workspace-isolation`（2026-08-31 自 master 新建）。

## 1. 目标

同一租户内再分一层「工作空间」：创建/切换 workspace，实体按 workspace 隔离，切换后查询仅见当前 workspace 数据。本质是「第二租户维度」。

- 默认 workspace 自动存在（每个租户一个 `Default`），存量数据回填到默认 workspace，向后兼容。
- 顶栏 WorkspaceSwitcher 切换，切换后全站数据刷新。
- Admin 可新建/重命名/删除 workspace；删除有守卫（默认不可删、非空不可删）。

## 2. 代码现状（调研事实，2026-08-31）

| 事实 | 位置 |
|---|---|
| `ITenantScoped` 仅 `Guid TenantId { get; }` | `src/AgentPlatform.Domain/Abstractions/ITenantScoped.cs:7` |
| 实现 ITenantScoped 的聚合 18 个 | `Domain/Aggregates/`：Agent、AgentConfiguration、AgentMessageLog、ApiKey、Conversation、ConversationWorkflowBinding、DebugSession、EvaluationDataset、HumanApproval、KnowledgeBase、PublishedWorkflow、RunningExecution、TenantCredentialSetting、ToolDefinition、User、Workflow、WorkflowVersion、WorkflowTrigger |
| 平台级（非租户隔离，不受影响） | WorkflowTemplate、PlatformModel、AgentRoleDefinition |
| 有 TenantId 列但不实现 ITenantScoped（无 query filter，存量缺口） | AuditLog、ExecutionLog、AgentRunRecord |
| TenantId 赋值方式：聚合工厂构造函数传参，无 SaveChanges 拦截器 | 如 `Agent.cs:144` |
| `ITenantProvider`：OverrideTenantId → JWT `tenant_id` claim → `X-Tenant-Id` header → 配置默认租户 | `Application/Abstractions/ITenantProvider.cs:6`；`Infrastructure/Persistence/TenantProvider.cs:14,51,58` |
| query filter 在 `OnModelCreating` 反射统一施加，闭包引用 `_tenantId` 字段（DbContext 构造时解析一次） | `Infrastructure/Persistence/AppDbContext.cs:184-197` |
| IUnitOfWork 即 AppDbContext，提交由 `UnitOfWorkBehavior` 统一触发，无租户注入步骤 | `AppDbContext.cs:27`；`Application/Behaviors/UnitOfWorkBehavior.cs:27` |
| JWT claims：NameIdentifier/Email/sub/tenant_id/Role，写 httpOnly cookie | `Api/Endpoints/AuthEndpoints.cs:32-44`；`Api/Security/JwtTokenService.cs:24-42` |
| 前端请求基座 `/api/v1` + `withCredentials`，无 Authorization 头 | `src/services/api.ts:68-75` |
| 前端数据加载统一入口 `useApiState`（deps 变化重载） | `src/hooks/useApiState.ts:16` |
| 顶栏 LanguageSwitcher 在 `AppLayout.tsx:141`，WorkspaceSwitcher 可插其旁 | `src/layouts/AppLayout.tsx:125-148` |
| 测试基座：IntegrationAppFactory（钉死默认租户）+ IntegrationSeeder（Tenant1/Tenant2 常量） | `SpecFlowTests/IntegrationAppFactory.cs:28,54`；`IntegrationSeeder.cs:19` |
| EF：`MigrateAsync` 优先，25 个 Configuration，约 26 个迁移 | `DatabaseInitializer.cs:102-116` |

## 3. 数据模型

### 3.1 新聚合

- `Workspace`（`ITenantScoped`）：`Id`（Guid, ValueGeneratedNever）、`TenantId`、`Name`、`Description?`、`IsDefault`（bool）、`CreatedAt`。租户内 Name 唯一（唯一索引 (TenantId, Name)）。
- `WorkspaceMember`（`ITenantScoped`，按 §6 D4 决策取舍）：`Id`、`TenantId`、`WorkspaceId`、`UserId`。唯一索引 (WorkspaceId, UserId)。

### 3.2 存量聚合扩展

- 18 个 `ITenantScoped` 聚合 + §2 所列 3 个有 TenantId 无过滤器的实体（按 §6 D2 决策）新增 `WorkspaceId`（Guid，`defaultValue` = 各租户默认 workspace）。
- `IWorkspaceScoped` 接口（`Guid WorkspaceId { get; }`）挂在 Domain，聚合显式实现；平台级聚合不实现。
- query filter 追加 `w => w.WorkspaceId == _workspaceId`（与现有 tenant filter 同一反射点，`AppDbContext.cs:184-197`）。

### 3.3 EF 迁移（一次迁移）

- 新表 `workspaces`、`workspace_members`（按 §6 D4）。
- 全部目标表加 `workspace_id` 列。
- 迁移铁律：`dotnet ef migrations add AddWorkspaceIsolation`（`#pragma warning disable IDE0161`）。
- 存量行回填：迁移内 `defaultValue` 不能引用其他表 → 由 `DatabaseInitializer` 幂等兜底（EnsureDefaultWorkspace：每租户缺失则插 `IsDefault=true` 的 Default，再把 `workspace_id` 为空的行刷成默认 workspace Id）。

## 4. 前后端接口契约（camelCase）

### 4.1 后端端点（`WorkspacesController`，前缀 `/api/v1/workspaces`）

| 方法 | 路径 | 鉴权 | 语义 |
|---|---|---|---|
| GET | `/` | `[Authorize]` | 当前租户 workspace 列表 `{id, name, description?, isDefault, createdAt}` |
| POST | `/` | `[Authorize(Roles="Admin")]` | 新建 `{name, description?}` → 201 |
| PUT | `/{id}` | `[Authorize(Roles="Admin")]` | 重命名/改描述 |
| DELETE | `/{id}` | `[Authorize(Roles="Admin")]` | 删除；默认 workspace → 409；非空（含成员/业务实体引用）→ 409 |
| POST | `/{id}/switch` | `[Authorize]` | 切换：按 §6 D1 返回新 JWT（写 cookie）/ 仅校验可访问性 |

### 4.2 命令/查询（Application，CQRS）

- `ListWorkspacesQuery` / `CreateWorkspaceCommand` / `UpdateWorkspaceCommand` / `DeleteWorkspaceCommand`（删除结局枚举：Deleted / NotFound / DefaultConflict / InUseConflict） / `SwitchWorkspaceCommand`。
- 租户上下文来源：现有 `ITenantProvider`；workspace 上下文来源：新增 `IWorkspaceContext`（`Infrastructure/Persistence/WorkspaceContext.cs`），解析链与 D1 决策对应（JWT `workspace_id` claim → `X-Workspace-Id` header → 租户默认 workspace）。

### 4.3 前端

- `types/index.ts`：`Workspace`、`CreateWorkspaceRequest`、`UpdateWorkspaceRequest`。
- `services/api.ts`：`getWorkspaces / createWorkspace / updateWorkspace / deleteWorkspace / switchWorkspace`。
- `stores/appStore.ts`：`currentWorkspaceId` + `workspaces`（持久化 localStorage `app-workspace-id`）。
- `layouts/AppLayout.tsx`：顶栏 `WorkspaceSwitcher`（LanguageSwitcher 旁，L141 区域）——Select 下拉 + 新建入口（Admin）+ 切换后 `currentWorkspaceId` 变更 → 全站经 `useApiState` 依赖注入刷新（或简易 `window.location.reload()`，见 §6 D5）。
- 若走 header 方案：`api.ts` 请求拦截器统一附 `X-Workspace-Id`。

## 5. 验收标准

1. 租户 A 建 workspace W1/W2；在 W1 建工作流，切到 W2 后列表不可见；切回 W1 可见。
2. 默认 workspace 自动创建；存量数据（迁移后旧库）全部落在默认 workspace，切换前行为与现状一致。
3. 非 Admin 可切换、不可创建/删除（403）。
4. 默认 workspace 删除 → 409；含业务实体的 workspace 删除 → 409；空 workspace 删除成功。
5. 平台级聚合（WorkflowTemplate/AgentRoleDefinition/PlatformModel）不受 workspace 影响，跨 workspace 可见。
6. 跨租户访问 workspace id → 404（租户隔离既有语义延续）。
7. build 0/0 + 全量 `dotnet test` 0 失败（新增：workspace 隔离单测/集成测试、删除守卫、回填幂等）+ 前端 `tsc` 0 error + vitest 通过。
8. 三道质量门全绿；`.quality-gate.json` 推进 `f35-workspace-isolation` 含 `cleared:true` + `codebaseOptimizer`；质量报告 `docs/quality/f35-workspace-isolation-gate.md`。
9. 触及 UI → 按 feature-builder 硬约束 #7 配套 playwright-bdd E2E（CI 驱动，本地不跑）。

## 6. 决策（已锁定，2026-08-31 用户拍板）

- **D1 workspace 上下文传递 = C**：JWT `workspace_id` claim 默认 + `X-Workspace-Id` header 覆盖 + 租户默认 workspace 兜底——镜像 `ITenantProvider` 三级解析链（`IWorkspaceContext`：claim → header → 默认）。切换端点重签发 cookie 保持 claim 一致，前端拦截器同时附加 header。
- **D2 覆盖范围 = A**：18 个 `ITenantScoped` 聚合全量加 `WorkspaceId` + query filter；AuditLog/ExecutionLog/AgentRunRecord 仅加列不加 filter（沿用现状，租户 filter 缺口另立技术债）。
- **D3 成员模型 = B**：新增 `WorkspaceMember` 成员表——非 Admin 仅可见/可切自己所在的 workspace（默认 workspace 对全员可见）；Admin 可为 workspace 分配/移除成员（POST/DELETE `/{id}/members`）。
- **D4 删除守卫（固定）**：删除 workspace 时若仍有业务实体或成员 → 409；默认 workspace 恒不可删；绝不级联删除/移动数据。
- **D5 前端切换刷新 = A**：zustand `currentWorkspaceId` 驱动——`useApiState` 内部订阅 workspace 变更并自动 refetch（单点改 hook，避免逐页注入依赖）；切换同时由后端重签 cookie。
- **写路径注入约定**：TenantId 由聚合工厂显式传参（既有模式不动）；`WorkspaceId` 由 `AppDbContext.SaveChangesAsync` 对未显式赋值的新增 `IWorkspaceScoped` 实体自动注入当前 workspace（避免触碰全部 handler 工厂调用点），显式赋值优先。

## 7. 风险

- 🔴 全聚合加列 + query filter 修改：漏一个聚合 = 数据串 workspace。缓解：反射统一施加（改 `AppDbContext.cs:184-197` 一处）+ 架构测试断言「实现 IWorkspaceScoped 的聚合必有 query filter」。
- 🔴 DbContext 闭包在构造时解析一次 `_tenantId`——`_workspaceId` 同样只解析一次，per-request scope 下成立，但必须确认 AppDbContext 生命周期是 Scoped 且 header/claim 在 DbContext 构造前可用。
- 🟡 存量库回填依赖 `DatabaseInitializer` 幂等逻辑；SQLite 迁移加非空列需带 defaultValue。
- 🟡 测试基座（IntegrationAppFactory/Seeder/IntegrationConstants）需同步加 workspace。
- 🟢 平台级聚合不受影响。

## 8. 对抗式代码审查记录（2026-08-31，ddd-code-reviewer）

| # | 严重度 | 文件 | 问题 | 修复 |
|---|---|---|---|---|
| 1 | P1(安全) | WorkspaceProvider + 新增 Api/Middleware/WorkspaceHeaderGuardMiddleware.cs | X-Workspace-Id 头无可见性校验：非 Admin 伪造头可读同租户任意工作空间数据，绕过 D3=B | 新增中间件（UseAuthorization 后）：非 Admin 且头 ≠ claim 时校验默认/成员可见性，不可见则剥离头；Program.cs 注册；ArchitectureTests 控制器注入白名单补 IJwtTokenService |
| 2 | P1 | TriggerWorkflowCommandHandler + WorkflowRepository | 调度/Webhook scope 的 DbContext 过滤器在构造期冻结为「默认租户×默认工作空间」，非默认工作空间的工作流被静默跳过（master 上会运行 → F35 回归）；处理器内 Override 注入对过滤器无效（误导注释已更正） | 新增 IWorkflowRepository.GetByIdForTriggerAsync（IgnoreQueryFilters + 显式 TenantId 守卫），触发路径改用；2 处单测 stub 同步更新 |
| 3 | P2 | WorkspaceProvider | claim/header 为 Guid.Empty 时被当作合法工作空间（登录时默认空间未供应即写空 claim）→ 全站空集，未回退目录兜底 | 空 Guid 视为缺省，沿解析链回退 |
| 4 | P2 | Web/src WorkspaceSwitcher.tsx | 删除「当前」工作空间后仅清空本地状态，cookie 旧 claim 仍指向已删空间 → 全站查询为空 | 删除成功后自动 switch 到默认工作空间（失败回退 null） |
| 5 | P3 | WorkspacesController.Switch | API-Key 主体无 Name/Email claim，重签 JWT 时 Claim 构造对 null 抛异常 → 500 | 回退空串 |
| 6 | P3 | WorkspaceHeaderGuardMiddleware | 质量门审计：X-Workspace-Id 头对 Admin 完全跳过校验——浏览器 localStorage 陈旧 id（换租户重登录/空间已删）直通 → 全站空集（租户过滤器仍隔离，无泄漏，仅体验缺陷） | Admin 亦校验「id 属于本租户」（GetByIdAsync 租户过滤后存在即放行），跨租户/已删 id 一律剥离回退 claim |

验证：`dotnet build` 0 警告 0 错误；`dotnet test` 559 通过 / 1 失败（master 既有 SpecFlow LLM 用例）/ 6 跳过（Docker 门控）；前端 `tsc` 0 error，vitest 42/44（2 个 master 既有失败）。审查确认无误项：AppDbContext 闭包 field-reference per-query 求值（既有已验证模式）、Workspace/WorkspaceMember 未叠加 workspace 过滤器、SaveChanges 注入边界（仅 Added、显式赋值优先）、迁移/Down 完整、删除守卫 409 语义、RBAC 404-vs-403、18+3 聚合 WorkspaceId 一致性、回填幂等。遗留 P3 备忘：ListWorkspaceMembers N+1；名称唯一校验大小写语义（OrdinalIgnoreCase vs SQLite 区分大小写索引）；AuditLog/ExecutionLog/AgentRunRecord 运行期新行 WorkspaceId 恒为空（按 D2「仅加列」设计）。

## Quality Gate Checklist

> F35 质量门（Mode 3 = audit + checklist，ddd-phase-quality-gate）。条目对齐本 feature 实际模块：
> Domain（Workspace/WorkspaceMember 聚合 + IWorkspaceScoped）、Application（Workspaces CQRS）、
> Infrastructure（EF 配置 + 迁移 + WorkspaceContext/Provider/Directory/Provisioner）、
> Api（WorkspacesController + WorkspaceHeaderGuardMiddleware）、前端（api.ts/appStore/useApiState/WorkspaceSwitcher）。

### 1. Pre-flight Version Audit

- [x] `dotnet build` 在新增代码前基于既有代码通过
- [x] 无新增 NuGet 包（F35 仅用既有 EF Core 9 / MediatR / antd / zustand，无版本锁定项）
- [x] EF Core 9 约束已核实：无非泛型 `Set(Type)` → Provisoner/仓储显式枚举 18 聚合（已注释说明）
- [x] API 契约与既有模式对齐（claim/header/cookie 命名镜像 `ITenantProvider` 既有约定）

### 2. BDD Scenarios First

- [x] 前端 E2E：`src/AgentPlatform.Web/e2e/features/workspace-switch.feature` + `workspace.steps.ts`（playwright-bdd，CI 驱动）
- [x] 单测：`Application.Tests/Workspaces/WorkspaceHandlersTests.cs`（CQRS 全 outcome：创建/改名冲突/删除守卫 3 分支/切换可见性/成员增删）
- [x] 集成单测：`Infrastructure.Tests/Persistence/WorkspaceIsolationTests.cs`（过滤器隔离/回填幂等/SaveChanges 注入）
- [x] 边界场景覆盖：默认不可删 409、非空 409、跨租户 404、非 Admin 403/可见性、空 Guid claim 回退
- [x] 既有 SpecFlow 基座同步（IntegrationSeeder：T1/T2 默认工作空间供应 + T2 显式 Override scope）

### 3. DDD Layer Rules

- [x] `IWorkspaceContext/IWorkspaceProvider/IWorkspaceDirectory/IWorkspaceProvisioner` → `Application/Abstractions`；实现 → `Infrastructure/Persistence`
- [x] `IWorkspaceRepository/IWorkspaceMemberRepository` → `Domain/Repositories`；实现 → `Infrastructure/Persistence/Repositories`
- [x] `IWorkspaceScoped` → `Domain/Abstractions`（18 个业务聚合显式实现；平台级聚合不实现）
- [x] Domain 项目零外部 NuGet 依赖（架构测试 #1 守护）
- [x] Application 不引用 Infrastructure（架构测试 #2 守护；唯一例外 IJwtTokenService 已进 ArchitectureTests 白名单）
- [x] Api 层仅经 `AddApplication()`/`AddInfrastructure()` + `UseMiddleware<WorkspaceHeaderGuardMiddleware>()`

### 4. DI Registration Completeness

- [x] `IWorkspaceContext` → `WorkspaceContext` — Scoped
- [x] `IWorkspaceProvider` → `WorkspaceProvider` — Scoped（AppDbContext 构造期解析一次）
- [x] `IWorkspaceDirectory` → `WorkspaceDirectory` — Singleton（跨 DbContext 共享目录）
- [x] `IWorkspaceProvisioner` → `WorkspaceProvisioner` — Scoped（EF 写路径）
- [x] `IWorkspaceRepository` → `WorkspaceRepository` — Scoped；`IWorkspaceMemberRepository` → `WorkspaceMemberRepository` — Scoped
- [x] 中间件约定注册（`UseMiddleware`，非 DI 接口）；ArchitectureTests DI 断言通过
- [x] `AddWorkspaceMemberCommand` 复用既有 `IUserRepository`（无重复注册）

### 5. Configuration-First

- [x] 无新增魔法数字/超时/重试值；cookie 参数复用登录端点既有写法
- [x] 默认工作空间名 `"Default"`（WorkspaceProvisioner）为设计文档 §1 契约常量，与既有 `DefaultTenantIdSeed` 模式一致（waiver 见质量门报告）
- [x] 租户默认租户 Id 复用既有 `Tenant:DefaultTenantId` 配置链，未新增配置节（无新 Settings 类需求）

### 6. EF Core Mapping Sync

- [x] `WorkspaceConfiguration` / `WorkspaceMemberConfiguration`（ValueGeneratedNever + 唯一索引 (TenantId,Name) / (WorkspaceId,UserId)）
- [x] 迁移 `20260831052610_AddWorkspaceIsolation`：新表 ×2 + 21 个 `WorkspaceId` 列（18 聚合 + 3 补列实体）+ 索引，`#pragma warning disable IDE0161` 就位
- [x] 迁移与 `AppDbContextModelSnapshot` 一致（快照 +124 行）
- [x] `defaultValue = Guid.Empty` + `DatabaseInitializer.EnsureDefaultWorkspacesAsync` 幂等回填（迁移内 defaultValue 不能引用其他表，设计 §3.3 兑现）
- [x] Workspace/WorkspaceMember 不实现 IWorkspaceScoped（容器非隔离对象，过滤器正确排除）
- [x] SaveChanges 注入边界：仅 `EntityState.Added` + 显式赋值优先（`InjectWorkspaceIdForAddedEntities`）

### 7. Concurrency and Lifecycle

- [x] `WorkspaceDirectory`（Singleton）用 `ConcurrentDictionary`；仅增不减评估：键数=租户数（有界，万级租户 <1MB），无动态删租户功能故无清理路径需求（waiver 备案，目标=未来租户生命周期 feature）
- [x] `AppDbContext` Scoped（`AddDbContext` 默认），claim/header 在构造期可用（`UseAuthentication/UseAuthorization` → 中间件 → 控制器解析链）
- [x] 中间件剥离越权头发生在请求 scope 的 `AppDbContext` 构造之前（嵌套 scope 隔离验证）
- [x] `decimal` 累加：本 feature 无新增
- [x] 每次 `Ensure`/`Register` 有幂等保证（存在即复用），种子路径 `catch` 不阻断启动
- [x] 触发路径并发语义：`GetByIdForTriggerAsync` IgnoreQueryFilters + 显式 TenantId 守卫（跨租户不可达）；`OverrideWorkspaceId` 在 handler 内同步注入（AppDbContext 构造期冻结的过滤器不受影响，已注释）

### 8. Cross-Cutting Infrastructure

- [x] `WorkspacesController` 仅注入 `IMediator` + `IJwtTokenService`（白名单横切）；命令实现 `ICommand<T>`、查询不实现（SaveChanges 语义正确）
- [x] 错误语义：默认删 409 / 非空 409 / 跨租户与不可见 404（不泄漏存在性）/ 名称冲突 409，均 ProblemDetails
- [x] RBAC：非 Admin 仅 List/Switch；成员管理 Admin-only（D3=B）
- [x] `WorkspaceHeaderGuardMiddleware` 注册于 `UseAuthorization` 之后（Program.cs）；非 Admin + Admin 头均经租户内可见性校验
- [x] 全部新 async 方法传递 `CancellationToken`（仓储/处理器/端点 `ct`；中间件/端点用 `RequestAborted`）
- [x] 实现类全部 `internal sealed`；聚合/控制器 `public sealed`；公共参数 null guard（`ThrowIfNullOrWhiteSpace`）
- [x] DTO 出参（`WorkspaceDto`/`WorkspaceMemberDto`），请求模型 `[Required]`/`[StringLength]`/`[EmailAddress]`
- [x] 前端：`api.ts` 拦截器统一附 `X-Workspace-Id`；`appStore` 持久化 + 登出清理；`useApiState` 订阅 `currentWorkspaceId` 全站刷新（D5=A）；删除当前空间自动 switch 回默认（审查项 #4 修复）
- [x] `dotnet build` 0 警告 0 错误；受影响测试项目全绿（Application 238 / Infrastructure 158+6 跳过 / Architecture 9 / Api 35）

### Incremental Gate Sequence（F35 实际模块序）

```
Module 1: Domain（IWorkspaceScoped + Workspace/WorkspaceMember 聚合）
Module 2: Infrastructure EF（Configuration ×2 + 迁移 + AppDbContext 过滤器/SaveChanges 注入）
Module 3: Infrastructure 上下文（Context/Provider/Directory/Provisioner + DatabaseInitializer 兜底）
Module 4: Application CQRS（5 命令 + 2 查询）→ 每模块 build 0 警告 → 测试全绿 → DI/层审计 → 下一模块
Module 5: Api（Controller + 中间件 + 认证端点 claim）
Module 6: 前端（api.ts/appStore/useApiState/WorkspaceSwitcher + E2E feature）
```

### Final Regression

- [x] 全量 `dotnet build` 0/0；`dotnet test`（Application/Infrastructure/Architecture/Api.Tests）0 失败
- [x] master 既有失败清单豁免：SpecFlow LLM 用例 1、IntegrationTests 需 `OPENAI__Key` 环境变量、前端 vitest 2（均不计入本 gate）
- [x] 无新增未豁免 P0/P1/P2 审计发现；质量门报告见本节上方 §8 + 会话报告
