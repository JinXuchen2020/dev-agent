# F7 · 工作流平台化（program）

> **状态**：`doing`（子项① 进行中）。F7 是 program，可拆 8 个子史诗（见 §0）。本文件是 program 的总设计枢纽；每个子史诗在对应 feature-builder 轮次中细化并独立 check-in。本文件当前细化**子项①：工作流版本管理 + 导入导出**，作为 feature-builder 本轮（分支 `feat/f7-workflow-versioning`）的取数单元。

## 0. F7 子史诗总览（roadmap，来源 competitive-roadmap.md §4）

1. **① 版本管理 + 导入导出**（本轮回填）✅ 进行中
2. ② 节点全家桶：Code/HTTP/Tool/Knowledge Retrieval/Condition/Loop/Variable/Sub-workflow/Delay/User-Input（注：Tool/Code/Knowledge 节点 executor 已在 F5 落地，本子项补齐其余）
3. ③ 触发器：Webhook / 定时(cron) / Chat
4. ④ 发布为 API / MCP Server（复用现有 API Key）
5. ⑤ 模板市场 / 示例库
6. ⑥ 执行 Trace / 评估视图（节点级耗时/token/IO）
7. ⑦ 工作流调试器（变量监视 + 单步重跑 + 错误分支）
8. ⑧ 企业增强：多工作空间隔离与切换 / 用量仪表盘 / 工作流 diff

## 1. 本轮目标（子项①）

把"无版本、无导入导出"的工作流变为可管理、可迁移的资产：
- **版本管理**：对任意工作流可「存为版本」（快照当前定义）、查看版本历史、回滚到任意历史版本、删除版本。
- **导入导出**：工作流定义可导出为 JSON 文件（下载），并可从 JSON 文件导入为**新的**工作流（不覆盖原工作流）。导出/导入的 JSON 形状与画布 `toPayload()` 对齐，支持直接回灌。
- **多租户隔离**：版本与导入均按租户隔离（`WorkflowVersion` 实现 `ITenantScoped`，复用 `AppDbContext` 全局 query filter）。
- **审计**：版本创建 / 回滚 / 导入 / 导出 / 删除均落审计（新增 `AuditActionType` 成员）。

纯增量、低风险（新增聚合 + 迁移 + 端点 + 前端 UI），不改动既有 `Workflow` 聚合与执行引擎。

## 2. 后端接口契约（camelCase）

> 统一挂在现有 `WorkflowsController`（`Route("api/v1/workflows")`）下，复用其 `[Authorize]` 基座。版本写操作与导入 = `[Authorize(Roles="Admin,Operator")]`（与 `UpdateWorkflow`/`RunWorkflow` 一致）；读/导出 = `[Authorize]`（任意已认证，与 `GetWorkflow` 一致）。

| Verb + Route | 方法 | RBAC | 作用 |
|---|---|---|---|
| `POST /api/v1/workflows/{id}/versions` | `CreateWorkflowVersion` | Admin,Operator | 对当前工作流定义存为快照（版本号自动 = 最新+1），返回快照详情 |
| `GET  /api/v1/workflows/{id}/versions?skip=&take=` | `ListWorkflowVersions` | Authorize | 版本历史（按版本号降序分页），返回 `{items,totalCount}` |
| `GET  /api/v1/workflows/{id}/versions/{versionId}` | `GetWorkflowVersion` | Authorize | 单版本详情（含快照的 nodes/edges） |
| `POST /api/v1/workflows/{id}/versions/{versionId}/restore` | `RestoreWorkflowVersion` | Admin,Operator | 用该版本快照整体回滚工作流（图 + 名称 + context），返回更新后的 `WorkflowDetail` |
| `DELETE /api/v1/workflows/{id}/versions/{versionId}` | `DeleteWorkflowVersion` | Admin,Operator | 删除指定版本 |
| `GET  /api/v1/workflows/{id}/export` | `ExportWorkflow` | Authorize | 导出当前工作流定义为 JSON（`WorkflowExport`） |
| `POST /api/v1/workflows/import` | `ImportWorkflow` | Admin,Operator | 从 `WorkflowImportRequest` 创建**新**工作流，返回 `WorkflowDetail` |

### 2.1 请求 / 响应模型（Application 层 DTO）

复用既有 `WorkflowNodeRequest` / `WorkflowEdgeRequest`（定义于 `Application/Workflows/Commands/UpdateWorkflow/UpdateWorkflowCommand.cs`）：
```csharp
// 复用（导入/导出节点形状）
public sealed record WorkflowNodeRequest(Guid Id, StepType Type, string Name, WorkflowNodePosition Position, string? Config = null, Guid? AssignedAgentId = null);
public sealed record WorkflowNodePosition(double X, double Y);
public sealed record WorkflowEdgeRequest(Guid Id, Guid Source, Guid Target, string? Label = null);
```

新增（置于 `Application/Workflows/Versioning/`）：
```csharp
// 快照视图（版本详情节点/边，无运行时 state/result）
public sealed record WorkflowVersionNodeView(Guid Id, StepType Type, string Name, double X, double Y, string? ConfigJson, Guid? AssignedAgentId);
public sealed record WorkflowVersionEdgeView(Guid Id, Guid Source, Guid Target, string? Label);

public sealed record WorkflowVersionSummary(Guid Id, int VersionNumber, string Name, string? Note, DateTime CreatedAt, Guid? CreatedBy);
public sealed record WorkflowVersionDetail(
    Guid Id, int VersionNumber, string Name, string? Note, DateTime CreatedAt, Guid? CreatedBy,
    string Context, IReadOnlyList<WorkflowVersionNodeView> Nodes, IReadOnlyList<WorkflowVersionEdgeView> Edges);
public sealed record WorkflowVersionList(IReadOnlyList<WorkflowVersionSummary> Items, int TotalCount);

// 导出 = 与导入请求同构，可直接回灌
public sealed record WorkflowExport(
    Guid Id, string Name, string Context,
    IReadOnlyList<WorkflowNodeRequest> Nodes, IReadOnlyList<WorkflowEdgeRequest> Edges, DateTime ExportedAt);

// 导入请求（新建工作流）
public sealed record ImportWorkflowRequest(
    string Name, string InitialContext, IReadOnlyList<WorkflowNodeRequest>? Nodes = null, IReadOnlyList<WorkflowEdgeRequest>? Edges = null);
```

CQRS 命令/查询（均位于 `Application/Workflows/Versioning/`）：
- `CreateWorkflowVersionCommand(Guid WorkflowId, Guid TenantId, string? Note)` : `ICommand<WorkflowVersionDetail?>`
- `ListWorkflowVersionsQuery(Guid WorkflowId, int Skip, int Take)` : `IRequest<WorkflowVersionList>`
- `GetWorkflowVersionQuery(Guid WorkflowId, Guid VersionId)` : `IRequest<WorkflowVersionDetail?>`
- `RestoreWorkflowVersionCommand(Guid WorkflowId, Guid VersionId, Guid TenantId)` : `ICommand<WorkflowDetailResponse?>`
- `DeleteWorkflowVersionCommand(Guid WorkflowId, Guid VersionId, Guid TenantId)` : `ICommand`
- `ExportWorkflowQuery(Guid WorkflowId)` : `IRequest<WorkflowExport?>`
- `ImportWorkflowCommand(string Name, string InitialContext, IReadOnlyList<WorkflowNodeRequest>? Nodes, IReadOnlyList<WorkflowEdgeRequest>? Edges, Guid TenantId)` : `ICommand<WorkflowDetailResponse>`

## 3. 数据模型

### 3.1 新聚合 `WorkflowVersion`（Domain）
`AgentPlatform.Domain/Aggregates/Workflows/WorkflowVersion.cs`：
- 实现 `ITenantScoped, IAggregateRoot`（自动获得租户 query filter）。
- 字段：`Id`(Guid, 代码显式 `Guid.NewGuid()` → EF `ValueGeneratedNever()`)、`WorkflowId`(Guid)、`TenantId`(Guid)、`VersionNumber`(int)、`Name`(string, 快照时工作流名)、`SnapshotJson`(string, nvarchar max，存序列化图)、`Note`(string?)、`CreatedAt`(DateTime)、`CreatedBy`(Guid?)。
- 工厂：`WorkflowVersion.Create(...)`。`SnapshotJson` 由 `WorkflowGraphSnapshot` 序列化得到。

### 3.2 快照序列化辅助 `WorkflowGraphSnapshot`（Application）
`AgentPlatform.Application/Workflows/Versioning/WorkflowGraphSnapshot.cs`：
- `FromWorkflow(Workflow)` → 抓 `Context` + `Nodes`(Id/Type/Name/PositionX/Y/ConfigJson/AssignedAgentId) + `Edges`(Id/Source/Target/Label)，序列化为 JSON 存 `SnapshotJson`。
- `ToReplaceGraphArgs()` → 反序列化后产出 `ReplaceGraph` 所需 `(TempId,Type,Name,X,Y,Config,AgentId)` / `(TempId,SourceTempId,TargetTempId,Label)` 元组。关键点：用快照中节点原始 `Id` 作为 `TempId`，边引用这些 `Id` 作为 `SourceTempId/TargetTempId`；`Workflow.ReplaceGraph` 会映射 `TempId→新 Guid`，从而**完整重建图结构**（类型/名称/坐标/配置/代理/拓扑），节点新 Guid 由引擎生成，不影响结构正确性。

### 3.3 仓储 / EF
- `IWorkflowVersionRepository`（Domain/Repositories）：`GetByIdAsync` / `ListByWorkflowAsync(workflowId,skip,take)` / `CountByWorkflowAsync` / `GetLatestVersionNumberAsync` / `Add` / `Remove`。查询经 `AppDbContext` 自动租户过滤；handler 侧再校验 `TenantId` 防 id 猜测越权。
- `WorkflowVersionRepository`（Infrastructure/Persistence/Repositories）：基于 `AppDbContext.WorkflowVersions`。
- `WorkflowVersionConfiguration`（Infrastructure/Persistence/Configurations）：`ToTable("WorkflowVersions")`；`Id` `ValueGeneratedNever()`；`SnapshotJson` 不设长度（nvarchar max）；`HasIndex(WorkflowId, VersionNumber)`。
- `AppDbContext`：新增 `DbSet<WorkflowVersion> WorkflowVersions`（`ApplyConfigurationsFromAssembly` 自动注册配置）。
- **EF 迁移铁律**：`dotnet ef migrations add AddWorkflowVersions`（生成文件加 `#pragma warning disable IDE0161`）；`dotnet-ef` 在 `~/.dotnet/tools`，使用前 `export PATH="$HOME/.dotnet/tools:$PATH"`。

### 3.4 审计
`Domain/Aggregates/AuditLogs/AuditLog.cs` 的 `AuditActionType` 枚举新增：`CreateWorkflowVersion, RestoreWorkflowVersion, ImportWorkflow, ExportWorkflow, DeleteWorkflowVersion`。handler 内 `IAuditLogRepository.Add(AuditLog.Record(...))`；命令实现 `ICommand` → `UnitOfWorkBehavior` 自动提交。

## 4. 验收标准（sub-epic ①）

- **版本创建**：`POST /versions` 成功，版本号 = 现有最大+1；`SnapshotJson` 可反序列化为与当时图一致的结构；审计落 `CreateWorkflowVersion`。
- **版本列表**：按版本号降序分页，含 `totalCount`；跨租户不可见（query filter）。
- **版本详情**：返回快照 nodes/edges 与创建时一致。
- **回滚**：`POST /versions/{id}/restore` 后工作流图/名称/context 与快照一致，返回 `WorkflowDetail`；运行/暂停态禁止回滚（抛 `WorkflowConflictException` → 409）；审计落 `RestoreWorkflowVersion`。
- **删除版本**：`DELETE /versions/{id}` 后该版本不可见于列表；审计落 `DeleteWorkflowVersion`。
- **导出**：`GET /export` 返回与画布 `toPayload()` 同构的 `WorkflowExport`（nodes=WorkflowNodeRequest、edges=WorkflowEdgeRequest），可直接回灌 `import`。
- **导入**：`POST /import` 用导出 JSON 创建新工作流，图经 `ValidateGraph` 校验（环/缺 Start/缺 End/不可达 → 400）；返回新 `WorkflowDetail`；审计落 `ImportWorkflow`。
- **租户隔离**：A 租户版本不可被 B 租户列出/读取/回滚/删除（handler 校验 `TenantId` + query filter 双保险）。
- **前端**：WorkflowsPage 卡片可「版本历史」（Drawer：列表/存为版本/恢复/删除）、「导出」（下载 JSON）；Canvas 工具栏「导入 JSON」（文件 → 新建工作流并跳转）；i18n 中英齐备（对称测试通过）。
- **QA**：`dotnet build` 0/0、`dotnet test src/AgentPlatform.sln` 全绿（含新增版本/导入导出单测 + 租户隔离 + 回滚 + 序列化往返）、前端 `tsc --noEmit` 0 error + `vite build` 通过 + `qa.mjs` 全绿。

## 5. 风险点

- **R1 序列化往返稳定性**：`WorkflowGraphSnapshot` 用 `System.Text.Json` 序列化 record；`StepType` 以数字存储，反序列化需能还原枚举。缓解：record 位置构造器命名与属性一致，JsonSerializer 按名匹配；单测覆盖「Workflow → Snapshot → 反序列化 → ReplaceGraph → 结构等价」。
- **R2 回滚冲突**：运行/暂停态禁止回滚（复用 `UpdateWorkflowCommandHandler` 守卫）。
- **R3 导入图非法**：经 `ReplaceGraph` 内 `ValidateGraph` 抛 `WorkflowGraphException`，全局异常处理器 → 400。
- **R4 大 JSON**：`SnapshotJson` 用 nvarchar max；测试覆盖典型规模（数十节点）。
- **R5 审计落库**：命令必须实现 `ICommand`（非 ICommand 的 handler 不会触发 `UnitOfWorkBehavior` 提交，审计仅 `Add` 不落库）——本设计全部写操作为 `ICommand`。

## 6. 质量门清单（嵌入，供 ddd-phase-quality-gate 消费）

- G1 DI 注册：新仓储 `IWorkflowVersionRepository` 经 `AddScoped` 注册；handler 经 `RegisterServicesFromAssembly` 自动注册。
- G2 DDD 层合规：聚合/仓储/应用分层清晰，无跨层调用；`WorkflowVersion` 为聚合根。
- G3 EF 映射：迁移 `AddWorkflowVersions` 生成 + `Id` `ValueGeneratedNever()` + `SnapshotJson` nvarchar max；Snaphot 实体无需 OwnsMany。
- G4 CancellationToken：所有异步方法透传 `ct`。
- G5 内部类密封：`WorkflowVersionRepository`/`WorkflowVersionConfiguration` 用 `internal sealed`。
- G6 并发：版本号由 `GetLatestVersionNumberAsync` 读后 +1（单租户内顺序创建，低风险；不做分布式锁）。
- G7 null 守卫：handler 对 `GetByIdAsync` 返回 null → `null`（控制器映射 404）；跨租户 → null（不泄露存在性）。
- G8 API 基础设施：401/403/404/409/400/200 正确；集成测试覆盖。
- G9 Swagger/XML：公共 DTO/控制器方法补 XML 注释。
- G10 死代码：无遗留占位/桩。
- G11 模型一致性：前后端字段/类型/枚举逐一对齐（见 §2 + 前端 `types/index.ts`）。
- G12 审计：5 个新 action 均 emit 且经 UoW 落库。

## 7. 决策（D1–D5，已锁定）

- **D1 版本=手动快照**：不自动在每次 `UpdateWorkflow` 存版本（避免噪声）；用户通过「存为版本」显式创建。回滚=显式动作。
- **D2 导入=新建**：导入 JSON 创建**新**工作流（不覆盖原），返回新 id；导出/导入 JSON 与画布 `toPayload()` 同构。
- **D3 导出形状=与导入请求同构**：`WorkflowExport.Nodes` 用 `WorkflowNodeRequest`（含 `Position{X,Y}`），前端导出文件可直接作为导入输入，无需转换。
- **D4 版本号=整型递增**：每工作流内 `max+1`；不引入语义化版本（对齐 `AgentConfiguration.version` 的整数语义，但不强制 Major/Minor）。
- **D5 RBAC 沿用工作流既有**：写/导入=Admin,Operator；读/导出=任意已认证。版本无独立权限维度。
