# F36 · Agent 上下文隔离（Blackboard 分区 + 独立对话历史）质量门报告

> 日期：2026-09-01 · 分支 `feat/f36-agent-context-isolation`（基于 `feat/f35-workspace-isolation`）· feature-builder 全栈流水线
> 设计文档：`features/f36-agent-context-isolation.md`（§5 决策 D1–D4 用户锁定 2026-08-31；§8 审查修复记录）

## 结论

| 质量门 | 状态 | 摘要 |
|---|---|---|
| ddd-code-reviewer | **PASS**（0 open） | 1×P1 + 3×P2 修复 |
| ddd-phase-quality-gate | **PASS**（P0=0 P1=0 P2=0；P3 2 waiver） | checklist 嵌入设计文档 |
| codebase-optimizer | **PASS**（Round F36-01，0 open） | 1×P1 + 3×P3 修复，沿用结构门 2 waiver |

## ddd-code-reviewer 修复记录

| 严重度 | 文件 | 问题 | 修复 |
|---|---|---|---|
| P1 | ConversationConfiguration / 迁移 | 并发同 (tenant,workflow,agent) 双步骤同时判空建会话 → 双行、历史分裂 | 唯一过滤索引 `IX_Conversations_TenantId_WorkflowId_AgentId`（`"AgentId" IS NOT NULL`，SQLite/PG 双栈合法）；冲突由 best-effort 吞掉；新增 EF 测试锁定 |
| P2 | AgentCallStepExecutorTests | best-effort 不吞 OCE 无用例锁定 | 新增 `AgentStep_ConversationPersistenceCancellation_IsNotSwallowed` |
| P2 | api.ts / ConversationsPage.tsx | getAgents 未接 AbortSignal，筛选切换不取消 | 补 signal 参数并传播 |
| P2 | ConversationsPage.tsx | 新建会话兜底刷新丢失当前筛选条件 | 兜底请求携带 status/q/agentId |

## codebase-optimizer（Round F36-01）关键修复

- **P1**：best-effort 会话持久化在创建路径 SaveChanges 失败时，Added 实体滞留共享 change tracker，编排器下一步 SaveChangesAsync 重放唯一索引冲突 → 「吞掉仅告警」被放大成工作流状态保存失败。修复：`IConversationRepository.Detach` + 失败路径先 Detach 再抛（OCE 仍穿透），测试锁定。
- P3：IntegrationConstants 注释错挂归位；executor 相邻 `if (agent is not null)` 合并；冗余单列索引 `IX_Conversations_AgentId` 移除（复合唯一过滤索引已覆盖）。

## 结构门（12 类 audit）要点

DI 无新接口（executor 新增两依赖均为既有 Scoped 注册）；EF 迁移 Up/Down/Snapshot/Configuration 一致，`"AgentId" IS NOT NULL` 过滤索引在 SQLite 与 PostgreSQL 双栈合法；分层/XML/CancellationToken/internal sealed 全合规；`GetFromPartition`/`SetInPartition` 无生产调用方 = D1 预留 API（agent 工具链接入时用，6 测试锁定，waiver 至 F37+）；截断字面量 8000/12000 waiver（< Message 16000 上限，注释说明）。前端：useEffect 依赖完整、AbortController 全链、i18n 对称。

## 验证

- 后端：`dotnet build AgentPlatform.sln` 0 警告 0 错误；Application **253/253**、Infrastructure **162 + 6 跳过**、Api **35/35**、Architecture **9/9**、Integration **5/5**（需 `OPENAI__Key`）、SpecFlow **115/116**（唯一失败 = master 既有 LLM 用例「Admin 创建会话后向其发送消息得到回复」，已验证 master 同样失败，豁免）。
- 新增测试：Blackboard 分区 7 例、AgentCallStepExecutor F36 行为 7 例（分区注入隔离/全局视图/回写键/创建/复用/持久化失败不阻断/OCE 穿透）、ConversationAgentIsolation EF 4 例、SpecFlow agentId 过滤契约场景 1 个（确定性，无 LLM）。
- 前端：`tsc --noEmit` 0 error；vitest 42/43（既有豁免）；`vite build` 通过。
- 模型一致性：后端 camelCase 序列化的 `Conversation.agentId`（域实体直出）与前端 `types/index.ts` `Conversation.agentId?: string | null` 对齐；`GET /conversations?agentId=` 参数命名一致；tsc 通过。

## 已知残留（非阻断）

1. 硬分区（`Dictionary<Guid,…>` 重构 + 三个持久化格式 SchemaVersion 升级）列 v2——软分区已满足 agent 隔离语义且零迁移成本。
2. `SetInPartition`/`GetFromPartition` v1 无生产调用方（D4 规定产物走全局键；分区写入随 agent 工具链落地接入）。
3. 截断字面量 8000/12000 未抽 IOptions（与既有 Truncate(200/500) 风格一致）。
