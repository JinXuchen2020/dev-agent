# F7 · 工作流版本管理 + 导入导出 — 质量门报告

> 分支：`feat/f7-workflow-versioning`
> 日期：2026-07-30
> 三道门：ddd-code-reviewer / ddd-phase-quality-gate / codebase-optimizer —— 全部 PASS（P0/P1/P2 = 0 open；gate 1 发现的 1×P1 已当场修复）

## 1. 实现范围（对照 `features/workflow-platformization.md` §0–§7，子项①）
1. **版本聚合**：新增 `WorkflowVersion`（Domain 聚合，`ITenantScoped`，不可变快照：Context+Nodes+Edges 序列化为 JSON）。
2. **快照机制**：`WorkflowGraphSnapshot` 记录（FromWorkflow / ToJson / FromJson / ToReplaceGraphArgs）；快照以原节点 Id 作 TempId，`ReplaceGraph` 内部重映射保留图拓扑。
3. **7 端点**：`POST {id}/versions`（存为版本）、`GET {id}/versions`（列表分页）、`GET {id}/versions/{vid}`（详情）、`POST {id}/versions/{vid}/restore`（回滚）、`DELETE {id}/versions/{vid}`（删除，幂等）、`GET {id}/export`（导出 JSON）、`POST import`（导入为新工作流）。
4. **回滚守卫**：Running/Paused 抛 `WorkflowConflictException` 拒绝回滚；回滚重建图 + 更名 + 更新 context。
5. **导入校验**：经 `Workflow.ReplaceGraph` 校验图结构；导入恒为**新**工作流，不覆盖。
6. **审计**：新增 5 个 `AuditActionType`（CreateWorkflowVersion/RestoreWorkflowVersion/ImportWorkflow/ExportWorkflow/DeleteWorkflowVersion）；Export 为查询，已显式注入 `IAuditLogRepository`+`IUnitOfWork` 持久化审计（修复 gate 1 死代码）。
7. **前端**：`WorkflowsPage` 版本历史 Drawer（存为版本/回滚/删除/导出），`WorkflowCanvasPage` 导入 JSON（创建新工作流）；RBAC `canManage` 与后端 `[Authorize(Roles="Admin,Operator")]` 对齐；`types`/`api`/`locales` 全量对齐（i18n 对称）。
8. **EF 迁移**：`20260730062346_AddWorkflowVersions`（含 `#pragma warning disable IDE0161` file-scoped 豁免）；`Id` `ValueGeneratedNever()` 避 GUID 陷阱。

## 2. Code Review（ddd-code-reviewer）结论
**穷尽分析无遗留 P0/P1/P2 缺陷**。核心探查点：
- **控制流**：`Controller → IMediator.Send → Handler` 全链路；Handler 经 `RegisterServicesFromAssembly` 自动注册；`UnitOfWorkBehavior` 对 `ICommand` 自动 `SaveChangesAsync`（Create/Restore/Delete/Import 持久化经测试验真）。
- **租户隔离**：聚合 `ITenantScoped` + 全局 query filter；Handler 显式 `wf.TenantId != request.TenantId → 404`，不泄露存在性。
- **静默崩溃路径**：回滚冲突→异常（400/409）；删除查无→幂等 return；导入 `request is null → BadRequest`；`FromJson` 损坏 JSON→`InvalidOperationException`。

### 未修观察（超范围 / 设计项，非回归）
- **P3-OBS-1**：`WorkflowVersion.CreatedBy` 恒 `null`（审计不记录操作人）。F7 设计未要求落地操作人，前端已 `v.createdBy &&` 守卫。如需改进，应在 `CreateWorkflowVersionCommand` 注入 `IUserContext`，属后续增强。
- **P3-OBS-2**：版本号并发（`GetLatestVersionNumberAsync()+1` 无行锁）可能重复，索引非唯一故不抛异常。设计项 G6，单租户低频写入下风险极低。

## 3. Structure Gate（ddd-phase-quality-gate）结论
12 类全扫（G1–G12，详见 `features/workflow-platformization.md` §7）：
- **DI 注册**：`IWorkflowVersionRepository` 在 `DependencyInjection.cs` 注册（行 102）；Handler 自动注册。
- **DDD 层**：聚合/仓储接口在 Domain，实现在 Infra；`internal sealed` 一致。
- **EF 映射**：`WorkflowVersionConfiguration`（`ValueGeneratedNever` + 非唯一索引 + nvarchar(max) 快照），迁移落盘。
- **CancellationToken**：全链路 `ct` 透传。
- **并发**：UoW 单提交；删除幂等、回滚冲突守卫。
- **null 守卫**：查无→404/幂等；导入 `request is null → BadRequest`。
- **API 基础设施**：401/403/404/204 处理到位（导出/列版本/详情仅 `[Authorize]`，写/回滚/删/导入 `[Authorize(Roles="Admin,Operator")]`）。
- **死代码**：gate 1 发现的 `ExportWorkflow` 审计死代码**已修复**（Export 查询现显式持久化审计）。
- **XML 注释 / Swagger / 蓝图漂移**：齐全；设计文档 §0–§7 与实际实现一致。

### gate 1 修复记录
| Severity | 文件 | 发现 | 修复 |
|----------|------|------|------|
| P1 | `ExportWorkflowQuery.cs` | `AuditActionType.ExportWorkflow` 声明但永不发出（Export 是查询，UoW 不自动审计）→ 死代码 | 注入 `IAuditLogRepository`+`IUnitOfWork`，显式 `AuditLog.Record(...ExportWorkflow...)` + `SaveChangesAsync`，重新跑测试仍 298/298 |

## 4. Codebase Optimizer（codebase-optimizer）结论
七维聚焦 F7 改动范围，0 open（详见 `.codebase-optimizer/rounds/round-f7-01-report.md`）：
- **无桩代码**：Handler 真实持久化/查询；Controller 真实派发；仓储真实查库。
- **XSS**：前端仅 antd 声明式 + `t()`，无 `dangerouslySetInnerHTML`。
- **类型**：`tsc --noEmit` 0 error；版本接口显式类型化，无 `any` 泛滥。
- **未捕获 Promise**：所有版本/导入操作 try/catch + `getErrorMessage`。
- **React key**：列表项 `v.id`；`actions` 加 `key`。
- **未用导入**：`vite build` 0 警告。
- **i18n 对称**：`versions` 块 zh-CN/en-US 键完全对称（由 `i18n-symmetry.test.ts` 强制）。
- **后端**：`dotnet build` 0/0；全方案 `dotnet test` **298/298**。
- **前端**：`node scripts/qa.mjs` OVERALL PASS（typecheck/lint/build/unit 全绿）。

## 5. 验证汇总
| 层 | 命令 | 结果 |
|----|------|------|
| 后端构建 | `dotnet build` | 0 error / 0 warning |
| 后端测试 | `dotnet test src/AgentPlatform.sln` | **298/298**（SpecFlow 41 / Arch 9 / App 114 / Infra 102 / Api 27 / Integration 5） |
| 前端类型 | `tsc --noEmit` | 0 error |
| 前端 lint | `eslint` | 0 error（21 warning，均 warn 不阻断） |
| 前端构建 | `vite build` | 通过 |
| 前端单测 | `vitest run` | 全绿 |
| 质量门 | 三道（reviewer / structureGate / optimizer） | 全部 PASS（cleared:true） |

## 6. 遗留 / 后续
- `CreatedBy` 操作人落地（P3-OBS-1，需 `IUserContext`）—— 后续增强。
- 版本号并发去重（P3-OBS-2，G6 设计项）—— 如需强一致可加唯一索引 + 重试。
- F7 其余子项（② 版本差异查看 / ③ 回滚预览 / ④ 版本标签 / ⑤ 定时快照 / ⑥ 版本权限 / ⑦ 跨工作流复制 / ⑧ 版本讨论）见 `features/workflow-platformization.md`，未在本轮实现。
