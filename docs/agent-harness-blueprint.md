# dev-agent → 真 Agent Harness 升级蓝图（Phase 7+ 路线图）

> **文档性质**：架构分析报告 + 下一版本蓝图。所有"现状"结论均来自对 `E:\Freelancer\AI_Projects\dev-agent\src` 的**直接代码核实**（接口/类/方法名 + 行号，2026-08-06），非记忆快照。
> **关联**：`../AGENT_PLATFORM_BLUEPRINT.md`（总体蓝图）、`quality/*`（质量门）、`../features/backlog.md`（实现意图池）。
> **置信度**：现状章节为已核实事实；路线图章节为方案建议，需 Phase 启动时再逐项锁定决策。

> **二期排期变更（2026-08-12）**：`features/backlog.md` 二期现以 **F29 = Agentic Agent Primitive（自主控制循环原语）** 置顶（P0，独立轨道，先于 Phase 7–11 启动）。Phase 7–11 路线图本身不变，仅在其前插入该范式级 feature；原 Phase 7–11 对应 feature 顺延为 **F30–F34**（执行持久化 / Agent 运行时实体化 / 消息总线 / 语义记忆 / 在线评估门禁）。设计文档 `features/agentic-agent-primitive.md`，现状核实同日。

## 0. 方法与偏差纠正

本报告以代码核实为准。过程中纠正了先前对话中的两处假设偏差，特此声明以保证可靠性：

- **偏差 1（模型层）**：先前假设"模型层单一、无 router"。核实：`ModelRouter`（`Application/Routing/Services/ModelRouter.cs`）+ `TenantModelClientResolver`（`CreateForTenant`）**已存在**，支持候选优先级回退与租户 BYO 模型。真实差距是**路由未接通到 agent 级** + `AgentCallStepExecutor` 硬编码 `_settings.DefaultModelId` 且**忽略 agent 的 `SystemPrompt`/`ModelEndpoint`**。
- **偏差 2（持久化/HITL）**：先前假设"无持久化、无暂停"。核实：HITL `HumanApproval` 落库、状态机 `Paused`、`ResumeAsync` 从仓库重载续跑——**已具备可中断/可续跑的有状态编排**，只是触发器仍是请求同步。

---

## 1. 现状核实（Verified Current State）

### 1.1 执行 / 编排核心
- 接口 `IOrchestrationPrimitive`（`Application/Abstractions/IOrchestrationPrimitive.cs`）→ 实现 `OrchestrationPrimitive`（`Infrastructure/Workflows/OrchestrationPrimitive.cs`），入口 `RunAsync(Workflow, OrchestrationPreset, ct)`（:112）。
- **同步执行**：`SequentialOrchestrator.RunToCompletionAsync`（:177）以 `do/while` 跑完所有 `Pending` 节点；HTTP `WorkflowsController.RunWorkflow`（:94）直接 `await _mediator.Send(...)` 在**同一请求内同步跑完**返回。
- 状态机：`WorkflowState` 含 `Pending/Running/Paused/Completed/RolledBack/Failed`，有 `PauseAsync/ResumeAsync/RetryStepAsync/RollbackToAsync`。
- **in-flight 控制**：`static ConcurrentDictionary<Guid, RunningCtsEntry> s_runningCts`（:50）+ `Timer` 驱逐——**进程内、非持久**。
- 后台服务：`WorkflowScheduler : BackgroundService` 仅**轮询触发器再调同一个同步 `RunAsync`**，**非 durable 执行框架**。

### 1.2 Agent 模型（关键缺陷）
- `Agent` 聚合根（`Domain/Aggregates/Agents/Agent.cs`，`ITenantScoped`）+ `AgentRoleDefinition`（`Domain/Aggregates/AgentRoleDefinitions/AgentRoleDefinition.cs`）——**均为配置实体，非运行时进程**。
- 节点绑定：`Workflow.AssignAgentToNode` / `AgentAssignments`（`Workflow.cs:137,292`），`WorkflowNode.AgentId`。
- **缺陷（高优先）**：`AgentCallStepExecutor`（`Infrastructure/Workflows/AgentCallStepExecutor.cs:50`）硬编码 prompt 与 `_settings.DefaultModelId`，**未加载 agent 的 `SystemPrompt`/`ModelEndpoint`**。即"配置了 agent，执行时却不生效"。

### 1.3 HITL（人在回路）
- `UserInputStepExecutor`：首次执行建 `HumanApproval`(Pending) 落库 → 返回 `NeedsIntervention` → 编排器置 `Paused`（SequentialOrchestrator.cs:304）。
- 恢复：`ResolveApprovalCommandHandler.cs:73` 写回结果 → `ResumeAsync`。暂停/恢复状态存 DB。

### 1.4 运行持久化
- `ExecutionLog` + `ExecutionLogEntry`（`Domain/Aggregates/ExecutionLogs/`）；每步 `Update` + `SaveChangesAsync`（SequentialOrchestrator.cs:272）。`ResumeAsync` 从仓库重载续跑——**运行中持久化、可中途续跑**。Blackboard 为内存可变传递（非独立聚合）。

### 1.5 记忆
- 仅 RAG 向量检索：`IVectorStore`（Pg/InMemory）+ `BuildWorkflowContext._vectorStore.SearchAsync`（SequentialOrchestrator.cs:462）。明文截断：`MaxSummaryTokens`（:481）。**无 embedding 生成、无语义/情节记忆、无自动 compaction 服务**。

### 1.6 模型层
- `SemanticKernelModelClient`（`Infrastructure/Models/SemanticKernelModelClient.cs`，`IChatCompletionService`）。`ModelRouter` + `TenantModelClientResolver`（`CreateForTenant`）**已存在**——回退/多模型/租户 BYO 已具备，**未接到 agent 级**。

### 1.7 Trace / Eval（F24）
- `ExecutionLogEntry` 含 `TokensIn/TokensOut` + `NodeType`（`StepType`）；`EvaluationDataset` + `EvaluationCase`（`Domain/Aggregates/Evaluation/`）；`EvaluationDatasetsController` POST `/run`——已落地。

### 1.8 多租户 / 鉴权（Phase 5）
- `TenantProvider`（从 JWT `tenant_id` / `X-Tenant-Id` 解析）+ `ITenantScoped`（几乎所有聚合）；`JwtTokenService` / `AuthEndpoints` / `ApiKeyAuthenticationHandler` / `ApiKey` + `ApiKeyEncryptionService`；`AuditLog` 聚合——均已落地。

---

## 2. 差距分析（Gap → 真 Harness）

| 维度 | 现状 | 真 Harness 要求 | 差距 |
|---|---|---|---|
| 运行时范式 | 请求同步 `RunAsync`；in-flight 控于进程内 `ConcurrentDictionary` | 常驻、durable、可挂起/恢复、事件/定时唤醒 | 🔴 缺失 durable 框架 |
| 多 agent 原语 | agent 是配置实体；executor 忽略其 prompt/model；无消息总线/独立上下文/并行 | agent 一等公民：独立上下文、消息总线、并行推理、handoff | 🔴 缺失（且配置失效） |
| 语义记忆 | 仅 RAG 检索；无 embedding 生成/compaction | 向量写入 + episodic 写回 + 自动 compaction | 🔴 缺失 |
| 模型绑定 | `ModelRouter` 存在但未接 agent | agent 级模型路由 + fallback | 🟡 部分（接线缺口） |
| 规划/反思 | 涌现式，非工程化 | plan mode / self-critique / tree-of-thought | 🟡 缺失 |
| 可观测/评估 | F24 trace + 数据集回归 | 在线门禁 + 影子 eval + 成本归因 | 🟢 接近（缺在线闭环） |
| HITL | DB 持久化暂停/恢复 | 细粒度中断-恢复 checkpoint | 🟢 已具备 |
| 部署/扩展 | 多租户/鉴权有；无队列/水平扩展 | 队列化 + 常驻运行时 + 水平扩展 | 🟡 部分 |

---

## 3. 目标架构（Target Architecture）

在现有 DDD 分层之上**叠加**六层，不推翻既有骨架：

1. **Durable Execution Layer（持久执行层）**：引入工作流持久化执行框架（候选见 §5 D1），把 `RunToCompletionAsync` 改为**可挂起协程**，每步落检查点；`WorkflowScheduler` 升级为 durable 驱动器；in-flight 状态外置 DB，进程重启可恢复。
2. **Agent Runtime（agent 运行时）**：把 `Agent` 从配置实体提升为运行时实体——每 agent 实例有独立上下文窗口（Blackboard 分区 / 独立对话历史）、加载自身 `SystemPrompt`+`ModelEndpoint`（接通 `ModelRouter`+`TenantModelClientResolver`）；引入**消息总线**（in-process `Channel<T>` → 可选 broker）让 agent 间发消息、handoff、协商；`NegotiationOrchestrator` 升级为真正的多 agent 协作（非单 LLM 步骤选择）。
3. **Semantic Memory Layer（语义记忆层）**：`IEmbeddingGenerator` + 向量写入（`IVectorStore` 已存在）；agent 可写回 episodic 记忆；自动 compaction（把 `MaxSummaryTokens` 截断升级为摘要服务）；跨会话检索。
4. **Online Eval Gate（在线评估门禁）**：把 F24 数据集回归变为**生产前/影子流量回归门禁** + 在线监控（token/cost/latency 告警）+ 自动回归挂 CI。
5. **Model Routing per Agent（agent 级模型路由）**：修复 `AgentCallStepExecutor` 接通 agent 配置；router 支持 agent 级 fallback。
6. **Observability / Governance**：trace 已具备；补 cost 归因到 agent/tenant、异常回放。

---

## 4. 路线图（Phase 7 → 真 Harness）

每阶段独立可交付、独立质量门（沿用 feature-builder 八阶段 + 三道质量门）。

### Phase 7 · 执行持久化（Durable Execution）— P0
> 详细阶段计划：[`../phases/phase-7-durable-execution.md`](../phases/phase-7-durable-execution.md)
- **范围**：引入检查点机制；`RunToCompletionAsync` 改造为可挂起/恢复；in-flight 状态外置 DB；`WorkflowScheduler` 升级为 durable 驱动器；进程崩溃可恢复。
- **关键改动**：`OrchestrationPrimitive` 检查点、`ExecutionLog` 增 `CheckpointData`、`ConcurrentDictionary`→DB。
- **风险**：长事务一致性；存量工作流兼容；每步 `SaveChangesAsync` 性能瓶颈（批处理/检查点合并）。
- **验收**：kill 进程后运行中工作流从检查点恢复；压测无数据损坏。

### Phase 8 · Agent 运行时实体化 + 模型接通 — P0
> 详细阶段计划：[`../phases/phase-8-agent-runtime.md`](../phases/phase-8-agent-runtime.md)
- **范围**：修复 `AgentCallStepExecutor` 加载 agent 的 `SystemPrompt`/`ModelEndpoint`；Blackboard 按 agent 分区；引入 agent 上下文窗口；接通 `ModelRouter`+`TenantModelClientResolver`。
- **前置**：补 agent 种子（SystemPrompt/ModelEndpoint 字段 + 迁移）。
- **风险**：现有 agent 配置缺字段；prompt 泄露租户隔离。
- **验收**：同工作流不同 agent 节点行为不同；agent 级模型路由生效；租户 BYO 模型隔离。

### Phase 9 · Agent 消息总线 + 多 agent 协作 — P1
> 详细阶段计划：[`../phases/phase-9-agent-message-bus.md`](../phases/phase-9-agent-message-bus.md)
- **范围**：`AgentMessageBus`（`Channel<T>` 起步）+ 消息类型；`NegotiationOrchestrator` 升级为真正并行 agent 协作；handoff 模式；消息持久化 + 幂等。
- **风险**：死锁/消息风暴；活锁。
- **验收**：N 个 agent 并行推理 + 消息收敛；无活锁；可观测消息流。

### Phase 10 · 语义记忆层 — P1
> 详细阶段计划：[`../phases/phase-10-semantic-memory.md`](../phases/phase-10-semantic-memory.md)
- **范围**：`IEmbeddingGenerator` + 向量写入 + episodic 写回 + 自动 compaction 服务。
- **风险**：embedding 成本；检索质量；租户向量隔离。
- **验收**：agent 跨会话召回相关记忆；长上下文自动压缩不丢关键事实。

### Phase 11 · 在线评估门禁 + 部署闭环 — P2
> 详细阶段计划：[`../phases/phase-11-online-eval-gate.md`](../phases/phase-11-online-eval-gate.md)
- **范围**：影子流量回归 + 在线监控告警 + CI 自动回归；队列化执行支持水平扩展。
- **验收**：生产变更前自动跑 eval 门禁；执行可水平扩展。

---

## 5. 待锁定决策（Decisions to Lock）

- **D1 Durable 框架选型**：Workflow Core（轻、EF 友好）/ Dapr Workflow（分布式）/ **自建基于 `ExecutionLog` 的检查点**（复用现有 per-step 持久化，成本最低、风险最小）。建议 Phase 7 先自建检查点，分布式留待 Phase 11。
- **D2 消息总线传输**：in-process `Channel<T>`（Phase 9 起步）→ 可选 broker（Phase 11）。
- **D3 向量后端**：复用 `IVectorStore`（Pg 已支持）+ 选 embedding 模型（OpenAI 兼容 / 本地）。
- **D4 agent 上下文隔离粒度**：Blackboard 按 agent 分区 vs 每 agent 独立对话历史（建议两者结合：分区 Blackboard + 独立 message 历史）。

---

## 6. 风险与缓解

- **历史漂移**：文档 vs 代码——本报告以代码核实为准；后续每阶段启动前重核实关键类。
- **agent 配置缺失**：Phase 8 前须补 agent 种子（`SystemPrompt`/`ModelEndpoint`），否则修复 executor 后行为退化。
- **进程内 CTS 驱逐**：`Timer` 驱逐逻辑在 durable 化后废弃。
- **性能**：每步 `SaveChangesAsync` 已是瓶颈候选，Phase 7 需检查点合并/批处理。
- **租户隔离**：记忆层、模型 BYO、agent 上下文分区均须复用 `ITenantScoped` + `TenantProvider`，不得绕过。

---

## 7. Definition of Done（"真 Harness" 判定）

dev-agent 升级为真 harness 当且仅当满足：

1. 工作流可在进程崩溃后从检查点恢复（durable）。
2. agent 是一等运行时实体，配置（prompt/model）在执行时生效，且有独立上下文。
3. 至少一个多 agent 协作场景经消息总线并行收敛。
4. agent 具备跨会话语义/情节记忆与自动 compaction。
5. 生产变更经自动 eval 门禁 + 在线监控。
6. 执行可水平扩展（队列化）。

---

## 8. 下一步建议

建议下一版本以 **Phase 7（Durable Execution）+ Phase 8（Agent 运行时实体化 + 模型接通）** 组成最小闭环——两者直接消除最大两块差距（持久化 + agent 配置失效），风险可控，且复用现有 per-step 持久化与 `ModelRouter`。Phase 9–11 视 Phase 7/8 落地情况滚动排期。

落地时请遵循项目既有铁律：新聚合/迁移必 `dotnet ef migrations add`（含 `#pragma warning disable IDE0161`）、`ValueGeneratedNever()` 避 GUID 陷阱、`src/` 与 `.quality-gate.json` 同笔暂存且含 `Quality-Gate:` 行、不 push（沙箱无 GitHub 出站网络）。
