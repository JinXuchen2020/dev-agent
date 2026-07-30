# F24 · 执行 Trace / 评估视图

> 状态：`open`。来源：F7 工作流平台化 program 子项 **⑥**。本文档为 feature-builder 取数单元骨架；实现前须先锁定 §6 决策（尤其 Trace 数据是否需新存储 vs 复用现有 ExecutionLog.Entries）。

## 0. 目标
提供节点级可观测性（耗时 / token / IO）与数据集回归评估，对标 LangSmith / Langfuse。让运营者能「钻进一次运行看每个节点发生了什么」，并用测试数据集批量回归工作流质量。

## 1. 范围
**in**：
- **Trace 视图**：在 `ExecutionLogDetailPage` 增加节点级时间线（每个节点开始/结束/耗时/输入/输出/token/错误），钻取单次 execution。
- **Trace 数据**：确认 `ExecutionLog.Entries` 已含 `Duration`/IO；补足 token 字段（若缺）与节点级错误。
- **评估（数据集回归）**：新建 `EvaluationDataset`（聚合）+ 数据集管理 UI；「对工作流跑评估」端点批量用数据集输入跑工作流、比对期望输出、出通过率/分数报告。
- 多租户隔离（Trace 与数据集均 ITenantScoped）+ 审计（RunEvaluation）。

**out（v1）**：在线标注/人工评分、LLM-as-judge 自动评分（可后续，v1 用精确匹配/包含比对）。

## 2. 接口契约草案（后端）
- `GET /api/v1/executions/{id}/trace` → 节点级 Trace（复用 ExecutionLog + Entries，任意已认证可读）。
- `GET /api/v1/workflows/{id}/executions` → 某工作流的历史运行列表（分页，tenant-scoped）。
- `EvaluationDataset`：`POST/GET/PUT/DELETE /api/v1/evaluation-datasets`（Admin,Operator 写；list 可读）。
- `POST /api/v1/evaluation-datasets/{dsId}/run` body `{ workflowId }` → 批量跑，返回 `EvaluationReport(Total, Passed, Score, Cases[])`。

## 3. 数据模型与改动面
- `ExecutionLog.Entries` 补 `TokensIn/TokensOut/Error?`（若缺；可能需迁移或已在 F18 分析中存在，先核验）。
- **新增聚合** `EvaluationDataset`（ITenantScoped）：`{ Id, Name, TenantId, Cases: List<EvaluationCase>(Input, ExpectedOutput) }` + EF 迁移（`Id ValueGeneratedNever()`）。
- `RunEvaluationCommand/Handler`：对每 case 跑目标工作流（复用 orchestrator）、比对输出、聚合报告。
- 前端：`ExecutionLogDetailPage` 时间线组件 + `EvaluationDatasetsPage`（CRUD）+ 评估报告抽屉。

## 4. 风险
- 🟡 中风险：Trace 数据完备性（需确认 Entries 字段）、评估批量跑的性能/超时（大数据集）、与 F20 新节点类型的 Trace 兼容。
- 缓解：先核验 `ExecutionLog.Entries` 现有字段再决定迁移；评估用后台任务 + 分页跑，防长时阻塞；Trace 对所有 `StepType` 通用（基于 ExecutionLog 而非特定节点）。

## 5. 验收标准草案
- Trace 视图展示每个节点的耗时/IO/token/错误，与 execution 实际一致。
- 数据集 CRUD 正常；运行评估返回通过率与逐 case 结果；token 统计正确。
- 多租户：A 的 Trace/数据集不可被 B 见。
- 审计落库；前端 tsc 0 + qa.mjs 全绿。

## 6. 决策（待锁定）
- **S1** Trace 存储：复用现有 `ExecutionLog.Entries`（补字段）+ 迁移 vs 新增独立 `WorkflowTrace` 表。
- **S2** 评估比对：精确匹配 vs 包含 vs 未来 LLM-judge（v1 用包含/精确）。
- **S3** 评估运行：同步（小数据集）vs 后台任务（大数据集，需进度查询）。
- **S4** Token 统计来源：复用量化（若 ExecutionLog 已记）vs 重新核算。
