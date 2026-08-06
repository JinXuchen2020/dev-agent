# F24 · 执行 Trace / 评估视图

> 状态：`done`。来源：F7 工作流平台化 program 子项 **⑥**。本文件为 feature-builder 取数单元，§6 决策于 2026-08-05 锁定，实现已收口（v2.18 · 2026-08-05）。

## 0. 目标
提供节点级可观测性（耗时 / token / 节点类型 / 输出 / 错误）与数据集回归评估，对标 LangSmith / Langfuse。让运营者能「钻进一次运行看每个节点发生了什么」，并用测试数据集批量回归工作流质量。

## 1. 范围
**in**：
- **Trace 数据补全（S1/S4）**：复用现有 `ExecutionLog.Entries`（拥有实体），新增列 `TokensIn` / `TokensOut` / `NodeType`（`StepType?`）。打通 token 采集链路：`StepExecutionResult → StepCompleted/StepFailed 事件 → 事件处理器 → ExecutionLogEntry → EF 迁移 → DTO → 前端类型`。Output 复用既有 `Result`，Error 复用 `ErrorDetail`。**Input（节点入参）v1 不采集**（需编排器额外 plumbing，列为已知残留）。
- **Trace 视图前端**：扩展 `ExecutionLogDetailPage` 步骤表，增加 `节点类型 / TokensIn / TokensOut` 三列（后端 DTO 已含）；列表/明细/步骤端点已存在，仅扩响应模型字段，不改路由与鉴权。
- **评估（数据集回归，全新）**：新建 `EvaluationDataset` 聚合（ITenantScoped）+ 数据集管理 UI + 「对工作流跑评估」端点批量用数据集输入跑工作流、比对期望输出、出通过率/分数报告。多租户隔离（自动 query filter）+ 审计 `RunEvaluation` / `CreateEvaluationDataset` / `UpdateEvaluationDataset` / `DeleteEvaluationDataset`。
- 多租户隔离：`EvaluationDataset` 直接实现 `ITenantScoped`（自动获得全局过滤，避免重蹈 `ExecutionLog` 手动过滤覆辙）；`ExecutionLog` 保持现状（不破坏既有）。

**out（v1）**：在线标注/人工评分、LLM-as-judge 自动评分、后台任务式评估进度查询、节点级 Input 采集。

## 2. 接口契约（后端，最终版）

### 2.1 现有端点扩展（仅扩响应字段，不改路由/鉴权）
- `GET /api/v1/execution-logs/{id}` → `ExecutionLogDetailResponse`：其 `entries[]` 每项增 `tokensIn/tokensOut/nodeType`。
- `GET /api/v1/execution-logs/{id}/steps` → 分页 `entries[]` 同上增字段。
- 鉴权维持：`[Authorize]` + `[Authorize(Roles="Admin,Operator")]`（不变）。

### 2.2 新增评估端点（路由前缀 `api/v1/evaluation-datasets`）
| HTTP | 路由 | 鉴权 | 说明 |
|---|---|---|---|
| GET | `/evaluation-datasets` | `[Authorize]`（任意已认证可读） | 列表（tenant-scoped，支持 `keyword?`） |
| GET | `/evaluation-datasets/{id:guid}` | `[Authorize]` | 详情（含 `cases[]`） |
| POST | `/evaluation-datasets` | `[Authorize(Roles="Admin,Operator")]` | 新建（body: `name, description?, cases[]`） |
| PUT | `/evaluation-datasets/{id:guid}` | `[Authorize(Roles="Admin,Operator")]` | 改名/描述/替换 cases |
| DELETE | `/evaluation-datasets/{id:guid}` | `[Authorize(Roles="Admin,Operator")]` | 删除（tenant-scoped） |
| POST | `/evaluation-datasets/{id:guid}/run` | `[Authorize(Roles="Admin,Operator")]` | body `{ workflowId }` → `EvaluationReport` |

### 2.3 关键 DTO
- `EvaluationDatasetSummaryResponse { id, name, description?, caseCount, createdAt }`
- `EvaluationDatasetDetailResponse { id, name, description?, cases: EvaluationCaseResponse[], createdAt }`
- `EvaluationCaseResponse { id, input, expectedOutput, matchMode }`（`matchMode`: `0=Exact, 1=Contains`）
- `CreateEvaluationDatasetRequest { name, description?, cases: CreateEvaluationCaseRequest[] }`，`CreateEvaluationCaseRequest { input, expectedOutput, matchMode }`
- `RunEvaluationRequest { workflowId }`
- `EvaluationReport { total, passed, score, cases: EvaluationCaseResult[] }`，`EvaluationCaseResult { input, expectedOutput, actualOutput, passed, durationMs, tokensIn, tokensOut, errorDetail? }`

## 3. 数据模型与改动面
- **ExecutionLogEntry 扩展**（新增迁移 `ExtendExecutionLogEntry`）：加 `TokensIn int`、`TokensOut int`、`NodeType StepType?`（列 `integer` / `nullable integer`）。构造函数增参；`ExecutionLogConfiguration` 增列映射。
- **新增聚合 `EvaluationDataset`（ITenantScoped）**：`{ Id, Name, TenantId, Description?, CreatedAt, Cases: List<EvaluationCase> }`；`EvaluationCase` 拥有实体 `{ Id, Input, ExpectedOutput, MatchMode(EvaluationMatchMode) }`。EF 迁移 `AddEvaluation`：`EvaluationDatasets` 表 + OwnsMany `EvaluationCases`（含 `DatasetId` FK）+ `ValueGeneratedNever()` 主键。
- **枚举**：新增 `EvaluationMatchMode { Exact=0, Contains=1 }`（`Domain/Enums`）；`AuditActionType` 增 `RunEvaluation, CreateEvaluationDataset, UpdateEvaluationDataset, DeleteEvaluationDataset`。`TokenUsage` 值对象已存在（`Domain/ValueObjects/TokenUsage.cs`），复用。
- **RunEvaluationCommand/Handler**：对每个 case（上限 `EvaluationSettings.MaxCases`，默认 10，可配置）以 `case.Input` 作为 initial context 调 `IOrchestrationPrimitive.RunAsync(workflow, preset, ct)`（复用 `RunWorkflowCommandHandler` 路径），取末位 Completed 节点 `Result` 为 actualOutput，按 `MatchMode` 比对（Exact：`string.Equals`；Contains：`actual.Contains(expected, OrdinalIgnoreCase)`），聚合 `EvaluationReport`；审计 `RunEvaluation`。
- **前端**：`EvaluationDatasetsPage`（CRUD 表格 + 新建/编辑 Modal + 运行 Modal + 评估报告抽屉）+ 扩展 `ExecutionLogDetailPage` 步骤表列 + `api.ts`/`types/index.ts`/`locales`(中/en 对称)/`App.tsx` 路由/`AppLayout.tsx` 菜单。

## 4. 风险与缓解
- 🟡 Trace 数据完备性：已核验 `ExecutionLogEntry` 缺 token/节点类型 → 经 7 步链路贯通（§S4 实现面），token 在 `AgentCallStepExecutor` 已算，仅被丢弃，补回即可。
- 🟡 评估性能/超时（大数据集）：`RunEvaluation` 同步批量但**硬上限 MaxCases=10**（可配置）+ 复用编排器每步超时（`StateMachineSettings.StepTimeoutSeconds`）逐 case Bounding；后台任务式进度查询列为 v1 之后增强（设计文档原选项 S3 选「同步 + 上限保护」）。
- 🟡 与 F20 兼容：Trace 基于 `ExecutionLog`（通用，不依赖特定 `StepType`）；新增 `NodeType` 列对所有节点类型通用；Loop body 节点也发 `StepCompleted`（已确认 `RunLoopBodyAsync` 同样发布），token/节点类型贯通一致。
- 评估运行在测试环境依赖 integration fixture 的 stub model（与 F27/F28 BDD 一致），单元测用 mock `IOrchestrationPrimitive`。

## 5. 验收标准
- Trace 视图展示每个节点的 节点类型 / 耗时 / TokensIn / TokensOut / 输出 / 错误，与 execution 实际一致（token 数正确）。
- 数据集 CRUD 正常（tenant 隔离）；运行评估返回通过率（passed/total）与逐 case 结果（input/expected/actual/passed/durationMs/tokens）。
- 多租户：A 的 Trace/数据集不可被 B 见（`EvaluationDataset` 自动 filter；`ExecutionLog` 维持手动）。
- 审计落库（`RunEvaluation` 等）；前端 `tsc 0` + `qa.mjs` 全绿。

## 6. 决策（已锁定，2026-08-05）
- **S1 Trace 存储**：✅ 复用现有 `ExecutionLog.Entries`（拥有实体），**仅补列**（TokensIn/TokensOut/NodeType），不建独立 `WorkflowTrace` 表（避免与 ExecutionLog 重复存储）。
- **S2 评估比对**：✅ v1 用 `Exact` / `Contains` 双模式（case 级 `matchMode`），不做 LLM-judge（属 out 范围）。
- **S3 评估运行**：✅ **同步批量 + 上限保护**（MaxCases 默认 10，可配置），逐 case 复用编排器 step 超时 bounding；后台任务进度查询列为后续增强（降低 v1 风险）。
- **S4 Token 来源**：✅ 复用模型层已算的 `TokenUsage`（贯通 `StepExecutionResult → 事件 → 处理器 → Entry`）；不重新核算。**Input（节点入参）v1 不采集**，列为已知残留（需编排器额外 plumbing）。
