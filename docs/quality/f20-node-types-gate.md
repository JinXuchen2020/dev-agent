# F20 节点全家桶 · 质量门报告（f20-node-types）

> 分支：`feat/f20-node-types`（本地，未推送）。三道质量门：`ddd-code-reviewer` + `ddd-phase-quality-gate` + `codebase-optimizer`（Phase 6+ 强制 PASSED）。
> 结论：**三道门 0 open findings**，`cleared: true`，根 `.quality-gate.json` 已与 src/ 改动一同暂存。

## 1. ddd-code-reviewer（对抗式代码审查）

### 审查范围
F20 全部后端实现：6 个 executor（Http/Condition/Variable/SubWorkflow/Delay/UserInput）+ `JsConditionEvaluator`（Jint 沙箱）+ `SequentialOrchestrator` 分支/循环/HITL 引擎 + HITL 审批恢复链路（`UserInputStepExecutor` / `ResolveApprovalCommandHandler` / `ListApprovalsQuery` / `WorkflowsController` 路由）+ `HumanApproval` 聚合与仓储 + `WorkflowNode.SetResult` 守卫。前端 HITL 接线（`WorkflowCanvasPage` / `api.ts` / `types` / `VariableWatchPanel`）作辅助核对。

### 控制流追踪（Section A 状态机 + C 编排器 + G API + F 仓储 + Z 通用）
- 主入口 `RunSequentialAsync`：逐节点执行；`NeedsIntervention` → `SetState(Paused)` + `return`（节点本身不置 Completed，保持 Running）。
- HITL 恢复：`ResolveApprovalCommandHandler` 写回 `uiNode.SetResult(input)`（SetResult 同时置 `State=Completed`）→ 仅当 `Paused` 时 `ResumeAsync` → `RunSequentialAsync` 跳过 `Completed` 节点，**不重跑 UserInput**（VERIFIED）。
- 分支：`ApplyBranchSkip` 计算非选中分支可达子图并排除与选中分支重叠的 join 节点（`ReachableFrom` BFS），`skip` 幂等（VERIFIED）。
- 循环：`RunLoopBodyAsync` 逐 item 注入 `itemVariable` 入共享 Blackboard，`bodyNode.Reset()` 每轮重跑，主线性遍历经 `loopBodyIds` 跳过 body（VERIFIED）。
- 续跑：已 `Completed` 的 Condition 节点在内存 skip 丢失后于启动阶段按 `Result` 重算 skip（VERIFIED）。

### 行为不变量追踪
| # | 不变量 | 结论 | 位置 |
|---|--------|------|------|
| 1 | 分支 skip 不误跳 join / 不漏跳非选中分支 | VERIFIED | SequentialOrchestrator.cs:426-456 |
| 2 | 循环每 item 重置 body，`<`/`<=` 无 off-by-one | VERIFIED（foreach 自然逐项） | SequentialOrchestrator.cs:462-528 |
| 3 | HITL resume 跳过已完成 UserInput 节点 | VERIFIED（SetResult 置 Completed） | ResolveApprovalCommandHandler.cs:56-64 |
| 4 | Jint 沙箱不暴露宿主 API（无 CLR）+ 超时边界 | VERIFIED（默认 AllowClr=false + TimeoutInterval 2s + MaxStatements 200k） | JsConditionEvaluator.cs:60-64 |
| 5 | Http `{{name}}` 替换来源正确、非 2xx 转为可重试失败 | VERIFIED | HttpStepExecutor.cs:46-80 |
| 6 | 租户隔离：跨租户无法读/写他人审批 | VERIFIED（TenantId 校验 + 仓储 HasQueryFilter） | ResolveApprovalCommandHandler.cs:39 / ListApprovalsQuery.cs:39 |

### 发现与修复
| 严重度 | 类别 | 文件:行 | 发现 | 修复 |
|--------|------|---------|------|------|
| P2 | 正确性/健壮性 | `WorkflowNode.SetResult` + `ResolveApprovalCommandHandler.cs:63` | 审批**空输入**批准时 `result=""` → `SetResult` 旧守卫 `ThrowIfNullOrWhiteSpace` 抛 `ArgumentException` → 端点 500；同一守卫还会使 HTTP 204（空响应体）在 `node.SetResult(result.Output ?? "")` 处崩溃 | 将 `SetResult` 守卫由 `ThrowIfNullOrWhiteSpace` 放宽为 `ThrowIfNull`（空串属合法完成态），同时消解编排器 `?? ""` 路径的潜在崩溃 |
| P3 | 取消语义 | `DelayStepExecutor.cs:41-44` | `Task.Delay` 被取消时仍返回 `Success`，可能掩盖真实的外部取消 | 仅当 `ct.IsCancellationRequested` 为真时上抛 `OperationCanceledException`（与编排器取消分类一致），否则返回可重试失败 |

### 测试覆盖
- 后端：`F20NodeExecutorsTests.cs`（7 类型全覆盖）、`OrchestrationPrimitiveTests.cs`（含 F7 bugfix6 回归）；全方案 330 测试全绿（SpecFlow 41 / Arch 9 / App 125 / Infra 123 / Api 27 / Integration 5）。
- 前端：`workflowCanvasStore.nodeTypes.test.ts`（7 扩展类型映射 + addNode 默认配置）；`node scripts/qa.mjs` OVERALL PASS（typecheck/lint/build/unit）。

### Top 3 运行时风险（已逐一核对，均非缺陷）
1. Jint 表达式逃逸沙箱 → 已确认默认不暴露 CLR，且 `TimeoutInterval`+`MaxStatements` 封顶无限循环。
2. HITL 恢复后重复触发审批 → 已确认 `SetResult` 置 `Completed` 使续跑跳过节点，且 `GetPendingByNodeAsync` 在旧审批已 Approved 时返回 null 不会重建。
3. Loop body 脱离循环上下文重复执行 → 已确认 `loopBodyIds` 主循环跳过 + 每轮 `Reset()`。

### Gate Status: **PASS**  [P0:0 | P1:0 | P2:0(已修) | P3:0(已修)]

## 2. ddd-phase-quality-gate（DDD 结构卫生）

12 类全扫（G1–G12），0 open findings：
- DI 注册完整：6 executor + `IConditionEvaluator` + `IHumanApprovalRepository` 全部注册于 `DependencyInjection.cs`。
- DDD 层：接口在 Abstractions/Domain，实现在 Infrastructure，全部 `internal sealed`。
- EF 映射同步：`HumanApproval` → `HumanApprovalConfiguration`（`ValueGeneratedNever`）+ DbSet + 迁移 `20260731042445_AddHumanApproval` + 快照。
- CancellationToken 透传：所有 executor / handler 均透传 `ct`。
- 并发：Blackboard 为 per-run 可变实例（非 Singleton）；Delay 30s 硬上限；Jint 2s/200k。
- null 守卫：所有公共方法 `ThrowIfNull`。
- 死代码：Loop 内联为设计（无 `LoopStepExecutor` 死代码）；无未引用实现类。
- XML 注释 / Swagger / 蓝图漂移：F20 §0–§6 与实际实现一致。
- 完整清单嵌入 `features/node-bundle.md` §「Phase Quality Gate Checklist（F20 · 节点全家桶）」。

## 3. codebase-optimizer（全库多维度健康检查 · F20 增量聚焦）

运行模式：自动化（聚焦 F20 增量，不重扫全库历史轮次）。七维扫描结论（聚焦 F20 改动 + 编排器引擎 + 前端节点组件）：

| 维度 | 结论 | 说明 |
|------|------|------|
| 架构 | PASS | executor 按 `HandlesType` 路由，无 switch 回归；依赖方向正确 |
| 代码质量 | PASS | `internal sealed` + null 守卫 + CT 透传 + 中文 XML 注释齐备 |
| 正确性 | PASS | 分支/循环/HITL 行为不变量全部 VERIFIED；已修空审批输入崩溃 |
| 测试 | PASS | F20NodeExecutorsTests / OrchestrationPrimitiveTests / nodeTypes.test.ts；后端 330 全绿，前端 qa.mjs OVERALL PASS |
| 性能 | PASS | Delay 30s 上限、Jint 2s/200k、Http 30s 超时、Loop body `Reset` 逐项；无 Singleton grow-only 集合；Blackboard per-run 实例 |
| 安全 | PASS | Jint 默认不暴露 CLR（沙箱）；HITL 全链路租户隔离；无硬编码密钥；`HttpClient` 命名客户端（无 socket 耗尽）；`{{}}` 仅做字符串替换（非 eval/SQL）；前端无 `dangerouslySetInnerHTML`/`eval`；无 `any` 泛滥 |
| 工程化 | PASS | 常量经具名常量/`Options`；EF 迁移落盘；DI 注册齐；i18n 对称 |
| 桩代码替换 | N/A | F20 无桩代码（6 executor 均为真实实现；SubWorkflow 真实触发独立 execution；UserInput 真实建审批+暂停恢复） |
| 生产就绪度 | P1→PASS | HITL、条件、循环、变量、子流、延迟均为生产可用实现 |

### Gate Status: **PASSED**（Round F20-01，0 open；后端 dotnet build 0/0 + dotnet test 330/330；前端 qa.mjs OVERALL PASS）

## 综合结论
三道质量门 **0 open findings**，`cleared: true`。根 `.quality-gate.json` 已更新并暂存，提交信息含 `Quality-Gate: f20-node-types cleared (0 open findings) [optimizer: PASSED]`。
