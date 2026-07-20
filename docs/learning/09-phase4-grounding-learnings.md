# 09. Phase 4 学习笔记：把"声称完成"的能力真实落地

> 目标：Phase 4 不是新功能，而是给"蓝图已宣称完成、但实为存根"的能力补课。本笔记讲清**五个核心知识点**、它们解决的问题、代码落点，以及背后的工程原则。

> **一句话**：Phase 4 把蓝图声称但实为存根的 RAG / Critic / 分页 / tokenizer 真实落地，核心是 fail-loud、数据库端分页、真实计数。
> 配套阶段文档：`phases/phase-4-grounding.md`（含验收标准与 Quality Gate Report）。

---

## 8.1 为什么 Phase 4 是"加固"而不是"新功能"

Phase 1→3 的演进里，很多"听起来很厉害"的能力其实是**存根（Stub）**——接口定义好了、调用链跑通了，但实现是假的（返回硬编码、只 log、no-op）。RAG、Critic、分页、tokenizer 都属于这一类。

| 能力 | Phase 3 结束时声称 | 实际 | Phase 4 要做 |
|------|-------------------|------|-------------|
| RAG 知识接地 | "已接入上下文" | `PgVectorStore` 返回硬编码 `doc-1/doc-2` | 接真 pgvector |
| Critic 质量闸 | "审查通过才放行" | 异常时静默 `Approved=true` | 异常默认拒绝 |
| 列表/日志查询 | "支持分页" | 先全表加载再内存过滤 | 数据库端分页 |
| 上下文压缩 | "按 token 预算压缩" | `maxSummaryTokens` 只是占位预算 | 接真实 tokenizer |

**关键认知（来自 AutoGen 教训）**：这是"实现漂移"的 B 类——**类名承诺了能力，实现却没兑现**。这类问题 CI 查不出来（编译通过），只有人读代码或运行时才会暴露。所以 Phase 4 被定为 **launch-blocking（上线前必做）**：带着假能力上线，等于主动喂错信息。

---

## 8.2 Phase 4 知识地图（五大知识点）

```
Phase 4 加固 = 给"名不副实"的承诺补课
┌───────────────────────────────────────────────────────────────┐
│  ① RAG 接真 PGVector        向量检索管线（向量化→存储→检索）      │
│  ② Critic 质量闸 fail-loud  质量闸保真（异常默认拒绝）           │
│  ③ EF 数据库端分页          大数据集内存安全                     │
│  ④ 上下文压缩接真 tokenizer  token ≠ 字符，预算要真实计数         │
│  ⑤ CI 编译验证门禁          别人替你编译的保险                   │
└───────────────────────────────────────────────────────────────┘
        ↑ 全部由 ddd-code-reviewer（①②③④ 叙事性模块）
          或 ddd-phase-quality-gate（③ 结构卫生）把关
```

---

## 8.3 五大知识点详解

### 知识点 1 · RAG 接真 PGVector（向量检索管线）

**问题**：`PgVectorStore.SearchAsync` 返回硬编码 `doc-1/doc-2`，`Ingest/Delete` 只是 log——所以 Agent 检索到的"知识"全是假的。

**解决方案**：向量检索三件套
1. **向量化**：用 `ITextEmbeddingGenerationService`（Semantic Kernel 封装的 OpenAI `text-embedding-3-small`）把文本转成向量。
2. **存储**：PostgreSQL + `pgvector` 扩展，`INSERT` 向量列（`Pgvector.Vector` 类型）。
3. **检索**：`<=> ` 余弦距离运算符做相似度排序，取 Top-K。

**代码落点**：`src/AgentPlatform.Infrastructure/VectorStore/PgVectorStore.cs`（约 248 行，`internal sealed`，`IDisposable` 释放 `NpgsqlDataSource`）。DI 注册 `IVectorStore → PgVectorStore`（Scoped，持有 per-scope 的 `NpgsqlDataSource`）。

**学到的工程点**：
- **best-effort 降级**：`OrchestrationPrimitive.BuildWorkflowContext` 把 `SearchAsync` 包在 try-catch 里，检索失败时降级为"空上下文"，不让向量库故障拖垮主流程。
- **表初始化双检锁**：`EnsureTableExistsAsync` 用 `SemaphoreSlim` + 双重检查，避免并发重复建表。
- **资源释放**：`NpgsqlDataSource` 用 `OpenConnectionAsync` 管理连接，类实现 `IDisposable`。
- **真实集成测试**：配了真实 PostgreSQL 容器 fixture，不是 mock。

**踩坑点**：Npgsql 的向量类型要装 `Pgvector` 包；`pgvector` 扩展要在库里 `CREATE EXTENSION` 一次；embedding 失败要向上抛（让调用方决定重试还是降级），不要吞掉。

---

### 知识点 2 · Critic 质量闸 fail-loud

**问题**：原 `OrchestrationPrimitive` 在 Critic 异常时 `catch { Approved = true; }`——模型挂了反而"审查通过"，质量闸形同虚设。

**解决方案**：**fail-loud 优先**。新增 `AllowCriticOverride` 开关，默认 `false`：
- 模型调用失败 / JSON 解析失败 → `Approved = false`（**拒绝**）；
- 只有显式 `AllowCriticOverride = true` 时才放行走查。

**代码落点**：`src/AgentPlatform.Infrastructure/Workflows/CriticStepExecutor.cs`，双层 catch 结构：
- **内层 catch**（业务异常）：模型失败 → 按 `AllowCriticOverride` 决定 approve/reject；
- **外层 catch**：`OperationCanceledException` 原样重抛（尊重取消），其余意外异常 → 标记为 `RetryableFailure`。

**学到的工程原则**（最重要的一条）：
> **fail-loud 优于 fail-silent**。质量/安全相关的闸，故障态必须"拒绝"或"报错"，绝不能"默默放行"。把"放行"变成需要显式开关的有意识决策，而不是默认行为。

---

### 知识点 3 · EF 数据库端分页

**问题**：`ListWorkflowsQueryHandler` / `GetExecutionLogStepsQueryHandler` 先**全表加载到内存**再 `Where/Skip/Take`——数据量一涨就 OOM / 变慢。

**解决方案**：始终在 `IQueryable` 上链式分页，让数据库做过滤与切片：

```csharp
// ✅ 正确：数据库端分页
IQueryable<Workflow> q = db.Workflows.Where(predicate)
                                     .OrderByDescending(w => w.CreatedAt);
var total = await q.CountAsync(ct);
var items = await q.Skip((page-1)*size).Take(size).ToListAsync(ct);
```

**代码落点**：`src/AgentPlatform.Infrastructure/Persistence/Repositories/WorkflowRepository.cs`（`QueryAsync`，`IQueryable → Where → OrderByDescending → Skip → Take → CountAsync + ToListAsync`）；`ExecutionLogRepository.QueryStepsAsync` 用 shadow FK（`EF.Property<Guid>(e, "ExecutionLogId")`）走 owned-entity 查询。

**学到的工程点**：
- **分页前绝不要 `ToList()` / `AsEnumerable()`**——那会触发全表物化。
- **`CountAsync` 和分页用同一个 `IQueryable`**，避免两次查询逻辑不一致。
- owned-entity 的查询要用 `EF.Property` 访问 shadow 外键（EF Core 9 的惯用法）。

---

### 知识点 4 · 上下文压缩接真 tokenizer

**问题**：`BuildWorkflowContext` 的摘要压缩用 `maxSummaryTokens = 8000` 当预算，但这个数字没有真实计数支撑——压缩可能失效，长对话会上下文爆炸。

**解决方案**：引入 `ITokenCounter`，用真实计数约束预算：
- `TokenCounter.CountTokens(summary)` 做**多语种字符级**估算（`OrchestrationPrimitive.cs` L543 调用）。
- `MaxSummaryTokens` 改为**可配置**（从 `StateMachineSettings` / `appsettings` 读，经 `IOptions` 注入）。

**代码落点**：`src/AgentPlatform.Infrastructure/Tokenizers/TokenCounter.cs`（`internal sealed`，`Singleton`，无状态）。

**学到的工程点**：
- **token ≠ 字符**：中文尤其如此，1 个汉字 ≈ 1~2 token，纯按 `s.Length/2` 估算会失真。
- **预算要可配置**：不同模型 tokenizer 不同，硬编码魔法数会随模型切换而失效。

---

### 知识点 5 · CI 编译验证作为上线硬前提

**问题**：本环境只有 Python/Node、**无 .NET SDK**，删除/修改代码后无法本地 `dotnet build` 自检。

**解决方案**：把 **CI `dotnet build` + 全测试（单元 / 集成 / SpecFlow / 架构）** 当成上线不可省的门禁。Phase 4 收尾时实测：**Build 0 warnings / 0 errors，Tests 40/40 passed**。

**学到的工程点**：
- CI 是"别人替你编译"的保险，尤其在你本地环境受限时。
- **0 warnings 纪律**（`TreatWarningsAsErrors`）：存根残留、`[Obsolete]` 未清理这类"能编译但埋雷"的问题，会被编译器告警拦下。
- 死代码清理后，**必须**全量编译 + 跑测试，确认没有隐藏的引用断裂。

---

## 8.4 质量治理：两道互补关的分工

Phase 4 仍沿用三道关里的两道（设计评审在动手前已走）：

| 模块类型 | 强制 Skill | 查什么 |
|----------|-----------|--------|
| **叙事性蓝图能力**（类名即承诺能力）| `ddd-code-reviewer`（对抗式）| 实现行为是否忠于蓝图、依赖是否真用、方法是否非空壳 |
| **纯基础设施 / 结构卫生**（仓储 / DI / EF / CRUD）| `ddd-phase-quality-gate`（静态门禁）| DDD 分层 / EF 映射 / 并发 / 密封 / 守卫 |

**为什么"名词即承诺"的模块必须走 code-reviewer**：`ddd-phase-quality-gate` 的 Blueprint Drift 只查"蓝图声明要做但被标记未来的功能"，**不查"实现行为 vs 蓝图叙事"的深度一致性**。像 `PgVectorStore`（名字说它是向量库）、`CriticStepExecutor`（名字说它审查）这类，最容易"编译通过但名不副实"——只有对抗式审查能抓到。

Phase 4 强制范围：`ddd-code-reviewer` 覆盖 **RAG / Critic / 上下文伸缩**；DB 分页走 `ddd-phase-quality-gate`。

---

## 8.5 提炼的 5 条工程原则

1. **fail-loud 优于 fail-silent** —— 质量/安全闸的故障态必须拒绝或报错，不放行。
2. **分页永远在数据库端做** —— 分页前绝不 `ToList()` / `AsEnumerable()`。
3. **token 预算要接真实计数** —— token ≠ 字符，且要可配置。
4. **外部依赖故障要 best-effort 降级** —— 检索/模型挂了，主流程应降级而非崩溃。
5. **CI 编译门禁不可省** —— 它是"别人替你编译"的保险，0 warnings 纪律能拦住埋雷代码。

---

## 8.6 自检清单（可对照代码验证）

- [ ] `PgVectorStore.SearchAsync` 无硬编码 `doc-1/doc-2`，`Ingest/Delete` 真落库
- [ ] `CriticStepExecutor`：`AllowCriticOverride=false` 时，模型异常 → `Approved=false`
- [ ] `WorkflowRepository.QueryAsync` / `ExecutionLogRepository.QueryStepsAsync` 是 `IQueryable` 链式分页，无前置 `ToList()`
- [ ] `TokenCounter.CountTokens` 真实计数，`MaxSummaryTokens` 来自配置
- [ ] `dotnet build` 0 warnings / 0 errors，`Tests 40/40 passed`

---

## 复盘自测

- fail-loud 为什么优于 fail-silent？Critic 异常时默认应该通过还是拒绝？
- 为什么分页必须在数据库端做、不能在内存里 `ToList` 后分页？
- token 为什么不等于字符？`MaxSummaryTokens` 为什么要从配置读？

---

## 8.7 按能力查因（速查表）

> 复习时按"能力"反查：这个能力最容易在哪翻车、怎么验证它真落地了。是「复盘自测」三问的实操版。

| 能力 | 最容易踩的坑（名不副实的表现） | 怎么验证真落地 | 代码落点 |
|------|-------------------------------|---------------|----------|
| ① RAG 接真 PGVector | 返回硬编码 `doc-1/doc-2`；`Ingest/Delete` 只 log；embedding 失败被吞 | `SearchAsync` 无硬编码、`Ingest` 真 `INSERT`；跑通 PG 容器集成测试 | `PgVectorStore.cs` |
| ② Critic fail-loud | 异常时静默 `Approved=true`，质量闸形同虚设 | `AllowCriticOverride=false` 时模型异常 → `Approved=false` | `CriticStepExecutor.cs` |
| ③ EF 数据库端分页 | 先 `ToList()` 全表再内存 `Where/Skip/Take`，数据一涨就 OOM | `QueryAsync` 是 `IQueryable` 链式，无前置 `ToList()` | `WorkflowRepository.cs` |
| ④ 上下文压缩接真 tokenizer | `maxSummaryTokens` 只是占位预算，无真实计数 | `TokenCounter.CountTokens` 真实计数；`MaxSummaryTokens` 来自配置 | `TokenCounter.cs` |
| ⑤ CI 编译门禁 | 本地无 SDK 误以为编译过；0 warnings 纪律缺失 | `dotnet build` 0 warnings；Tests 40/40 passed | `.quality-gate.json` |

**记忆钩子**：①②③④ 是"名词即承诺"的叙事性模块，归 `ddd-code-reviewer` 查；③ 的结构卫生归 `ddd-phase-quality-gate`。

---

## 8.8 参考代码

- `src/AgentPlatform.Infrastructure/VectorStore/PgVectorStore.cs` — 真 PGVector 实现
- `src/AgentPlatform.Infrastructure/Workflows/CriticStepExecutor.cs` — fail-loud 质量闸
- `src/AgentPlatform.Infrastructure/Persistence/Repositories/WorkflowRepository.cs` — DB 端分页
- `src/AgentPlatform.Infrastructure/Persistence/Repositories/ExecutionLogRepository.cs` — owned-entity 查询
- `src/AgentPlatform.Infrastructure/Tokenizers/TokenCounter.cs` — 真实 token 计数
- `src/AgentPlatform.Infrastructure/Workflows/OrchestrationPrimitive.cs` — 上下文注入与压缩（`BuildWorkflowContext`）
- `phases/phase-4-grounding.md` — 验收标准 + Quality Gate Report
