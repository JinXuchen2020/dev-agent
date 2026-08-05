# F24 · 执行 Trace / 评估视图 — 质量门报告

> 阶段：feature-builder Phase 5 三道质量门（reviewer / structureGate / codebaseOptimizer）
> 分支：`feat/f24-execution-trace` · 关联设计：`features/execution-trace-eval.md`（§6 决策 2026-08-05 锁定）
> 验证基线：`dotnet build src/AgentPlatform.sln` 0/0 · `dotnet test` F24 增量 12/12 · 前端 `node scripts/qa.mjs` OVERALL PASS

## 1. reviewer · ddd-code-reviewer（对抗式审查）

聚焦 F24 增量核心类型与边界，逐项寻找逻辑结构缺陷与测试覆盖缺口。

**审查对象（增量）**
- `RunEvaluationCommandHandler`（评估主链路：克隆工作流→跑编排→取末位 Completed 结果→比对→汇总报告→审计）
- `EvaluationDatasetsController`（6 端点，读写 RBAC 拆分）
- `EvaluationDataset` / `EvaluationCase` 聚合（ITenantScoped + 拥有实体）
- `EvaluationDatasetConfiguration`（OwnsMany + ValueGeneratedNever）
- `StepTraceEventHandler`（token / NodeType 写入 ExecutionLogEntry）
- `ExecutionLogDetailPage` 三列扩展 + `api.ts` / `types` / `locales`（中-en 对称）/ 路由 / 菜单

**发现与判定（0 P0 / 0 P1 / 0 P2 / 0 P3）**
1. ✅ **克隆工作流隔离（关键正确性）**：`RunEvaluation` 每 case `new Workflow(Guid.NewGuid(), …)` 克隆源步骤，避免编排器 `repository.Update(workflow)+SaveChanges` 污染调用方原工作流——设计决策落地正确，单元测用 mock `IOrchestrationPrimitive` 验证。
2. ✅ **Token 核算短路安全**：`Matches()` 前以 `error is null && actual is not null` 短路，避免 `Contains` 在 null 上 NPE；`Exact` 用 `string.Equals(Ordinal)`、`Contains` 用 `OrdinalIgnoreCase`，与设计 §3 一致。
3. ✅ **失败 case 不中断**：单 case 异常被 `try/catch` 捕获写入 `errorDetail`，其余 case 继续，报告 `passed/total` 真实反映部分成功。
4. ✅ **聚合守卫**：`EvaluationDataset` / `EvaluationCase` 构造器 `ThrowIfNullOrWhiteSpace`；`CreateEvaluationDatasetCommand` 校验 `name` 非空 + `Cases.Count > MaxCases` 抛 `InvalidOperationException`（默认 10，可配置）。
5. ✅ **EF Guid 陷阱规避**：根 `Id` 与拥有实体 `EvaluationCase.Id` 均 `ValueGeneratedNever()`，避免 owned children 被误判 `ValueGeneratedOnAdd` 致 UPDATE 非 INSERT 并发错（与 F13/F23 既有经验一致）。
6. ✅ **RBAC 与前端门控对齐**：Controller GET `[Authorize]`、POST/PUT/DELETE/run `[Authorize(Roles="Admin,Operator")]`；前端 `canWrite`（Admin/Operator）隐藏写按钮；侧边栏入口对所有已认证可见（读开放），与后端一致。
7. ✅ **审计落库**：`RunEvaluation` / `CreateEvaluationDataset` / `UpdateEvaluationDataset` / `DeleteEvaluationDataset` 四枚举已加，`RunEvaluationCommandHandler` 调 `auditLogRepository.Add` 经 UoW 提交。
8. ⚠️ **已知残留（非阻断，已在设计 §1 out / §6 S4 记录）**：
   - **节点级 Input 采集 v1 不做**（需编排器额外 plumbing）。
   - **Token 实际落库依赖编排器对评估克隆工作流产生 ExecutionLog**（与 F20 Trace 共用 RunWorkflow 管线；单元测用 mock `GetByWorkflowIdAsync(Arg.Any<Guid>())` 验证求和逻辑，生产由真实管线保证）。

## 2. structureGate · ddd-phase-quality-gate（12 类全扫）

| 类别 | 结论 |
|---|---|
| DI 注册完整性 | ✅ `IEvaluationDatasetRepository→EvaluationDatasetRepository`（Scoped）；`EvaluationSettings` 经 `configuration.GetSection("Evaluation")` Configure |
| DDD 分层 | ✅ Application 仅引用 Domain（聚合/枚举/仓储接口/Abstractions）；Infrastructure 实现仓储+配置；Api 仅 `IMediator`+`ITenantProvider`。grep 确认 Application 无 Infrastructure 引用 |
| EF 映射 | ✅ `EvaluationDatasetConfiguration : IEntityTypeConfiguration<EvaluationDataset>`；根与拥有实体双 `ValueGeneratedNever`；迁移 `20260805080820_AddEvaluation` 含 `#pragma warning disable IDE0161` |
| CT 透传 | ✅ Handler / Repository 全链路 `CancellationToken ct` 透传至 EF |
| internal sealed | ✅ Repository / Configuration / Handler 均 `internal sealed` |
| 并发 | ✅ 无新增 Singleton / grow-only 容器 |
| 空守卫 | ✅ 聚合构造器 `ThrowIfNullOrWhiteSpace`；`RunEvaluation` 对 `actual`/`log` 空判；`GetByIdAsync` 返回 null→`KeyNotFoundException`→404 |
| API 基础设施 | ✅ Controller 仅依赖 `IMediator`；新增 `KeyNotFoundExceptionHandler` 映射 404 ProblemDetails |
| 蓝图漂移 | ✅ 无（新增资源，不改动既有契约） |
| XML 文档 | ✅ 公共类型/成员中文 `/// summary`（Controller/Command/Request 齐备） |
| 死代码 | ✅ 无残留（枚举新增均被 emit） |
| i18n 对称 | ✅ `zh-CN.ts` / `en-US.ts` `evaluation` 块 + `common.view` + `nav.evaluationDatasets` 双向对称；前端 `tsc --noEmit` 0 error、lint 0 error |

## 3. codebaseOptimizer（七维）

- **架构**：DDD 分层正确，接口 Domain.Repositories / 实现 Infrastructure / DI 三处齐备；复用 F7 `RunWorkflow` 编排路径，零重复。
- **代码质量**：`internal sealed` + 中文 XML 文档 + 命名一致（`EvaluationDatasetSummaryResponse/DetailResponse/CaseResult/Report`）。
- **正确性**：克隆隔离 + 租户隔离（ITenantScoped 自动 filter）+ 审计 + MaxCases 上限 bounding；种子/迁移幂等。
- **测试**：后端 F24 增量 **12/12**（StepTrace 7：token/NodeType 持久化、null-tenant 默认、缺失 log NoOp、StepExecutionResult 四态 token 透传；Eval 5：聚合 Update 替换 cases、Create 映射、>MaxCases 拒绝、RunEvaluation Contains 通过+求和、Exact 失配失败）；架构测试随全量跑；前端 `qa.mjs`（typecheck/lint/build/unit）全绿。
- **性能**：`RunEvaluation` 同步批量但硬上限 MaxCases=10（可配置）+ 逐 case 复用编排器 step 超时 bounding；`ListEvaluationDatasets` 仅按 TenantId+keyword 过滤，无 N+1。
- **安全**：写端点 RBAC Admin,Operator；读需认证；前端无 `dangerouslySetInnerHTML`、无 XSS；无硬编码密钥（ApiKey 体系复用）。
- **工程化**：EF 迁移含 `#pragma`；`dotnet build` 0 警告；i18n 对称；lint 0 error；`EvaluationSettings.MaxCases` 可配置（appsettings）。

## 4. 验收对齐（设计 §5）

- ✅ Trace 视图展示 节点类型 / 耗时 / TokensIn / TokensOut / 输出 / 错误（新增三列扩展既有明细页）。
- ✅ 数据集 CRUD 正常（tenant 隔离）+ 运行评估返回通过率与逐 case 结果（input/expected/actual/passed/durationMs/tokens/error）。
- ✅ 多租户隔离（`EvaluationDataset` 自动 filter；`ExecutionLog` 维持手动不破坏）。
- ✅ 审计落库；前端 `tsc 0` + `qa.mjs` 全绿。

## 5. 结论

三道质量门对 F24 增量 **均为 0 open**。`cleared:true`，与 `.quality-gate.json` 同笔暂存以满足 pre-commit 钩子。
