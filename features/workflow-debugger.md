# F25 · 工作流调试器（变量监视 + 单步重跑 + 错误分支）

> 状态：`open`。来源：F7 工作流平台化 program 子项 **⑦**。本文档为 feature-builder 取数单元骨架；实现前须先锁定 §6 决策（尤其单步重跑对执行引擎的侵入程度）。

## 0. 目标
为工作流提供「开发期可观测 + 可干预」的调试能力：实时变量监视、单步执行/重跑单个节点、出错时从错误分支恢复。对标 Dify 调试模式 / LangGraph 断点。

## 1. 范围
**in**：
- **变量监视**：调试运行时展示各节点产出变量（复用 F20 Variable 节点 + F24 Trace 数据）。
- **单步**：画布「调试运行」模式，支持逐步执行（节点级暂停/继续）、单节点重跑（改配置后重跑该节点，不重跑全链）。
- **错误分支**：节点失败时允许人工介入修正输入/配置后从该节点重跑（而非整链重来）。
- 调试态 UI：画布调试面板 + 变量抽屉 + 节点状态高亮。
- 多租户隔离 + 审计（DebugRun/StepRetry）。

**out（v1）**：分布式断点持久化、调试会话共享（v1 单用户调试态）、与 F21 触发器联调。

## 2. 接口契约草案（后端）
- `POST /api/v1/workflows/{id}/debug/run` body `{ mode: step|full, initialContext }` → 启动调试 execution（Admin,Operator），返回首节点待执行态。
- `POST /api/v1/executions/{id}/debug/step` → 执行下一节点，返回该节点结果 + 变量快照。
- `POST /api/v1/executions/{id}/debug/retry-node` body `{ nodeId, overriddenConfig?, overriddenInput? }` → 重跑指定节点。
- `GET /api/v1/executions/{id}/debug/variables` → 当前变量快照。

## 3. 数据模型与改动面
- 复用 `ExecutionLog` + `Entries`（F24 Trace 字段）；调试态 = execution 的一种运行模式（新增 `RunMode` 枚举：Normal/Debug，或复用现有 status 扩展）。
- `WorkflowOrchestrator` 新增「单步/重跑节点」能力（按 nodeId 定位 + 注入 override + 局部重算下游），**对现有全量运行路径为非破坏性扩展**（新分支，不影响 `RunWorkflowCommand`）。
- 前端：画布「调试」模式切换 + 步进控件 + 变量监视抽屉 + 节点错误重试按钮。

## 4. 风险
- 🔴 高风险：执行引擎需支持「暂停/单步/局部重跑」，对编排内核侵入大；重跑节点后下游一致性（需重算受影响子图）。
- 缓解：调试能力作为 orchestrator 独立分支（不影响生产 `RunWorkflowCommand`）；单步态用 execution 持久化（可恢复）；先做变量监视 + 错误重跑（高价值低风险），单步全链作为增强。

## 5. 验收标准草案
- 调试运行可逐步执行，每步变量快照正确、节点状态高亮。
- 单节点重跑（带 override）仅重算该节点 + 下游受影响子图，结果与全量重跑一致。
- 错误节点可人工修正后从该节点恢复。
- 多租户隔离；审计落库；前端 tsc 0 + qa.mjs 全绿。

## 6. 决策（待锁定）
- **S1** 执行引擎介入深度：orchestrator 新增单步分支（v1）vs 引入独立调试执行器（重构大）。
- **S2** 调试态存储：复用 ExecutionLog（加 RunMode）vs 独立 DebugSession 聚合。
- **S3** 重跑影响范围：仅该节点 vs 该节点 + 下游子图（建议后者）。
- **S4** 与 F24 关系：调试变量监视复用 Trace 数据字段（优先复用，避免双写）。
