# 阶段十：语义记忆层（Semantic Memory）

> 学习目标：把"仅 RAG 检索"升级为"可写回的语义 / 情节记忆 + 自动 compaction"。本阶段补上真 Harness 的记忆引擎——不再只是文件注入式记忆。
> **关联**：`../docs/agent-harness-blueprint.md`（总体蓝图 §1.5 / §3 层3 / §4 Phase 10 / §5 D3 / §6 / §7）、`./phase-8-agent-runtime.md`（前置：agent 运行时实体，记忆须绑定 agent）、`../docs/quality/*`（质量门）。

## 学习目标

- [ ] **Embedding 生成与写入**：`IEmbeddingGenerator` 接入（OpenAI 兼容 / 本地），向量写入复用 `IVectorStore`（Pg 已支持）
- [ ] **Episodic 记忆写回**：agent 可把关键事实 / 经历写回向量库，跨会话召回
- [ ] **自动 Compaction**：把现有 `MaxSummaryTokens` 明文截断升级为摘要服务，长上下文自动压缩不丢关键事实
- [ ] **租户向量隔离**：记忆向量按租户分区，复用 `ITenantScoped` + `TenantProvider`，不得绕过
- [ ] **检索质量与成本治理**：embedding 成本、检索召回率、top-k 调参

## 前置依赖

- [ ] 阶段八 Agent 运行时实体化已完成（记忆须绑定到具体 agent / 租户上下文）
- [ ] 已锁定蓝图决策 **D3**：复用 `IVectorStore`（Pg 已支持）+ 选定 embedding 模型（OpenAI 兼容 / 本地）
- [ ] 已确认 `IVectorStore`（`Infrastructure`）+ `BuildWorkflowContext._vectorStore.SearchAsync`（:462）+ `MaxSummaryTokens`（:481）现状——当前只有检索、无生成、无写回、无 compaction

## 任务清单

### 现状核实（动手前必做，防历史漂移）

- [ ] 重核实记忆现状（§1.5）：确认仅 RAG 向量检索，**无 embedding 生成、无语义 / 情节记忆写回、无自动 compaction 服务**，截断靠明文 `MaxSummaryTokens`（:481）。
- [ ] 重核实 `IVectorStore` 接口能力与 Pg / InMemory 实现——确认写入端是否就绪，决定是否需补 upsert / 删除 API。

### 实现任务

- [ ] **Embedding 生成器**：`IEmbeddingGenerator` 抽象 + 实现（OpenAI 兼容 / 本地）；接入 `IVectorStore` 写入路径。🔍 强制 `ddd-phase-quality-gate`：核对 DI 作用域 / 密封 / 空守卫 / 接口非空壳 / 配置可切换模型。
- [ ] **Episodic 记忆写回**：agent 经总线 / 运行时把关键事实写回 `MemoryEntry` 聚合（`ITenantScoped`，含 AgentId / TenantId / Embedding / Payload / Timestamp）；提供召回 API。🔍 强制 `ddd-code-reviewer`：核对记忆**真实写入并可跨会话召回**（非仅接口定义）、检索走 embedding 相似度、非伪造召回。
- [ ] **自动 Compaction 服务**：把 `MaxSummaryTokens` 明文截断升级为摘要服务——长上下文 / 历史对话达阈值时调用 LLM 生成结构化摘要并回写，保关键事实。🔍 强制 `ddd-code-reviewer`：核对压缩真实发生、关键事实不丢、摘要调用真实接入（非占位）。
- [ ] **租户向量隔离**：记忆写入 / 检索均按 `TenantProvider` 当前租户过滤，复用 `ITenantScoped` 全局 query filter；确认跨租户不可越权读他租户记忆。🔍 强制 `ddd-code-reviewer`：核对跨租户记忆隔离生效、Global Query Filter 真实拦截。
- [ ] **成本 / 质量治理**：embedding 调用批量 + 缓存；top-k / 相似度阈值可调；接入阶段五 `AuditLog` 记录 token 消耗。🔍 强制 `ddd-phase-quality-gate`：核对配置项齐全、审计落库。

## 验收标准

1. agent 可在一次会话写回关键事实，并在**后续会话**经语义检索召回（跨会话情节记忆）。
2. 长上下文超过阈值时自动 compaction，压缩后关键事实不丢失（对照压缩前后事实清单校验）。
3. 租户 A 的记忆不能被租户 B 检索到（Global Query Filter 拦截）。
4. embedding 成本可观测（token 计入 `AuditLog`），top-k / 阈值可配置。
5. 检索召回率达标（构造若干 query，相关记忆命中率 ≥ 既定基线）。

▶ **设计评审关（动手前强制）**：进入本 Phase 前须已过 `blueprint-architecture-review`（见 phase-1 §0-1）。EmbeddingGenerator / 语义记忆存储 / Compaction 服务属"叙事性能力"，合入前强制 `ddd-code-reviewer`。

## 0. Quality Skill Routing Policy（质量 Skill 路由策略）

本平台有两个互补 skill，职责不同、不可互相替代：

| 模块类型 | 强制 Skill | 目的 |
|----------|-----------|------|
| 实现"叙事性能力"的模块（IEmbeddingGenerator / 语义记忆存储 / Compaction 服务——**类名即承诺某种能力**） | **`ddd-code-reviewer`**（对抗式审查） | 验证实现行为是否忠于蓝图 §1.5/§4 Phase 10、依赖是否真实使用、记忆是否真实写入召回、压缩是否真实发生 |
| 纯基础设施 / 结构卫生模块（MemoryEntry 仓储 / DI / EF 映射 / IVectorStore 写入 / 配置） | `ddd-phase-quality-gate`（静态结构门禁） | DI / DDD 层 / EF / 并发 / 密封 / 守卫等结构卫生 |

**硬性规则（WHY）**：`ddd-phase-quality-gate` 的 "Blueprint Drift" 仅查"蓝图声明要做、但被标记未来的功能"，**不查"实现行为 vs 蓝图叙事"的深度一致性**。凡是"类名 / 接口名承诺了某种能力"的模块，都是"名不副实现"的高风险区，必须由 `ddd-code-reviewer` 把关。

**`ddd-code-reviewer` 报告必须包含**：对所审模块，显式写出"已核对的蓝图章节 / 验收标准"（例如 "verified against 蓝图 §1.5 / §4 Phase 10 / 阶段十验收标准"）。缺此项即视为未通过。

### Phase 10 强制范围（高风险叙事性模块）

- **EmbeddingGenerator / 语义记忆写回**：核对 §1.5 / §4 Phase 10；重点验证记忆真实写入并可跨会话召回、检索走 embedding 相似度、非伪造。
- **自动 Compaction 服务 / 租户隔离**：核对 §3 层3 / §5 D3 / §6；重点验证压缩真实发生且关键事实不丢、跨租户记忆隔离生效。

> 规划提示：阶段十补上记忆引擎，本 §0 要求在此阶段启动前即明确——上述模块合入前**必须**走 `ddd-code-reviewer`。

## 学习笔记

### 第一天（YYYY-MM-DD）

```

```

### 第二天（YYYY-MM-DD）

```

```

## 进度

- **开始日期**：
- **完成日期**：
- **完成度**：█░░░░░░░░░ 0%

## 回顾（完成后填写）

### 做得好的

### 下次改进

### 对蓝图文档的反馈
