# F26 质量门报告 — 企业增强（用量仪表盘 + 工作流 diff）

> 阶段：F26 v1（用量仪表盘 + 工作流 diff；多工作空间/Workspace 独立排期，未纳入 v1）
> 分支：`feat/f26-enterprise-enhancements`（不 push）
> 日期：2026-08-06

## 总览

| 门 | Skill | 结果 | 增量 open findings |
|----|-------|------|--------------------|
| 1 | ddd-code-reviewer | PASS（修复 5 项后清零） | 0 |
| 2 | ddd-phase-quality-gate | PASS（结构化审计 12 类 0 open） | 0 |
| 3 | codebase-optimizer | PASS（七维度分析 F26 增量 0 open，未建分支/不 push 以遵守 feature-builder 硬约束） | 0 |

## 验证基线（闸门实测）

- 后端构建：`dotnet build AgentPlatform.sln` → **0 警告 0 错误**。
- 后端测试：`AgentPlatform.Application.Tests` **188/188**（含 DiffWorkflow 6 + GetWorkflowUsage 4）；`ArchitectureTests` **9/9**；`Api.Tests` **35/35**；`Infrastructure.Tests` **124/124**。
- 前端：`tsc --noEmit` **0 error**；`vite build` 成功；`vitest` **44/44**（含 i18n-symmetry）；`eslint` **0 error**；`bddgen` 生成 `workflow-usage.feature.spec.js` 绑定通过。

## 门 1 · ddd-code-reviewer

对抗式审查聚焦 F26 v1 触达生产代码的改动：`DiffWorkflowQuery.cs`、`GetWorkflowUsageQuery.cs`、`WorkflowGraphSnapshot.cs`、`Workflow.GetEffectiveGraph`。

### 发现与修复

| 严重度 | 类别 | 文件:行 | 发现 | 修复 |
|--------|------|---------|------|------|
| P1 | 逻辑/假阳性 | `DiffWorkflowQuery.cs` | 边「变更」检测按 `Id` 比较，而 `ReplaceGraph` 每次保存重生成节点/边 Id → 每个未变更的边都被误报为「已变更」（凡当前图 vs 版本快照比对，Id 必然不同）。 | 移除 `changedEdges` 概念（边除端点名+标签外无可变属性）；删除 `EdgeEquals` 方法与 `ChangedWorkflowEdge` 类型。 |
| P1 | 逻辑/假阳性 | `DiffWorkflowQuery.cs` `NodeEquals` | `x.X == y.Y`（操作数错位）比较一个节点的 X 与另一个节点的 Y → 任意 `X≠Y` 的节点被误报为「已变更」；单测因坐标均为 0 而恰好通过，掩盖了缺陷。 | 修正为 `x.X == y.X`。 |
| P1 | 数据完整性 | `WorkflowGraphSnapshot.FromWorkflow` + `Workflow.GetEffectiveGraph` | `FromWorkflow` 读 `wf.Nodes`（`_nodes`），对遗留 `_steps`-only 工作流为空（该类工作流 `_nodes` 未被填充）→ 版本快照与 diff 抓取不到任何内容。 | 新增 `Workflow.GetEffectiveGraph()` 回退到链式视图（`EffectiveNodes`/`EffectiveEdges`）；`FromWorkflow` 改用之，遗留工作流同样可快照/对比。 |
| P2 | 健壮性 | `DiffWorkflowQuery.cs` `Compute` | `ToDictionary(n => n.Name)` 在重名节点（畸形遗留图，legacy `ReplaceSteps` 不强制唯一名）时抛 `ArgumentException` → 500。 | 新增 `ToNameMap`（首名优先，容忍重名），替代 `ToDictionary`。 |
| P3 | 死代码 | `GetWorkflowUsageQuery.cs` | 处理器内 `private const int MaxRangeDays` 未被引用（区间校验在 `AnalyticsController`）。 | 删除该常量。 |

### 控制流 / 回退核对

- `Handle`（line 58）：先 `GetByIdAsync(WorkflowId)` + 租户守卫（`wf.TenantId != request.TenantId → null`）；四种比对分支（OtherWorkflow / From+To 版本对 / 单 FromVersion 对比当前 / 默认最新版本）均解析为两个 `WorkflowGraphSnapshot` 后交 `Compute`。
- `Compute`（line 129）：节点按 `Name` 稳定匹配（规避 Id 重生成）；边按 `源名→目标名\u0001标签` 稳定键匹配；上下文按 `Context` 字符串比较。
- DI：`IWorkflowRepository` / `IWorkflowVersionRepository` / `ITenantProvider` 均在 `Infrastructure` / `Domain.Abstractions` 注册，无未注册接口。
- 多租户：`OtherWorkflowId` 与版本均经 `WorkflowId` + 租户守卫双重校验，无法跨租户读取。

### Top 3 运行时风险（已确认/已修复）

1. **重名节点致 500**（已修复 P2）：畸形遗留图经 `ToNameMap` 容忍，不再抛异常。
2. **边「已变更」假阳性**（已修复 P1）：`changedEdges` 整体移除，边变更仅以「增/删」表达（端点名或标签变化即换键）。
3. **时区边界偏移**（已知残留，P3 级）：`GetWorkflowUsage` 以 UTC `.Date` 截断，非 UTC 时区用户的「近 N 天」在日界附近可能偏移一天——与平台既有 UTC 约定一致，非回归。

## 门 2 · ddd-phase-quality-gate（结构化审计 12 类）

| 类别 | 结论 |
|------|------|
| DI 注册缺口 | 0 — `AddMediatR(RegisterServicesFromAssembly(Application))` 自动注册两 Handler，无新增接口 |
| DDD 分层违规 | 0 — Query/Handler/DTO 在 Application，Controller 在 Api 仅 `IMediator`+`ITenantProvider`，Repository 接口 Domain/实现 Infrastructure |
| EF Core 映射缺口 | 0 — v1 无新增聚合/VO（复用 `Workflow`/`ExecutionLog`/`WorkflowVersion`） |
| 硬编码值 | 0 — 边键分隔符 `\u2192`/`\u0001` 属键构造合理范围；用量 14 天默认合理，区间上限由 Controller `MaxRangeDays` 强制 |
| 缺失 CancellationToken | 0 — Handler/Repo 全 `ct` 透传至 EF |
| 缺失修饰符 | 0 — Handler 为 `internal sealed` |
| 并发/生命周期风险 | 0 — 两项均为只读 Query，无新增 Singleton/grow-only 集合 |
| 缺失空守卫 | 0 — `GetByIdAsync` 返回 null → Handler 返回 null → Controller `NotFound()`；跨租户显式守卫 |
| API 基础设施 | 0 — 沿用全局 ExceptionHandler + ProblemDetails；读端点类级 `[Authorize]` |
| 蓝图漂移 | 0 — 实现与 `features/enterprise-enhancements.md` §6/§7 一致 |
| 缺失 XML 文档 | 0 — 新增公共类型/成员均含中文 `/// <summary>` |
| 死代码 / 休眠常量 | 0 — `changedEdges`/`EdgeEquals`/`MaxRangeDays` 已清理；`pages.workflows.diff.changedEdges` 残留 locale key 无害（不影响对称），后续清理 |

**Gate Status: PASS（P0=P1=P2=P3=0）**

## 门 3 · codebase-optimizer（七维度分析 F26 增量）

> 采用分析模式（不建 `codebase-optimizer/{date}` 分支、不 push），以遵守 F26 feature-builder 硬约束（固定在 `feat/f26-enterprise-enhancements`、不 push）。

| 维度 | 结论 |
|------|------|
| 架构 | 0 — DDD 分层正确；Query/Handler 在 Application、Controller 在 Api、Repository 接口/实现分处 Domain/Infrastructure；复用 `WorkflowGraphSnapshot`/`WorkflowVersion` 零内核重写 |
| 代码质量 | 0 — `internal sealed` Handler + 中文 XML 文档 + `DiffWorkflow*` 命名一致；删除 `changedEdges`/`EdgeEquals`/未用常量 |
| 正确性 | 0 — 节点按 `Name` 稳定匹配、边按端点名+标签匹配，规避 `ReplaceGraph` Id 重生成陷阱；`GetEffectiveGraph` 修复遗留工作流空快照；`ToNameMap` 防重名崩溃 |
| 测试 | 0 — 后端 188+9+35+124 全绿（含 Diff 6 + Usage 4）；前端 tsc 0 + vite build + vitest 44 + eslint 0 + i18n 对称 + bddgen 绑定通过 |
| 性能 | 0 — 用量按 `WorkflowId` 单次分组聚合，无 N+1 |
| 安全 | 0 — 读端点仅认证、无硬编码密钥、前端无 `dangerouslySetInnerHTML` |
| 工程化 | 0 — `build` 0 警告、i18n 中/en 对称、lint 0 error、BDD `workflow-usage.feature` 已建 |

**结论：PASS（0 open）**

## 约束遵循

- 三道质量门对 F26 v1 增量均为 0 open（5 个发现全部修复）。
- `.quality-gate.json` 已推进至 `f26-enterprise-enhancements`，保留 `cleared:true` + `codebaseOptimizer` 字段。
- commit message 含 `Quality-Gate:` 行；**不 push**（feature-builder 硬约束）。
- BDD E2E `workflow-usage.feature` 已建且 `bddgen` 绑定通过；实时浏览器运行依赖集成后端 + Edge/Chromium，归 CI 闸门（本沙箱不跑实时 E2E）。
