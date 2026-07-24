# P1 质量门报告 · 可视化 DAG 画布 MVP

> 关联设计：`../features/dag-workflow-design.md`
> 关联待办：`../features/backlog.md` §五「P1 · 可视化 DAG 画布 MVP」
> 提交：与 `src/` 改动一同暂存 `.quality-gate.json`（cleared: true），commit message 含 `Quality-Gate:` 行。

## 1. 范围

后端 DAG 模型 + 前端画布，一次性提交（匹配 P0 单提交先例，且 pre-commit 门要求 `src/` 与 `.quality-gate.json` 同暂存）。

后端：`WorkflowNode`/`WorkflowEdge`/`StepType`/`WorkflowGraphException`/`IWorkflowExecutable`；`Workflow` 聚合（`ReplaceGraph`/`AddNode`/`AddEdge`/`GetTopologicalOrder`/`ValidateGraph`/`SyncStepsFromGraph`/`EnsureGraphSynced`）；`IWorkflowNodeRunner`/`WorkflowNodeRunner`；`RunNodeCommand`+Handler；`UpdateWorkflowCommand` Nodes/Edges；`GetWorkflowQuery.ToDetailResponse`；`SequentialOrchestrator` DAG 拓扑序路由；`AgentCallStepExecutor`/`CriticStepExecutor` 绑定 `HandlesType`；`WorkflowsController` `POST /{id}/nodes/{nodeId}/run` + `WorkflowGraphExceptionHandler`(422)；EF 迁移 `20260723010228_Phase6DagWorkflowNodes`；`WorkflowConfiguration` OwnsMany。

前端：`workflowCanvasStore`（zustand + 撤销/重做历史栈）；`WorkflowCanvasPage` + `DagNode`/`NodePalette`/`NodeConfigPanel`/`VariableWatchPanel`；`api.ts` 加 JWT 请求拦截器 + `runWorkflowNode`；`types/index.ts` P1 类型；`App.tsx` 接线（删 `WorkflowEditorPage`，改 `WorkflowCanvasPage`）。

## 2. 评审结果

### ddd-code-reviewer（对抗式代码评审）
- **P0/P1/P2：0 open。** 重点追查的高风险路径：
  - `WorkflowNodeRunner.ResolveExecutor` 按 `StepType` 命中、未知类型落 `*` 兜底、glob 按名匹配 —— 单元测试覆盖（见 §3），行为正确。
  - `UpdateWorkflowCommandHandler`/`RunNodeCommandHandler` 均校验 `TenantId` → 越权返回 404（不泄露存在性）。
  - `WorkflowConflictException` 已有 `WorkflowConflictExceptionHandler` → HTTP 409（Program.cs 注册）。
  - `SequentialOrchestrator` DAG 执行顺序排除 Start/End，末节点完成即 `Complete()`；拓扑序保证前驱先完成，收敛正确。
  - `ReplaceGraph` 边按 TempId 重映射，孤立边静默丢弃（前端保证同 id 引用，无悬空）。
  - 多租户：`WorkflowNode`/`WorkflowEdge` 经 `Workflow` 聚合 `OwnsMany` 拥有，全局 `HasQueryFilter`（Phase 5 已落地）随聚合根级联隔离，节点/边不独立可查，无需单测（见设计 §11 风险注记）。
- **P3：0 open（本轮新修）。** 上一轮 `ddd-phase-quality-gate` 标记的两项 P3（缺失 DAG 单测 + 新公共类型英文 XML 注释）已修复：新增 `WorkflowGraphTests`(12) + `WorkflowNodeRunnerTests`(4)；`WorkflowNode`/`WorkflowEdge`/`WorkflowGraphException`/`IWorkflowNodeRunner`/`WorkflowNodeRunner` 及 `Workflow` 新增 DAG 方法与属性摘要全部改为中文（项目约定）。

### ddd-phase-quality-gate（结构门）
- **PASS（P0=0 P1=0 P2=0 P3=0）。** 分层正确：命令/查询处理器在 Application，执行器在 Infrastructure，聚合不依赖基础设施；聚合不变量（`ValidateGraph`/`GetTopologicalOrder`/`SyncStepsFromGraph`）保留；前端无 god-component（store/页面/组件职责分离）。

### codebase-optimizer（等价检查，技能未安装，按 P0 先例记实）
- 前端 QA 五道闸门（`scripts/qa.mjs`）：**typecheck / lint / build / unit 全绿**（OVERALL PASS，qa-report.json）。
- 后端 `dotnet build`：**0 警告 0 错误**。
- 后端 `dotnet test`：**159 passed / 0 failed**（Application 77 · Infrastructure 21 · Api 9 · Integration 5 · Arch 6 · SpecFlow 41）。

## 3. 新增测试（覆盖设计 §9 后端验收）

- `AgentPlatform.Application.Tests/Workflows/WorkflowGraphTests.cs`（12 例）
  - `ValidateGraph`：无 End / 多 Start / 环 / 不连通 均抛 `WorkflowGraphException`；合法线性图通过。
  - `GetTopologicalOrder`：链 / 菱形 / 多分支 拓扑序正确；环抛异常。
  - 互转（roundtrip）：`ReplaceGraph` 同步遗留步骤（排除 Start/End）；`ReplaceSteps` 链化 + 投影保留名称。
- `AgentPlatform.Infrastructure.Tests/Workflows/WorkflowNodeRunnerTests.cs`（4 例）
  - `ResolveExecutor`：按 `StepType` 命中对应执行器；未知类型落 `*` 兜底；glob（`*critic*`）按名匹配；Start 节点跳过执行。
  - 注：调试中发现测试数据误用「CritiqueStep」（`critique` 含 q，不匹配 `*critic*`）——经核对确认解析器行为正确，修正测试数据后通过，非代码缺陷。

## 4. 结论

所有质量门（reviewer / structureGate / codebaseOptimizer 等价）**PASS（0 open）**。`.quality-gate.json` cleared:true，与 `src/` 改动一同提交。
