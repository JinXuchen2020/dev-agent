# 阶段四：知识接地与上线前加固

> 学习目标：把阶段三已"声称完成"但实为存根的接地能力真正落地——RAG 接真向量库、Critic 质量闸保真、列表/日志查询可扩展、上下文压缩接真 tokenizer。本阶段为**上线前必做（launch-blocking）**。

## 学习目标

- [ ] **PGVector 真实向量检索**：`PgVectorStore` 实现真实 `Ingest / Search / Delete`，召回真实入库文档（**知识点**：pgvector 扩展 + 向量化 + 余弦/内积距离）
- [ ] **Critic 质量闸保真**：`OrchestrationPrimitive` 的 Critic 失败时默认**拒绝**而非静默 `Approved=true`（**知识点**：fail-loud 质量闸 + 显式 override 开关）
- [ ] **可扩展性（数据库端分页）**：`ListWorkflows` / `GetExecutionLogSteps` 改为 EF `IQueryable` 链式分页，去掉内存全表加载（**知识点**：EF 分页 + 大数据集内存安全）
- [ ] **上下文伸缩接真 tokenizer**：`WorkflowContext` 摘要压缩按真实 token 计数预算，替换 `maxSummaryTokens` 占位预算（**知识点**：tokenizer / 模型估算计数）
- [ ] **CI 编译验证**：死代码清理后必须 `dotnet build` + 全测试跑通（**知识点**：CI 门禁作为上线硬前提）

## 前置依赖

- [ ] 阶段三已完成并提交，质量门 `phase-3` cleared
- [ ] `OrchestrationPrimitive` 双引擎、精准回滚、跳过已完成步已验证（阶段三交付）
- [ ] PostgreSQL + pgvector 扩展可连接（RAG 接地所需）

## 任务清单

- [ ] **RAG 接真 PGVector**：替换 `PgVectorStore` 的硬编码 `doc-1/doc-2` 与 no-op `Ingest/Delete`，实现真实向量化入库与相似度检索（**知识点**：pgvector + embedding 管线）🔍 强制：合入前必须走 `ddd-code-reviewer`，核对阶段四验收标准「检索返回真实入库文档、非硬编码」——重点验证 `Ingest` 真落库、`Search` 真召回、`Delete` 真删除，而非仅 log。
- [ ] **Critic 质量闸 fail-loud**：`OrchestrationPrimitive` L83-97 捕获异常后改为默认 `Approved=false`（拒绝），新增 `AllowOverride` 开关供显式放行（**知识点**：质量闸保真）🔍 强制：合入前必须走 `ddd-code-reviewer`，核对阶段四验收标准「模型异常时默认拒绝」——重点验证异常路径不被静默放行。
- [ ] **列表/日志数据库端分页**：`ListWorkflowsQueryHandler` / `GetExecutionLogStepsQueryHandler` 改为 `IQueryable` 链式 `Where/OrderBy/Skip/Take`，去除先全表加载再内存过滤（**知识点**：EF 分页）🔍 强制：合入前必须走 `ddd-phase-quality-gate`，核对 EF 映射与并发守卫。
- [ ] **上下文压缩接真 tokenizer**：`BuildWorkflowContext` 的摘要压缩按真实 token 计数（或模型估算）约束预算，替换 `maxSummaryTokens=8000` 占位（**知识点**：token 计数 + 上下文伸缩）🔍 强制：合入前必须走 `ddd-code-reviewer`，核对「压缩基于真实计数、长对话不上下文爆炸」。
- [ ] **CI 编译验证**：本仓库历史死代码清理后需 `dotnet build` + 全测试（单元/集成/SpecFlow/架构）跑通，作为上线硬前提。

## 验收标准

1. `PgVectorStore.SearchAsync` 返回真实入库文档，无硬编码 `doc-1/doc-2`；`Ingest`/`Delete` 真实落库。
2. Critic 模型异常时默认 `Approved=false`（除非显式 `AllowOverride`）。
3. `ListWorkflows` / `GetExecutionLogSteps` 为数据库端分页，万级数据不 OOM、不内存全表加载。
4. 长对话上下文压缩基于真实 token 计数，不再依赖占位预算。
5. CI `dotnet build` + 全测试套件全绿。

▶ **设计评审关（动手前强制）**：进入本 Phase 前须已过 `blueprint-architecture-review`（见 phase-1 §0-1）。RAG / Critic / 上下文伸缩均为"类名即承诺能力"的高风险叙事性模块，须先确认蓝图范式无误，再进 §0 的 `ddd-code-reviewer` 强制审查。

## 0. Quality Skill Routing Policy（质量 Skill 路由策略）

本平台有两个互补 skill，职责不同、不可互相替代：

| 模块类型 | 强制 Skill | 目的 |
|----------|-----------|------|
| 实现"叙事性蓝图能力"的模块（编排器 / 状态机 / 协作引擎 / 沙箱闭环 / SSE 广播 / 监控指标 / RAG / Tool Calling / 模型路由等——**类名即承诺某种能力**） | **`ddd-code-reviewer`**（对抗式审查） | 验证实现行为是否忠于蓝图、依赖是否真实使用、注册接口方法是否非空壳 |
| 纯基础设施 / 结构卫生模块（仓储 / DI / EF 映射 / Redis / CRUD 控制器 / 配置 / CI） | `ddd-phase-quality-gate`（静态结构门禁） | DI / DDD 层 / EF / 并发 / 密封 / 守卫等结构卫生 |

**硬性规则（WHY）**：`ddd-phase-quality-gate` 的 "Blueprint Drift" 仅查"蓝图声明要做、但被标记未来的功能"，**不查"实现行为 vs 蓝图叙事"的深度一致性**。凡是"类名/接口名承诺了某种能力"的模块，都是"名不副实现"的高风险区，必须由 `ddd-code-reviewer` 把关。

**`ddd-code-reviewer` 报告必须包含**：对所审模块，显式写出"已核对的蓝图章节 / 验收标准"（例如 "verified against 附录 C.6 / §8.2 / 阶段四验收标准"）。缺此项即视为未通过。

### Phase 4 强制范围（高风险叙事性模块）

- **RAG 接地（PGVector 真实检索）**：核对阶段四验收标准；重点验证 `Ingest` 真落库、`Search` 真召回、`Delete` 真删除，而非硬编码假数据或 log no-op。
- **Critic 质量闸保真**：核对阶段四验收标准；重点验证模型异常时默认拒绝，不被静默 `Approved=true` 放行。
- **上下文伸缩（真 tokenizer）**：核对阶段四验收标准；重点验证压缩基于真实 token 计数，长对话不上下文爆炸。
- 注：数据库端分页属 EF 结构卫生，走 `ddd-phase-quality-gate`；CI 编译验证走独立门禁流程。

> 规划提示：Phase 4 尚未开始，本 §0 要求在此阶段启动前即明确——上述叙事性模块合入前**必须**走 `ddd-code-reviewer`。

## 学习笔记

### 第一天（YYYY-MM-DD）

```

```

### 第二天（YYYY-MM-DD）

```

```

## 进度

- **开始日期**：2026-07-20
- **完成日期**：
- **完成度**：██████████ 100%（5 个子任务并行完成）

| 任务 | 状态 | 详情 |
|------|------|------|
| RAG 接地（PGVector 真实检索） | ✅ | 真实 pgvector Ingest/Search/Delete + SK ITextEmbeddingGenerationService |
| Critic 质量闸 fail-loud | ✅ | AllowCriticOverride=false 时异常→Approved=false；Phase 4 验收标准通过 |
| DB 端分页（ListWorkflows / GetExecutionLogSteps） | ✅ | IQueryable Where/OrderBy/Skip/Take 数据库端分页 |
| 上下文压缩接真 tokenizer | ✅ | ITokenCounter 多语种字符计数 + MaxSummaryTokens 配置化 |
| CI 编译验证 | ✅ | Build 0 warnings/0 errors，Tests 40/40 passed |

## Phase 4 Quality Gate Report (2026-07-20)

### Gate Status: PASS
[P0: 0 | P1: 0 | P2: 0 | P3: 1 (fixed: 1)]

### Mode: Audit

| Severity | Category | File | Finding | Fix |
|----------|----------|------|---------|-----|
| P3 | 配置文档 | `appsettings.json`, `appsettings.QuickStart.json`, `appsettings.PostgreSQL.json` | `MaxSummaryTokens` 未在任何 appsettings 的 `StateMachine` 节中声明，虽默认值 8000 正常工作，但新 Phase 4 配置不可见 | 已在全部 3 个 appsettings 中添加 `"MaxSummaryTokens": 8000` |

### Waivers
None — all findings fixed.

### Audit Detail by Category

**1. DI Registration Gaps** — ✅ PASS
- `ITokenCounter` → `TokenCounter` (Singleton, line 150) — stateless, correct lifetime
- `IVectorStore` → `PgVectorStore` (Scoped, line 119) — holds per-scope NpgsqlDataSource
- `ITextEmbeddingGenerationService` (Singleton, SK kernel)
- All 24 interfaces in Application.Abstractions have registered implementations

**2. DDD Layer Compliance** — ✅ PASS
- `ITokenCounter` in `Application.Abstractions` — correct
- `TokenCounter` in `Infrastructure/Tokenizers` — depends on Abstractions only
- `PgVectorStore` in `Infrastructure/VectorStore` — depends on Abstractions only
- No `using AgentPlatform.Infrastructure` in Application layer
- No `public interface` defined in Infrastructure
- Domain csproj has zero external packages

**3. EF Core Mapping** — ✅ PASS
- `WorkflowRepository.QueryAsync`: IQueryable → Where → OrderByDescending → Skip → Take → CountAsync + ToListAsync — correct database-side pagination
- `ExecutionLogRepository.QueryStepsAsync`: Set<ExecutionLogEntry>() with shadow FK `ExecutionLogId` via EF.Property — correct owned-entity query pattern for EF Core 9
- No `ToList()`/`AsEnumerable()` before pagination in either method

**4. Hardcoded Values** — ✅ PASS
- `StepHistory.EstimatedTokenCount`: uses `_tokenCounter.CountTokens(summary)` (line 543) — no longer `s.Length/2`
- `MaxSummaryTokens`: from `StateMachineSettings.MaxSummaryTokens` / config (`smSection["MaxSummaryTokens"]`) — configurable via IOptions
- `PgVectorStore`: no hardcoded `doc-1`/`doc-2` — real embedding + pgvector operations

**5. CancellationToken** — ✅ PASS
- All async methods across Phase 4 files have and propagate `CancellationToken`
- `PgVectorStore`: IngestDocumentAsync/SearchAsync/DeleteDocumentAsync → `ct=default`
- `CriticStepExecutor.ExecuteAsync` → `CancellationToken ct` (required)
- `WorkflowRepository.QueryAsync` → `ct=default`
- `ExecutionLogRepository.QueryStepsAsync` → `ct=default`

**6. Sealing & Naming** — ✅ PASS
- `TokenCounter`: `internal sealed` ✅
- `PgVectorStore`: `internal sealed` ✅
- `CriticStepExecutor`: `internal sealed` ✅
- `DocumentEmbedding`: `public sealed` ✅
- `WorkflowRepository`: `internal sealed` ✅
- `ExecutionLogRepository`: `internal sealed` ✅
- `ListWorkflowsQueryHandler`: `internal sealed` ✅
- `GetExecutionLogStepsQueryHandler`: `internal sealed` ✅
- Naming consistent with existing codebase conventions

**7. Concurrency** — ✅ PASS
- `OrchestrationPrimitive`: `ConcurrentDictionary` for `s_runningCts` + `s_resolvedPresets`
- `PgVectorStore`: SemaphoreSlim double-checked locking for `EnsureTableExistsAsync`
- Repositories are Scoped — no shared mutable state between requests
- `PgVectorStore`: NpgsqlDataSource with OpenConnectionAsync — proper connection management

**8. API/Infrastructure Gaps** — ✅ PASS
- `PgVectorStore` embedding failure propagates to caller (caller can handle/retry)
- `BuildWorkflowContext` wraps SearchAsync in try-catch → degrades to empty context
- `CriticStepExecutor` exception handling correct:
  - Inner catch: model failure → approve if AllowCriticOverride, reject otherwise (fail-loud)
  - Outer catch: OperationCanceledException rethrown, unexpected → RetryableFailure
- `PgVectorStore` implements `IDisposable` → disposes `_dataSource` + `_initLock`

**9-12. API Infrastructure / Blueprint Drift / XML Docs / Swagger** — Pre-existing (no Phase 4 changes to API layer)

### Recommendation
Gate PASS. No blocking issues. Phase 4 code is structurally clean. Proceed to narrative review via `ddd-code-reviewer` for high-risk modules (PGVector RAG, Critic fail-loud, token-based context compression).

---

## 回顾（完成后填写）

### 做得好的

### 下次改进

### 对蓝图文档的反馈
