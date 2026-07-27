# F19 · Agent Roles 内建标记 + 页面补全 + 分类合并（统一角色目录，DB 为准）

> 状态：设计就绪，待实现（实现时开新分支 `feat/f19-agent-roles-unified`，走质量门）
> 优先级：**[P1]** open 🟡中风险（触及角色分类值对象 + 聚合加列 + EF 迁移 + 新增 PUT 端点 + 前端页重写）

## §1 目标
1. **修 bug**：系统架构 / 产品经理 / 需求分析等平台默认角色被错标为"自定义"——根因是前端 `BUILT_IN_ROLES` 硬编码的 code（architect/developer/...）与数据库 `AgentRoleDefinition` 实际 code（architecture/development/...）**整套对不上**，且聚合本身无"内建"标记。
2. **补全页面**：当前 `AgentRolesPage` 是纯展示页，后端 `POST`/`DELETE` 已就绪但前端没接，且**后端根本没有 `PUT`（编辑服务端不支持）**；还要补"被多少 Agent 引用"的反馈。
3. **合并两套分裂的分类**：`AgentType`（硬编码值对象，永不落库）与 `AgentRoleDefinition`（DB 表）code 体系互不相通。本 feature 把它们合并为**一套以数据库为准的统一角色目录**。

## §2 现状核验（逐文件行号，均基于真实代码）
- `src/AgentPlatform.Domain/ValueObjects/AgentType.cs:81` — `Predefined` = `architect / developer / tester / pm / tech-writer / reviewer`，纯硬编码，**永不落库**；`FromCode`/`FromCodeOrThrow`(`:92`/`:100`) 按 code 匹配，不在表内则抛异常。
- `src/AgentPlatform.Domain/Aggregates/AgentRoleDefinitions/AgentRoleDefinition.cs` — 聚合**无 `IsBuiltIn` 字段**；字段 `Name / RoleCode / Description / SystemPrompt / CreatedAt`。XML 注释自称"extends built-in AgentType set"，但 code 实际分裂（DB 种子用 architecture/...）。
- `src/AgentPlatform.Infrastructure/Persistence/DatabaseInitializer.cs:141-182` — 种子 6 个平台角色，code = `requirement / product / architecture / development / testing / documentation`，**未设任何内建标记**（与自定义角色同表同对待）。
- `src/AgentPlatform.Domain/Aggregates/Agents/Agent.cs:35` — `Agent.Role` 是 `AgentType` 值对象（EF `OwnsOne "Role"`）。
- `src/AgentPlatform.Application/Agents/Commands/CreateAgent/CreateAgentCommandHandler.cs:29-30` 与 `UpdateAgent/UpdateAgentCommandHandler.cs:34-35` — `AgentType.FromCode(RoleCode) ?? new AgentType(RoleCode, RoleCode, RoleCode)`：**不校验 RoleCode 是否真在目录里**，不在 Predefined 就现造一个 `AgentType`。
- `src/AgentPlatform.Web/src/pages/AgentsPage.tsx:46` — 新建 Agent 默认 `roleCode:'developer'`（AgentType code），但角色下拉来自 `getAgentRoles()`（`AgentRoleDefinition` code = `development`）→ **幽灵默认值 + 两套 code 不匹配**。
- `src/AgentPlatform.Web/src/pages/AgentRolesPage.tsx:9` — `BUILT_IN_ROLES = ['architect','developer','tester','pm','tech-writer','reviewer']` 硬编码 → 与 DB code（architecture/...）**全不匹配** → "Built-in"区块恒空，6 个平台角色全被标成 Custom 绿标。
- `src/AgentPlatform.Api/Controllers/AgentRolesController.cs` — 有 `POST`(Admin) / `DELETE`(Admin) / `GET` / `GET {roleCode}`，**无 `PUT`**（编辑服务端不支持）。
- `src/AgentPlatform.Application/Routing/Services/RoleBasedSelectionStrategy.cs:61-76` — **根本不读 `AgentType`**，只按 `WorkflowStep.StepName` 子串（"developer"/"architect"/"tester"）路由 → **合并角色 code 不影响路由逻辑**（回归安全）。
- `src/AgentPlatform.Domain/Aggregates/AgentConfigurations/AgentConfiguration.cs:45` — `AgentTypeCode` 是独立自由字符串（用于 by-type 查询），与统一目录的关系见 §3.6。

## §3 架构设计

### 3.1 数据模型（EF 迁移，铁律）
- `AgentRoleDefinition` 增 `IsBuiltIn`（bool, default false, required）。
- 新迁移 `AddAgentRoleIsBuiltIn`（生成后加 `#pragma warning disable IDE0161`）。
- `DatabaseInitializer` 种子 **7 个内建角色**（`IsBuiltIn = true`）：
  - `architecture`(系统架构) / `development`(代码实现) / `testing`(质量保证) / `product`(产品经理) / `documentation`(技术文档) / `reviewer`(评审专家) / `requirement`(需求分析)
  - 说明：`reviewer` 是 `AgentType` 第 6 个角色在 DB 无对应项 → 补为内建；`requirement` 沿用 DB 既有内建种子。
- **数据连续性**：既有 Agent 的 `Agent.Role.RoleCode`（如 `development`）与 DB 内建 code 一致 → 无需迁移 Agent 行，角色不丢。

### 3.2 统一角色目录（核心：以数据库为准）
- `AgentRoleDefinition` 表 = **唯一权威角色目录**（内建种子 + 租户自定义）。
- `AgentType` 值对象**降级为内建目录的类型化镜像**：`Predefined`(`:81`) 改为与 DB 内建 code **完全一致**（architecture/development/testing/product/documentation/reviewer/requirement）；`FromCode` 仍可用。
- 新增**架构测试（parity test，放 ArchitectureTests）**：断言 `AgentType.Predefined.Select(x => x.RoleCode)` 的集合 == `DatabaseInitializer` 内建种子 code 集合 → 强制"DB 为准"，任一方改动都令测试失败，杜绝再次漂移。
- `Agent.Role` 仍绑定 `AgentType`（**不改动 Agent 聚合、不新增 Agent 迁移**，低风险）；因两者 code 现已一致，下拉（DB code）→ `AgentType.FromCode(code)` 无缝命中，不再现造游离 `AgentType`。

### 3.3 内建判定（修 bug）
- `AgentRoleSummary` 增 `IsBuiltIn` 字段；`ListAgentRolesQuery` 回传该标记。
- 前端 `AgentRolesPage` **删除硬编码 `BUILT_IN_ROLES`**，按 `IsBuiltIn` 分区（内建只读区 vs 自定义可管理区）。

### 3.4 页面补全
- 后端新增 `PUT /api/v1/agent-roles/{roleCode}` + `UpdateAgentRoleDefinitionCommand`/`Handler`（当前缺编辑能力）：
  - **内建角色**：可编辑 `Name / Description / SystemPrompt`；**`RoleCode` 锁定、不可删**。
  - **自定义角色**：可编辑全字段 + 可删（见 3.5 引用拦截）。
- 前端：
  - 加"新建角色"按钮 → 模态（name / code / description / systemPrompt）→ `POST`(Admin)。
  - 每行加"编辑" → 模态（内建 code 只读）→ `PUT`。
  - 每行加"删除" → `Popconfirm` → `DELETE`(Admin)，拦截内建 / 被引用。
  - 列表增加"被引用 Agent 数"列（来自 3.5）。

### 3.5 引用计数
- `IAgentRoleDefinitionRepository` 增 `CountAgentsByRoleCodeAsync(tenantId, roleCode)`（或批量 `GetAgentCountsAsync`）。
- `AgentRoleSummary` 增 `AgentCount`。
- 删除拦截：若 `AgentCount > 0` → `409 Conflict`（提示先解绑相关 Agent 才能删）。

### 3.6 `AgentConfiguration.AgentTypeCode` 对齐（决策 D4）
- v1：`AgentConfiguration.AgentTypeCode` 保持**可选自由字符串**，但文档建议其值取自统一目录 code；`by-type` 查询不变。
- 后续（不在 F19）：F17 实例化若要做"按角色筛选配置"，应改用 `AgentRoleDefinition.Code`。

## §4 验收
- 内建角色（系统架构等）在页面"内建"区正确显示，**不再误标 Custom**。
- 新建 / 编辑 / 删除角色可用（Admin）；非 Admin 不显示按钮（RBAC 与后端 `[Authorize(Roles="Admin")]` 对齐）。
- 内建角色删除被拦截；被 Agent 引用的自定义角色删除被拦截并提示。
- 列表显示每个角色的"被引用 Agent 数"。
- `AgentType.Predefined` 与 DB 内建种子 code 一致（parity 测试通过）。
- 现有 Agent（`RoleCode=development` 等）仍正常，未丢角色、对话/工作流不受影响。
- 路由（`RoleBasedSelectionStrategy`）行为不变（现有策略测试回归通过）。

## §5 决策（D1–D4）
- **D1（合并策略，已定）**：DB 为准；`AgentType` 降为镜像 + parity 测试；**不改动 `Agent` 聚合、不新增 Agent 迁移**（低风险）。
  - *备选 D1-alt（不在本 feature）*：彻底移除 `AgentType`，`Agent.Role` → `RoleCode` 字符串（由 `IAgentRoleDefinitionRepository` 校验存在），需 Agent 聚合改动 + EF 迁移 + 更多测试改，风险更高，留作后续。
- **D2 内建集合**：7 个（含新增 `reviewer`、沿用 `requirement`）。
- **D3 编辑端点**：后端补 `PUT`；内建 `RoleCode` 锁、不可删。
- **D4 `AgentConfiguration` 对齐**：v1 不强制，仅文档建议。

## §6 风险
- 修改 `AgentType.Predefined` 的 code 会让引用旧 code 的测试失败 → 需同步改 SpecFlow `AgentTypeMigration`（`"architect"`→`"architecture"` 等）与 `GetAgentQueryHandlerTests`（`new AgentType("developer",...)` 构造仍 OK，但断言若依赖旧 code 需更新）。
- 新增 EF 迁移（IsBuiltIn 列 + 种子）→ 必须走 EF 铁律（`dotnet ef migrations add` + 迁移文件 `#pragma warning disable IDE0161`；涉及 `AppDbContextModelSnapshot` 自动更新）。
- parity 测试需让初始化器内建 code 集合与值对象可比较（从 DI 取 `DatabaseInitializer` 逻辑抽取常量，或两边都引用同一份常量源）。
- 与 F16（列表改卡片）强耦合：`AgentRolesPage` 在 F16 目标页清单内；建议 F16 与 F19 中**先落其一统一收口该页**（D 时序见 backlog 协同说明）。
