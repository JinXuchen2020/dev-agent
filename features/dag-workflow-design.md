# P1 · 可视化 DAG 画布 MVP — 设计文档

> 关联：`backlog.md` §5（P1 · 可视化 DAG 画布 MVP，high-risk）/ `competitive-roadmap.md` §4 P1。
> 本文是 P1 的实现契约。P1 涉及后端 DAG 模型 + 端点演进 + 前端画布重构，**属红线 high-risk 改动**，需先拍板 §8 决策点再进入实现。
> 生成日期：2026-07-23

---

## 0. 目标与范围（MVP 边界）

**做（P1 范围内）**
- 后端引入 `WorkflowNode` / `WorkflowEdge` + `StepType` 枚举，工作流从「线性步骤列表」升级为「有向图」。
- `SequentialOrchestrator` 改为**拓扑序执行**（Kahn），保留现有重试 / 回滚 / `NeedsIntervention` 行为。
- executor 路由从「`StepName` 字符串 glob」改为「`StepType` 枚举匹配」（`*` 仍作兜底）。
- 前端 `@xyflow/react` 画布：拖拽放置 / 连线 / 框选 / 缩放 / 小地图 / 网格对齐 / 撤销重做。
- 节点配置侧栏（按 `StepType` 渲染表单）+ 5 个基础节点：Start / End / LLM / Agent / Critic。
- 单步试运行（调试）+ 变量监视（基础版）。

**不做（留给 P2/P3，本文不实现）**
- 节点全家桶（Code/HTTP/Tool/Knowledge/Condition/Loop/Variable/SubWorkflow/Delay/UserInput）。
- 版本管理 / 导入导出 / 触发器（Webhook/cron/Chat）/ 发布为 API·MCP / Trace 评估视图 / 企业增强。
- 协商式多智能体的专属画布模式（P3 产品化，后端 `NegotiationOrchestrator` 已具备）。

---

## 1. 后端 DAG 模型

### 1.1 `StepType` 枚举（Domain.Enums）
```csharp
public enum StepType
{
    Start = 0,   // 入口，不执行 LLM，标记 workflow 开始
    End = 1,     // 出口，产出汇总 artifact
    LLM = 2,     // 一次 LLM 调用（默认 agent 兜底）
    Agent = 3,   // 分配给特定 agent 的 LLM 调用
    Critic = 4   // 评审步（收敛/评审）
    // P2 预留：Code, Http, Tool, Knowledge, Condition, Loop, Variable, SubWorkflow, Delay, UserInput
}
```

### 1.2 `WorkflowNode` 实体（Domain.Aggregates.Workflows）
```csharp
public sealed class WorkflowNode
{
    public Guid Id { get; private init; }
    public Guid WorkflowId { get; private init; }
    public StepType Type { get; private set; }          // 路由依据
    public string Name { get; private set; }            // 展示名 + agent 分配键
    public int Order { get; private set; }              // 拓扑序（由图推导，存为缓存）
    public double PositionX { get; private set; }       // 画布坐标
    public double PositionY { get; private set; }
    public string ConfigJson { get; private set; } = "{}"; // 节点配置（systemPrompt/agentId/评审标准…）
    public WorkflowState State { get; private set; }
    public string? Result { get; private set; }
    public string? ErrorDetail { get; private set; }
    public Guid? AssignedAgentId { get; private set; }

    // 方法：Rename / SetType / UpdatePosition / UpdateConfig / AssignAgent
    //      SetState / SetResult / SetError
}
```

### 1.3 `WorkflowEdge` 实体（Domain.Aggregates.Workflows）
```csharp
public sealed class WorkflowEdge
{
    public Guid Id { get; private init; }
    public Guid WorkflowId { get; private init; }
    public Guid SourceNodeId { get; private init; }
    public Guid TargetNodeId { get; private init; }
    public string? Label { get; private set; }
}
```

### 1.4 `Workflow` 聚合改造
- 删除 `List<WorkflowStep> _steps` → 新增 `List<WorkflowNode> _nodes` + `List<WorkflowEdge> _edges`。
- 保留 `AgentAssignments` 字典（键改为 node `Name`）。
- 保留 `ReplaceSteps(IReadOnlyList<string>)` **兼容旧线性写入**（内部转成链：nodes[0..n] + edges[i]→[i+1]），供 P0 端点与旧数据过渡。
- 新增领域方法：
  - `AddNode(StepType type, string name, double x, double y, string? configJson)`
  - `AddEdge(Guid sourceId, Guid targetId, string? label)`
  - `RemoveNode(Guid id)` / `RemoveEdge(Guid id)`
  - `RenameNode(Guid id, string name)` / `SetNodeConfig(Guid id, string configJson)` / `AssignAgentToNode(Guid id, Guid agentId)`
  - `IReadOnlyList<WorkflowNode> GetTopologicalOrder()`（Kahn，失败抛 `WorkflowGraphException`）
  - `void ValidateGraph()`（见 §2；失败抛 `WorkflowGraphException`）
- 计算属性 `Steps`：投影为有序 `WorkflowStep` 链，**仅用于向后兼容旧读取方**（如现有 `WorkflowDetailResponse` 过渡期）。新端点优先返回 `Nodes`/`Edges`。

---

## 2. 图校验（ValidateGraph）

`Workflow.ValidateGraph()` 在保存（PUT）与运行（run）前调用，抛 `WorkflowGraphException`（映射 422）：
1. **入口**：恰好 1 个 `Start`（入度 0）。
2. **出口**：≥1 个 `End`（出度 0）。
3. **无环**：DFS 三色染色检测回边。
4. **连通性**：所有节点从 `Start` 可达，且能到达某 `End`。
5. **节点命名**：`Name` 非空且唯一（agent 分配键唯一）。

新增 `Domain/WorkflowGraphException.cs`：`public class WorkflowGraphException : Exception`（namespace `AgentPlatform.Domain`）。

---

## 3. 执行引擎改造

### 3.1 `SequentialOrchestrator` → 拓扑序
- `RunSequentialAsync(workflow, ct)` 改为：`var ordered = workflow.GetTopologicalOrder();` 按拓扑序遍历（替代 `OrderBy(s => s.Order)`）。
- 每个节点的 `BuildWorkflowContext` 改为**只收集其已完成前驱节点**的 artifacts（通过 `Edges` 求前驱），替代「所有已完成步骤」。
- 末节点（`End` 或拓扑末位）完成 → `workflow.Complete()`。
- 重试 / 回滚 / `NeedsIntervention` 逻辑保持现有（回滚范围按拓扑序而非 `Order`）。

### 3.2 executor 路由改造
- `IStepExecutor` 新增成员：`StepType HandlesType { get; }`（枚举）；保留现有 `string StepType` 属性作为**向后兼容别名**（旧 `*critic*` 改为 `HandlesType = StepType.Critic`）。
- 节点路由：`ResolveExecutor(node)`：
  ```csharp
  var byType = executors.FirstOrDefault(e => e.HandlesType == node.Type);
  if (byType != null) return byType;
  return executors.FirstOrDefault(e => e.StepType == "*")   // AgentCall 兜底
      ?? executors.FirstOrDefault();
  ```
- `AgentCallStepExecutor.StepType => "*"` 不变（兜底任何未识别类型）；新增 `HandlesType => StepType.LLM`。
- `CriticStepExecutor`：`HandlesType => StepType.Critic`（替代 `*critic*` glob）。
- `Start` 节点：不调用 LLM，`ResolveExecutor` 返回 `null` 时由 orchestrator 直接产出「入口」artifact（`SetResult("[start]")`）。
- `End` 节点：由 `AgentCallStepExecutor` 兜底产出汇总（P2 可做专门汇总 executor）。

---

## 4. 端点契约

### 4.1 `PUT /api/v1/workflows/{id}`（扩展，向后兼容）
请求体（与 P0 的 `UpdateWorkflowCommand` 合并，新增 `Nodes`/`Edges`）：
```jsonc
{
  "name": "可选",
  "initialContext": "可选(JSON)",
  "steps": ["可选·旧线性，兼容"],          // 仅 steps → 旧 P0 行为（链化）
  "nodes": [                               // nodes+edges → DAG（优先）
    { "id": "n1", "type": "Start",  "name": "入口", "position": {"x":0,"y":0}, "config": {} },
    { "id": "n2", "type": "LLM",    "name": "起草", "position": {"x":0,"y":100}, "config": {"systemPrompt":"..."} },
    { "id": "n3", "type": "Critic", "name": "评审", "position": {"x":0,"y":200}, "config": {"criteria":"..."} },
    { "id": "n4", "type": "End",    "name": "出口", "position": {"x":0,"y":300}, "config": {} }
  ],
  "edges": [
    { "id": "e1", "source": "n1", "target": "n2" },
    { "id": "e2", "source": "n2", "target": "n3" },
    { "id": "e3", "source": "n3", "target": "n4" }
  ]
}
```
规则：
- 传 `nodes`+`edges` → 走 DAG（`ValidateGraph` + 拓扑序）。
- 只传 `steps` → 旧 P0 链化行为（向后兼容）。
- 两者都传 → **以 `nodes`/`edges` 为准**（覆盖）。

### 4.2 `GET /api/v1/workflows/{id}` 返回增 `nodes`/`edges`
`WorkflowDetailResponse` 新增 `Nodes` / `Edges` 字段（旧 `Steps` 保留过渡期）。

### 4.3 `POST /api/v1/workflows/{id}/run`（复用）
run 处理器调用 `GetTopologicalOrder()` 拓扑序执行（§3.1）。

### 4.4 `POST /api/v1/workflows/{id}/nodes/{nodeId}/run`（单步试运行，调试）
- 仅执行该节点：`ResolveExecutor` → `ExecuteStepWithRetryAsync`（单节点）→ 写该节点 `Result` / `State`。
- **不推进**整体 workflow 状态；若 workflow 非 Running/Paused 则先置 Running。
- 失败抛 `WorkflowConflictException`（Running 中不可单步）映射 409。

---

## 5. 前端画布（@xyflow/react 已装）

- 新增 `src/pages/WorkflowCanvasPage.tsx`（或改造 `WorkflowEditorPage`）。
- zustand store `workflowCanvasStore`：`nodes` / `edges` / `selectedId` / `history`（撤销重做栈）。
- 画布能力：拖拽放置节点（左侧节点面板拖入）、连线（handle 拖拽）、框选、缩放、小地图（`MiniMap`）、网格背景、Ctrl+Z/Y 撤销重做。
- 节点类型映射：`start|end|llm|agent|critic` → 对应 XYNode 组件（带类型图标 + 名称 + 状态色）。
- 载入：进入 `/edit` 调 `GET /{id}` → 用 `nodes`/`edges` 渲染（无则把旧 `steps` 链化显示）。
- 保存：「保存草稿」→ `PUT /{id}` 传 `nodes`+`edges`；「保存并运行」→ PUT + `POST /{id}/run`。

---

## 6. 节点配置侧栏

按 `node.type` 渲染表单（右侧 `Drawer`/`Panel`）：
- **Start**：`initialContext` 入口（JSON）。
- **LLM**：systemPrompt 模板（复用 context，可插 `{{artifacts}}` 占位）。
- **Agent**：分配 agent（下拉，来自 `GET /agents`）；写入 `node.config.agentId` + `AssignedAgentId`。
- **Critic**：评审标准（criteria 文本）。
- **End**：汇总方式（默认全部 artifacts 拼接）。
- 通用：重命名 / 删除节点 / 编辑坐标。

---

## 7. 调试（MVP）

- **单步试运行**：节点右键/工具栏「试运行」→ `POST /{id}/nodes/{nodeId}/run`；完成后刷新节点 `Result` 与状态色。
- **变量监视**：侧栏/底部面板展示已完成节点的 `Result`（轮询 `GET /{id}` 或复用 SSE 进度流），展示节点输入（前驱 artifacts）/输出。
- 后端真跑单步（§4.4），不依赖前端内存模拟——保证与真实执行一致。

---

## 8. High-risk 决策点（需用户拍板）

> 下列为 P1 的 5 个 high-risk 点，**默认推荐已标注（推荐）**。用户须逐点确认或选择备选，实现代理才会动手。

1. **模型策略**
   - （推荐）直接引入完整 DAG：`WorkflowNode` + `WorkflowEdge`，线性 workflow 视为「链化 DAG」。
   - 备选：过渡形态——保留 `List<WorkflowStep>`，仅加 `DependsOn`（父子依赖），不引入 Edge 实体。

2. **端点演进**
   - （推荐）扩展现有 `PUT /{id}` 合并 `nodes`+`edges`，向后兼容 `steps`（双模式）。
   - 备选：新增独立 `PUT /api/v1/workflows/{id}/graph`，旧 `PUT` 仅管元数据。

3. **executor 路由**
   - （推荐）枚举 `HandlesType` + `*` 兜底；旧 `*critic*` 改绑 `StepType.Critic`。
   - 备选：保留 `StepName` 字符串 glob 匹配，DAG 仅改存储不改路由。

4. **兼容迁移**
   - （推荐）旧 `steps` 读取时链化为 `nodes`+`edges`；新写统一 DAG；过渡期 `WorkflowDetailResponse` 同时返回 `Steps`/`Nodes`/`Edges`。
   - 备选：双模型长期共存（线性 + DAG 各自端点），不强制迁移。

5. **单步试运行实现**
   - （推荐）后端真 `RunStepAsync`（`POST nodes/{id}/run`），与真实执行一致。
   - 备选：前端内存模拟（不发后端请求，仅本地演示），后端 P2 再补。

---

## 9. 验收清单

**后端**
- `dotnet build` 0 warning / 0 error。
- 单测：
  - `Workflow.ValidateGraph`：环 / 多入口 / 无出口 / 不连通 均抛 `WorkflowGraphException`。
  - `GetTopologicalOrder`：链 / 菱形 / 多分支 拓扑序正确。
  - `ResolveExecutor`：按 `StepType` 命中；未知类型落 `*` 兜底。
  - 迁移：旧 `steps` 链化为 `nodes`+`edges` 后图校验通过。
- `PUT` 传 `nodes`+`edges` 后 `GET` 回显一致；`run` 按拓扑序；`nodes/{id}/run` 单步成功。

**前端**
- 画布拖拽/连线/缩放/小地图/撤销重做可用。
- 保存草稿 → `PUT` 传 `nodes`+`edges`；重新进入回显正确。
- 配置侧栏按类型渲染；Agent 节点能选 agent。
- 单步试运行写入节点结果，变量监视面板可读。
- 五道 QA 闸门（typecheck/lint/build/unit/e2e）全绿。

---

## 10. 实现任务拆分（供 feature-dev 消费）

**后端**
1. `StepType` 枚举（`Domain.Enums`）。
2. `WorkflowNode` / `WorkflowEdge` 实体 + `WorkflowGraphException`。
3. `Workflow` 聚合改造：`_nodes`/`_edges` + 领域方法 + `GetTopologicalOrder` + `ValidateGraph` + `ReplaceSteps` 链化兼容。
4. EF 配置（`WorkflowNodeConfiguration` / `WorkflowEdgeConfiguration`）+ 迁移。
5. `SequentialOrchestrator` 拓扑序 + `BuildWorkflowContext` 仅前驱 artifacts。
6. `IStepExecutor.HandlesType` + 两个 executor 绑定。
7. `UpdateWorkflowCommand/Handler` 扩展 `Nodes`/`Edges`；`RunExistingWorkflowCommand/Handler` 拓扑序；新增 `RunNodeCommand/Handler`。
8. `WorkflowsController`：`PUT` 扩展 + `POST /{id}/nodes/{nodeId}/run`；`WorkflowGraphExceptionHandler` → 422。
9. `WorkflowDetailResponse` 增 `Nodes`/`Edges`。

**前端**
1. `workflowCanvasStore`（zustand）+ history 栈。
2. `WorkflowCanvasPage` + XYNode 组件（5 类型）+ 拖拽/连线/缩放/小地图/撤销重做。
3. 节点配置侧栏（按类型表单）。
4. `api.ts`：扩展 `updateWorkflow`（nodes/edges）、`runExistingWorkflow`、`runWorkflowNode`。
5. 调试：单步试运行按钮 + 变量监视面板。
6. 接线：进入 `/edit` 载入 `nodes`/`edges`；保存草稿/运行分拆。

---

## 11. 风险与回归

- **回归**：现有 P0 端点（`PUT` 仅 `steps`、`POST /{id}/run`）必须保持可用——双模式兼容（§4.1/§8.4）保障。
- **数据迁移**：已存 `WorkflowStep` 记录在 `ReplaceSteps` 链化 + 读取投影下不破坏；EF 迁移新增 `WorkflowNodes`/`WorkflowEdges` 表，旧 `WorkflowSteps` 表可保留或迁移脚本转写。
- **多租户**：`WorkflowNode`/`WorkflowEdge` 继承 `ITenantScoped`（或经 `WorkflowId` 间接隔离），`HasQueryFilter` 已覆盖聚合根，需确认导航属性隔离生效（参考 R2 教训，加租户回归测试）。
