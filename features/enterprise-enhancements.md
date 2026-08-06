# F26 · 企业增强（多工作空间 / 用量仪表盘 / 工作流 diff）

> 状态：`done（v1 · 用量仪表盘 + 工作流 diff）`。多工作空间（Workspace）独立排期，未纳入 v1（见 §6 S1）。来源：F7 工作流平台化 program 子项 **⑧**。

## 0. 目标
补齐企业级治理能力：① 多工作空间（Workspace）隔离与切换（团队/部门级二级边界）；② 用量仪表盘（复用 F18 analytics，扩工作流维度）；③ 工作流 diff（比较两个版本/两个工作流的差异，复用 F7 ① 快照）。

## 1. 范围
**in**：
- **多工作空间**：在 Tenant 下引入 `Workspace` 二级维度；工作流/Agent/知识库等可归属 Workspace；用户可切换当前 Workspace；前端顶部 Workspace 切换器。
- **用量仪表盘**：在 F18 `analytics/summary` 基础上增「按工作流」维度面板（已有 TopWorkflows，扩为完整工作流用量页）。
- **工作流 diff**：`POST /api/v1/workflows/{id}/diff` body `{ fromVersion, toVersion | otherWorkflowId }` → 返回节点/边/配置差异（文本/结构化）。
- 多租户隔离 + 审计（CreateWorkspace/SwitchWorkspace/DiffWorkflow）。

**out（v1）**：跨租户工作空间（workspace 限租户内）、细粒度 workspace 级 RBAC（v1 仅归属隔离，不引入 workspace 角色）。

## 2. 接口契约草案（后端）
- `POST /api/v1/workspaces` / `GET /api/v1/workspaces` / `PUT /api/v1/workspaces/{id}`（Admin,Operator）。
- `Workspace` 切换：在 `TenantProvider` 外扩 `IWorkspaceProvider`（per-request 解析当前 WorkspaceId），各聚合加 `WorkspaceId` 列 + query filter（**破坏性，影响全部实体**）。
- `GET /api/v1/analytics/workflows?from=&to=` → 工作流量化（复用 F18）。
- `POST /api/v1/workflows/{id}/diff` body `{ fromVersion?, toVersion?, otherWorkflowId? }` → `WorkflowDiff(AddedNodes, RemovedNodes, ChangedNodes, EdgeChanges, ConfigChanges)`。
- 审计：`AuditActionType` 增 `CreateWorkspace/SwitchWorkspace/DiffWorkflow`。

## 3. 数据模型与改动面
- **新增聚合** `Workspace`（ITenantScoped，二级）：`{ Id, TenantId, Name, CreatedAt }` + EF 迁移。
- **破坏性**：主流实体（`Workflow`/`Agent`/`Conversation`/`KnowledgeBase` 等）加 `WorkspaceId`（Guid?）+ 全局 query filter；`ITenantScoped` → `ITenantWorkspaceScoped`（或聚合接口扩展）。影响面 = 全仓所有租户查询与 handler。
- `WorkflowGraphSnapshot` 复用做 diff（两版本 SnapshotJson 反序列化比对）。
- 前端：Workspace 切换器（`AppLayout`）+ 用量页 + diff 视图（复用 F7 ① 版本抽屉）。

## 4. 风险
- 🔴 极高风险：Workspace = 第二租户维度，需改全部聚合 + query filter + `TenantProvider` 体系 + 所有 handler 校验 + 迁移；是平台级breaking change。
- 缓解：先只做「用量仪表盘 + 工作流 diff」（低风险、纯增量，复用 F18/F7①），多工作空间作为**独立排期的高风险子项**，绝不与其他两项捆绑实现；diff 可独立先交付。

## 5. 验收标准草案
- **低风险两项（优先交付）**：用量仪表盘按工作流维度正确；工作流 diff 准确展示两版本节点/边/配置差异。
- **多工作空间（高风险，独立排期）**：创建/切换 workspace；实体按 workspace 隔离；切换后查询仅见当前 workspace 数据。
- 多租户：A 租户 workspace 不可见 B 租户。
- 审计落库；前端 tsc 0 + qa.mjs 全绿。

## 6. 决策（2026-08-06 锁定）
- **S1 Workspace 是否实现**：**v1 仅做用量仪表盘 + 工作流 diff（低风险纯增量）**；多工作空间 = 第二租户维度、全仓破坏性，明确**独立排期**，不纳入本 feature、不触碰 `ITenantScoped`/`TenantProvider` 体系。
- **S2 Workspace 数据模型**：N/A（v1 不含 Workspace）。
- **S3 diff 形态**：**结构化 JSON 差异**（v1，后端返回 AddedNodes/RemovedNodes/ChangedNodes/EdgeChanges/ContextChanged），复用 `WorkflowGraphSnapshot`（`WorkflowVersion.SnapshotJson` 反序列化比对）；可视化图 diff 留作后续前端增强。
- **S4 用量仪表盘**：**独立 endpoint** `GET /api/v1/analytics/workflows?from=&to=`（不复用 `analytics/summary` 扩字段，避免污染汇总 DTO），按工作流聚合执行数/成功率/平均延迟/Token，复用 F18 `IExecutionLogRepository.GetByTenantAsync` 数据层。
- **S5 审计边界**：v1 两项均为**只读查询**（diff 复用 `WorkflowGraphSnapshot.FromWorkflow` 当前图 vs 版本快照 / 其他工作流当前图；usage 为统计查询），与 F18 `GetDashboardSummaryQuery` 一致**不写审计**（避免查询层引入写副作用）；`AuditActionType` 不新增；多工作空间的 CreateWorkspace/SwitchWorkspace 审计随 Workspace 独立 feature 落地。

## 7. 实现结构清单（v1 · feature-builder 全栈实跑）
- 后端：
  - `Workflows/Versioning/DiffWorkflow/DiffWorkflowQuery.cs`：`POST /api/v1/workflows/{id}/diff`，body `{fromVersionId?,toVersionId?,otherWorkflowId?}`，返回 `WorkflowDiffDto`（复用 `WorkflowVersionNode`/`WorkflowVersionEdge` 记录）。读操作继承类级 `[Authorize]`。
  - `Analytics/Queries/GetWorkflowUsage/GetWorkflowUsageQuery.cs`：`GET /api/v1/analytics/workflows?from=&to=`，返回 `WorkflowUsageList`（按 `ExecutionLog.WorkflowId` 聚合）。
  - `AnalyticsController` 增 `GetWorkflowUsage` action；`WorkflowsController` 增 `DiffWorkflow` action。
- 前端：
  - `WorkflowUsagePage.tsx`（路由 `/usage`，AppLayout 菜单「用量」）：按工作流用量表格 + 执行数柱状图（复用 F18 antd plots / i18n）。
  - `WorkflowsPage` 版本抽屉增「对比」动作 → 选两版本（默认最新两版）→ 调用 `diffWorkflow` → 结构化 diff 弹窗。
  - `types/index.ts` + `services/api.ts` + `locales` zh-CN/en-US 同步（i18n 对称）。
- 质量：三道质量门 + `docs/quality/f26-enterprise-enhancements-gate.md` + `.quality-gate.json`（cleared:true）。

---

## Phase Quality Gate Checklist（v1 · F26）

> 由 ddd-code-reviewer + ddd-phase-quality-gate 两道门在提交前实跑；codebase-optimizer 维度（架构/质量/正确性/测试/性能/安全/工程化）已随本次定向评审覆盖。

### 门类与结论（8 类全过）

| # | 门类 | 结论 | 证据 |
|---|------|------|------|
| 1 | Pre-flight 版本审计 | PASS | 未引入新 NuGet 包；新增 Query/Handler 签名与 Controller 对齐；`dotnet build` 0 警告 0 错误 |
| 2 | BDD 场景优先 | PASS | `e2e/features/workflow-usage.feature`（2 场景）+ `e2e/steps/usage.steps.ts`；`bddgen` 绑定通过 |
| 3 | DDD 分层规则 | PASS | Query/Handler/DTO 在 Application；Controller 在 Api；Repository 接口在 Domain、实现在 Infrastructure；Application 不引用 Infrastructure |
| 4 | DI 注册完整性 | PASS | `Application/DependencyInjection.cs:24` `AddMediatR(RegisterServicesFromAssembly(Application))` 自动注册 `DiffWorkflowQueryHandler` 与 `GetWorkflowUsageQueryHandler` |
| 5 | 配置先行 | PASS | 无新增配置项；用量 14 天默认值合理；区间上限由 `AnalyticsController` 的 `MaxRangeDays` 强制 |
| 6 | EF Core 映射同步 | N/A | v1 未新增聚合/VO；复用 `Workflow`/`ExecutionLog`/`WorkflowVersion` 既有 `IEntityTypeConfiguration` |
| 7 | 并发与生命周期 | PASS | 两项均为只读 Query，无共享可变状态；MediatR 默认 Scoped 生命周期 |
| 8 | 横切基础设施 | PASS | 沿用全局 ExceptionHandler + ProblemDetails；diff/usage 读端点继承类级 `[Authorize]`；CORS 按既定策略延后 |

### 评审发现（ddd-code-reviewer，均已修复）

| 严重度 | 类别 | 文件:行 | 发现 | 修复 |
|--------|------|---------|------|------|
| P1 | 逻辑/假阳性 | `DiffWorkflowQuery.cs` | 边「变更」检测按 `Id` 比较，而快照 Id 每次保存重新生成 → 每个未变更边均被误报为「已变更」 | 移除 `changedEdges` 概念（边除端点名+标签外无可变属性）；删除 `EdgeEquals` |
| P1 | 逻辑/假阳性 | `DiffWorkflowQuery.cs` `NodeEquals` | `x.X == y.Y`（操作数错位）→ 任意 `X≠Y` 的节点被误报为「已变更」（测试因坐标为 0 而恰好通过） | 修正为 `x.X == y.X` |
| P1 | 数据完整性 | `WorkflowGraphSnapshot.FromWorkflow` + `Workflow.GetEffectiveGraph` | `FromWorkflow` 读 `wf.Nodes`（`_nodes`），对遗留 `_steps`-only 工作流为空 → 版本快照与 diff 抓取不到内容 | 新增 `Workflow.GetEffectiveGraph()` 回退到链式视图；`FromWorkflow` 改用之 |
| P2 | 健壮性 | `DiffWorkflowQuery.cs` `Compute` | `ToDictionary(n=>n.Name)` 在重名节点（畸形遗留图）时抛 `ArgumentException` | 新增 `ToNameMap`（首名优先，容忍重名） |
| P3 | 死代码 | `GetWorkflowUsageQuery.cs` | 处理器内 `private const int MaxRangeDays` 未被引用（区间校验在 Controller） | 删除该常量 |

### Gate Status: PASS
`[P0:0 | P1:0 | P2:0 | P3:0]` — 全部发现已修复，无待豁免项。
