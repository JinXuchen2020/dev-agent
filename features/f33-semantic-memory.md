# F33 · 语义记忆层 设计文档

> **关联**：`phases/phase-10-semantic-memory.md`、`docs/agent-harness-blueprint.md` §Phase 10、`features/backlog.md` F33
> **状态**：`doing`（2026-08-25，分支 `feat/f33-semantic-memory`，基于 f32）
> **优先级**：P1

---

## 1. 现状核实

| # | 事实 |
|---|------|
| ① | `IVectorStore` 已具备完整能力：Ingest/Search/Delete，全部带 tenantId 参数（PgVectorStore + InMemoryVectorStore 双实现、工厂按部署选择）——D3 复用基础成立 |
| ② | 记忆现状为「构建即丢」：BuildWorkflowContext 组装 Summary（超 MaxSummaryTokens 即 break 截断丢弃）/ Retrieval（按节点名向量检索），但 **AgentCallStepExecutor/CriticStepExecutor 的 prompt 均未消费这两个通道**——上下文白建 |
| ③ | 无任何跨运行经验沉淀机制 |

## 2. 目标（v1）

① Embedding 写入管线（复用 IVectorStore）；② 工作流完结 episodic 写回（成功经验与失败教训都沉淀）；③ 自动 compaction——超出 token 预算的旧步骤不再「硬截断丢弃」，改为从语义记忆召回注入；并打通 Summary/Retrieval 到执行器 prompt 的最后一公里。

## 3. 核心设计

### 3.1 服务
```
ISemanticMemoryService (Application 抽象, SCOPED)
├─ RememberRunAsync(tenantId, workflowId, workflowName, outcome, digest)   // episodic 写回
└─ RecallAsync(tenantId, query, topK, minScore, ct) → VectorSearchResult[] // 语义召回
SemanticMemoryService (Infrastructure): 包裹 IVectorStore；
  集合 = RoutingConstants.SemanticMemoryCollection("semantic-memory")
  内容模板 = "[episodic:{outcome}] workflow={name}({id})\n{digest}"
  metadata = { kind:"run", workflowId, outcome }
```

### 3.2 Episodic 写回
`SemanticMemoryWriteBackHandler` 订阅 WorkflowCompleted / WorkflowRolledBack：以「工作流名 + 各步骤产出摘要（截断聚合）+ 结局」为内容写回。失败教训同样沉淀。

### 3.3 自动 Compaction（替代硬截断）
SequentialOrchestrator.BuildWorkflowContext 改造：
- 近端步骤照旧逐条入 Summary 直到预算
- **溢出步骤不再静默丢弃**：若语义记忆服务可用，按当前节点名召回 Top-K 历史经验，以负数键（-1,-2…）写入 Summary.Summaries，标注「[semantic-recall]」
- 服务缺席/未启用 → 行为退回现状（全部既有测试零感知）
- 并在 prompt 中真正消费：AgentCallStepExecutor.BuildPrompt 新增 Summary 区块与 Retrieval 区块渲染（修复②的「建而不用」漂移）

### 3.4 配置
```json
"SemanticMemory": { "Enabled": true, "RecallTopK": 3, "RecallMinScore": 0.6 }
```

## 4. 决策记录
| 编号 | 决策 | 依据 |
|------|------|------|
| D3 | 向量后端复用 IVectorStore | 双实现+租户隔离+工厂齐备；引入专用库零收益高成本 |
| D2' | 写回触发点 | WorkflowCompleted/RolledBack 领域事件 handler（成功经验与失败教训均沉淀）|
| D4' | Compaction 实现 | 溢出内容→语义召回注入，非 LLM 二次压缩（零额外模型成本，v1 务实）|
| D5' | prompt 打通 | AgentCall 渲染 Summary/Retrieval——修复「建而不用」，否则记忆层无消费出口 |

## 5. 测试计划
- SemanticMemoryServiceTests：Remember→Ingest 参数（集合/租户/内容标记/metadata）；Recall→Search 透传 topK/minScore
- WriteBack handler：Completed/RolledBack 事件 → Ingest 收到含结局与步骤摘要的内容
- ExecutorPromptTests：ctx.Summary 含 [semantic-recall] 键 + Retrieval.Chunks → prompt 出现对应区块
- 全量回归四套件

## 6. 完成记录（2026-08-25）

**分支**：`feat/f33-semantic-memory`（基于 f32）

**交付物：**
- **① Embedding 管线**：`ISemanticMemoryService`（SCOPED）+ `SemanticMemoryService`——复用 IVectorStore，集合 `semantic-memory`；内容寻址 docId（SHA256 of wf+outcome+digest）同内容去重；metadata {kind:run, workflowId, outcome}
- **② Episodic 写回**：`SemanticMemoryWriteBackHandler` 订阅 WorkflowCompleted / WorkflowRolledBack——成功经验与失败教训（含 errorDetail）均沉淀为 `[episodic:{outcome}] workflow=…\n{步骤摘要}`；Enabled=false 静默跳过；写回异常仅告警不影响主流程
- **③ 自动 Compaction**：SequentialOrchestrator.BuildWorkflowContext 溢出步骤不再 `break` 硬丢弃 → 按 currentStep 名语义召回 Top-K 经验，以负数键 `[semantic-recall]` 注入 Summary（服务缺席/禁用时退回现状，全部既有测试零感知）
- **Prompt 打通（修复「建而不用」漂移）**：AgentCallStepExecutor.BuildPrompt 新增 History summary 与 Relevant knowledge 区块——Summary（含召回条目）与 Retrieval.Chunks 第一次真正进入 LLM prompt

**附带发现并修复**：BuildWorkflowContext 的 Summary/Retrieval 此前从未被任何执行器消费（上下文白建的隐性漂移），本 feature 一并打通。

**测试**：新增 7 例（服务 3：写穿参数/确定性 id 去重/召回透传；写回 handler 3：completed/rolled_back/disabled；prompt 渲染 1：semantic-recall+retrieval 断言）。全绿 App221 / Infra154+6skip / Api35 / Arch9；build 0/0；前端零改动。

**质量门**：三道门 PASS，`.quality-gate.json` 推进 `f33-semantic-memory`，`cleared:true`

**已知残留：**
- Compaction 仅接入 Sequential 路径（Negotiation 协作循环的 BuildWorkflowContext 未接，留后续）
- 记忆无 TTL/容量上限治理（依赖向量库侧策略，Phase 11 评估）
