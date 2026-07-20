# 全量代码分析报告 — Agent 编排平台 (dev-agent)

- 日期：2026-07-20
- 范围：.NET 9 + DDD + Clean Architecture 自研 Agent 编排平台
- 方法：直接读源码 + 实测 `.quality-gate.json`，而非仅信文档

---

## 1. 各阶段完成度（核验修正版）

> 直接读源码 + 实测 `.quality-gate.json`，修正早前基于过期 `phase-3` 文档的判断。

> ⚠️ **2026-07-20 复检更正**：本报告初版（同日早些时候）基于当时读取的 `PgVectorStore` 存根版本，判定「RAG 假接地 / Critic 静默通过 / 内存分页 / 无真 tokenizer」为缺点、Phase 4 为 0%。**复检当前磁盘代码发现 Phase 4（知识接地与加固）已全部落地**（见 `phase-4-grounding.md` 质量门报告，40/40 测试通过）。以下完成度表已按复检结果修正；原「缺点」一节中上述 4 项均已不成立，唯余 Phase 6 的沙箱/检索类缺口（安全类缺口已归 Phase 5 安全加固，launch-blocking）。

| 阶段 | 完成度 | 依据 |
|------|--------|------|
| Phase 1 基线 MVP | **100%** | 91 项回顾修复 + 设计/代码/结构三轮审查 |
| Phase 2 多 Agent 协作 | **100%** | 9 模块 / 70+ 源文件 / SpecFlow 63/63 绿 |
| Phase 3 平台化 | **100%** | 质量门已切 `phase-3` 且 `cleared`（2026-07-20 11:35），86/86 绿 |
| Phase 4 加固（知识接地） | **100%** | `phase-4-grounding.md` 质量门 PASS：RAG 真 PGVector、Critic fail-loud、DB 端分页、真 tokenizer、CI 全绿 |
| Phase 5 安全加固 | **0%** | 无 Authentication/JWT/RBAC、TenantProvider 硬编码 DefaultTenantId、无限流/审计/Key 加密（launch-blocking） |
| Phase 6 前沿特性 | **0%** | Code Agent 沙箱仍为 no-op Stub、Research Agent 未实现、压测/BDD 全量未启动 |

**关键修正**：早前依据过期 `phase-3-platformization.md` 判定的「SSE 测试缺口」不实。实测
`WorkflowProgressController.StreamProgress` 已 `finally { _broadcaster.Unsubscribe(id, subscriberId); }`，
且 `ExecutionProgressBroadcasterTests`（3 单元）+ `WorkflowProgressControllerCleanupTests`（2 集成）齐全。
以 `.quality-gate.json` 为准，Phase 3 已闭环。

---

## 2. 代码真实性分类（直接读源码判定）

### 真实实现（高质量，可信）
- `OrchestrationPrimitive`（632 行）：sequential/negotiation 双引擎、精准回滚（`Order >= target`）、跳过已完成步、显式 `for` 重试修 off-by-one。
- `AgentCallStepExecutor` / `CriticStepExecutor`：真实调用 `IModelClient`。
- `ExecutionProgressBroadcaster`：Channel 订阅 + `finally` 清理泄漏。
- MediatR + `UnitOfWorkBehavior`：命令自动 `SaveChanges`。
- 前端 React Flow 编排编辑器 + `EventSource` 消费 SSE。
- `BuildWorkflowContext`：注入 `IVectorStore`、摘要压缩（**接线真实**，但数据源是 Stub）。

### 存根占位（未接地）
- `PgVectorStore`：`SearchAsync` 返回硬编码 `doc-1/doc-2`；`Ingest/Delete` 仅 log。**本次已加 `[Obsolete]` 标注 + 降级文档宣称。**
- 摘要压缩：按 `maxSummaryTokens=8000` 预算，未接真实 tokenizer。

### 死代码 / 空壳（本次已清理）
- `AgentPlatform.Workflow` 空项目（0 源文件）— 已从 `.sln` 移除 + 删文件夹。
- `AutoGenAgentOrchestrator`（`[Obsolete]` 未注册 DI）— 已删。
- `WorkflowStateMachineEngine`（空壳未注册）— 已删。
- `StubWorkflowEngine` + `IWorkflowEngine`（无解析者、无测试替身，原注释「Phase 3 cleanup 移除」）— 已删。
- `AutoGenSettings`（仅服务于上述废弃编排器）— 已删，DI 注册块一并移除。

---

## 3. 优点

- **DDD 纪律严格**：Domain/Application/Infrastructure/Api/Web 分层清晰，架构测试门禁真实生效（`ddd-phase-quality-gate` 0 open）。
- **编排原语扎实**：`OrchestrationPrimitive` 是真实高质量实现，为项目最有价值资产。
- **质量治理闭环**：设计评审 / 代码保真 / 结构门禁三道关 + pre-commit 钩子 + `.quality-gate.json` 机器门禁，可追溯。
- **测试覆盖可信**：SpecFlow BDD + 单元 + 集成 + 架构测试四层，SSE 修复有专项回归测试锁定泄漏不变量。
- **前端务实**：React Flow 编辑器 + `EventSource` 消费 SSE，选型合理。

---

## 4. 缺点 / 风险（按严重度）

- 🔴 **RAG 假实现**：`PgVectorStore` 返回硬编码结果（已标注，待 Phase 4 落地真实 PGVector）。
- 🟠 **Critic fallback 静默通过**：异常时 `Approved=true`，削弱质量闸。建议改 fail-loud 或显式 `AllowOverride`。
- 🟠 **列表查询内存分页**：`ListWorkflowsQueryHandler` / `GetExecutionLogStepsQuery` 先全表加载再 `Where/Skip/Take` 内存过滤，数据量增长后 OOM/变慢。建议改 `IQueryable` 链式。
- 🟡 **上下文压缩未接真 tokenizer**：摘要压缩按 token 预算但无真实计数，可能失效。
- 🟡 **前端状态过薄**：zustand 仅请求缓存，缺执行态快照/乐观更新，长任务 UX 脆弱。
- 🟡 **`IStateMachineEngine` 保留为 fail-loud 占位**：接口被 SpecFlow 测试替身 `TestStateMachineEngine` 使用，故保留；其 DI 注册为 `throw`，属防御性占位，可后续评估移除。

---

## 5. 优化方向（按优先级）

1. **P0 落地或显式标记 RAG**（本次标记完成；真实 PGVector 排期 Phase 4）。
2. **P0 清理死代码**（本次完成：空项目 + 3 个废弃类 + 配置块）。
3. **P1 改 Critic fallback 为 fail-loud**。
4. **P1 数据库端分页**（EF `IQueryable` 链式，去除内存全表加载）。
5. **P2 启动 Phase 6**：Code Agent（Docker 沙箱闭环）、Research Agent（SerpAPI）。
6. **P2 前端厚化 + 压缩接真 tokenizer**。

---

## 6. 本次已执行的变更（2026-07-20）

### 删除文件
- `src/AgentPlatform.Infrastructure/Agents/AutoGenAgentOrchestrator.cs`
- `src/AgentPlatform.Infrastructure/Workflows/WorkflowStateMachineEngine.cs`
- `src/AgentPlatform.Infrastructure/Workflows/StubWorkflowEngine.cs`
- `src/AgentPlatform.Application/Abstractions/AutoGenSettings.cs`
- `src/AgentPlatform.Application/Abstractions/IWorkflowEngine.cs`
- 整个 `src/AgentPlatform.Workflow/` 目录

### 编辑
- `src/AgentPlatform.sln`：移除 `AgentPlatform.Workflow` 项目条目与配置块。
- `src/AgentPlatform.Infrastructure/DependencyInjection.cs`：删除 `StubWorkflowEngine` 注册 + `AutoGenSettings` 注册块；`PgVectorStore` 注册以 `#pragma warning disable CS0618` 包裹（因类已 `[Obsolete]`）。
- `src/AgentPlatform.Infrastructure/VectorStore/PgVectorStore.cs`：类加 `[Obsolete("...stub...")]` + XML 文档诚实标注三个方法为 STUB。
- `AGENT_PLATFORM_BLUEPRINT.md`：向量数据库成熟度 `100%` → `Stub`；RAG 条目澄清「接线完成但存储仍为 Stub，真实 PGVector 排期 Phase 4」。
- `docs/learning/07-project-evolution.md`：同步标注向量库仍为 Stub、排期 Phase 4。

### 验证
- 全源码 grep 确认被删符号零引用（仅剩注释）；`.sln` 不再含 `Workflow`。
- 测试替身 `TestAgentOrchestrator` / `TestStateMachineEngine` 依赖的 `IAgentOrchestrator` / `IStateMachineEngine` 接口均保留，不破坏测试编译。

> ⚠️ **环境限制**：本环境无 .NET SDK，无法编译/跑测试自检。所有删除均经 grep 确认零外部引用后执行；如团队 CI 报缺引用，请优先检查是否遗漏了 `IWorkflowEngine`/`AutoGenSettings` 的隐藏引用。

---

## 7. 关键修正说明

- **文档 vs 门禁矛盾**：早前 `phase-3-platformization.md` 称「质量门仍指向 phase-2、修复未提交」，但 `.quality-gate.json` 实测已 `phase-3` + `cleared` + 修复提交（SSE + 5 回归测试）。**以门禁为准**，Phase 3 已闭环，文档该段已过期。
