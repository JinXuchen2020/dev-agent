# F26 · 企业增强（多工作空间 / 用量仪表盘 / 工作流 diff）

> 状态：`open`。来源：F7 工作流平台化 program 子项 **⑧**。本文档为 feature-builder 取数单元骨架；实现前须先锁定 §6 决策（尤其「多工作空间」是否 = 第二租户维度，影响面极大）。

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

## 6. 决策（待锁定）
- **S1** Workspace 是否实现：v1 仅做用量仪表盘+diff（推荐，低风险）vs 含 Workspace（高风险，需独立 feature 排期）。
- **S2** Workspace 数据模型：二级维度加列 + query filter（破坏性）vs 独立 schema/库（隔离更强但成本更高）。
- **S3** diff 形态：结构化 JSON 差异（v1）vs 可视化图 diff（前端增强，后续）。
- **S4** 用量仪表盘：复用 F18 `analytics/summary` 扩字段 vs 独立 endpoint。
