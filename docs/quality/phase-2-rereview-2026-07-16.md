# Phase 2 代码复评报告（2026-07-16 复评）

> 触发：用户要求"重新评估 phase 2 的代码"。
> 方法：`ddd-code-reviewer` 对抗式审查，逐行读 `OrchestrationPrimitive` 及相关模块，对照已修复的蓝图附录 C（DESIGN READY）。
> 范围：编排核心（单一原语 + `sequential`/`negotiation` 预设、统一 `WorkflowContext`、critic 循环、回滚/持久化）。

---

## 0. 一句话结论（修正了我自己早先的判断）

**我早先写的 `docs/blueprint-drift-postmortem.md` §6 说"Phase 2 旧代码仍实现旧设计、处于漂移态"——这条现在已不准确。**

复评发现：编排的**实时执行路径已经被重写为新蓝图设计**：

- 实时路径：`RunWorkflowCommandHandler` → `IOrchestrationPrimitive.RunAsync` → `OrchestrationPrimitive`
  （`src/AgentPlatform.Application/Workflows/Commands/RunWorkflow/RunWorkflowCommandHandler.cs:26`，DI `DependencyInjection.cs:125`）。
- 旧的 `AutoGenAgentOrchestrator`（蜜罐：名带 AutoGen 却手写 `IModelClient` 循环）**根本没注册**（`IAgentOrchestrator` 在 DI 中无任何注册行）→ 是死代码，不是实时路径。
- `WorkflowStateMachineEngine` / `IWorkflowEngine` / `StubWorkflowEngine` 已标 `[Obsolete]` 或注册为抛异常。

所以**A 类实现漂移（双引擎 / 内存态 / 全量重置 / 空心 stub）在新基线上已基本解决**。本复评真正要报的，是**新原语自身的功能缺陷**，以及我早先笔记的陈旧结论。

> ⚠️ 但这**不等于可以清质量门**。`src/` 仍不能 commit：新原语尚有 3 个 P1 + 3 个 P2 未修，`cleared:true` 现在写下去是撒谎。详见 §5。

---

## 1. Findings

| Severity | Category | File:Line | Finding | Evidence | Suggested Fix |
|----------|----------|-----------|---------|----------|---------------|
| **P1** | Resume/Retry 重跑全量 | `OrchestrationPrimitive.cs:100-114`(`ResumeAsync`)→`179-237`(`RunSequentialAsync`) | Resume 把状态置 Running 后直接 `RunAsync`→`RunSequentialAsync` **遍历所有步骤并逐一执行，不跳过已 Completed 的步骤**。已完成的步骤会被重新调 LLM、重新发 `StepCompleted`、重新追加 artifact。"Resume" = 重跑整个工作流。 | `RunSequentialAsync` 的 `foreach (var step in orderedSteps)` 对 Step.State 无任何判断，必然重执行。 | 执行前跳过 `State == Completed`（及 `Failed` 已处理过）的步骤；或 Resume/Retry 仅从目标步骤之后开始。 |
| **P1** | Pause 执行中无效 | `OrchestrationPrimitive.cs:88-98`(`PauseAsync`) vs `179-237` | `PauseAsync` 只把 DB 状态翻成 Paused；但 `RunSequentialAsync` 的循环**只检查 `ct`、从不读 `workflow.CurrentState`**。运行中调 Pause，循环在步骤之间不会停，除非调用方同时取消 token。Pause 是弱/竞态的。 | 循环体仅有 `ct.ThrowIfCancellationRequested()`，无 `if (workflow.CurrentState == Paused) { ...; return; }`。 | 循环每步开头读当前持久化状态（或注入一个可轮询的取消源），Paused 时落盘并退出。 |
| **P1** | Critic 是模拟器（诚实性/功能缺口） | `CriticStepExecutor.cs:44-55`；`CriticConvergenceTermination.cs:47` | `CriticStepExecutor` **永远 APPROVED**，从不调 `IModelClient`、无评审逻辑；`CriticConvergenceTermination` 读 `Blackboard["negotiation:converged"]=="true"`——而这个 true 正是假 critic 写的。于是**协商预设在第一轮 critic 后就收敛**。蓝图 C.6 的"critic/reflection 循环"结构上在、功能上是装饰。此外 `CriticReviewResult.ReworkTarget` **全代码无任何消费方**，"范围重做"路径是死的。 | `CriticStepExecutor.ExecuteAsync` 返回硬编码 `Approved=true`；`CriticReviewResult.ReworkTarget` 在仓库内 grep 无读取点。 | 要么接真实 critic（调 `IModelClient` + 评审 prompt，产出 ReworkTarget 并触发回滚），要么在代码/文档明确标注"critic 为占位实现，协商收敛为演示性质"。 |
| **P2** | 上下文伸缩缺失（C.3.1） | `OrchestrationPrimitive.cs:428-455` | `BuildWorkflowContext` 把**全部**已完成步骤结果塞进 `Artifacts`，但 `Retrieval = RetrievalContext.Empty`、`Summary = StepHistory.Empty`。无压缩/检索/摘要。长工作流 token 线性爆炸。蓝图 C.3.1 明确要求上下文伸缩。 | 两处 `.Empty` 直接赋值；无任何 summarization/truncation 逻辑。 | 实现 C.3.1：超阈值时对历史做摘要/检索召回，而非全量追加。 |
| **P2** | ReworkTarget 未接线 | `OrchestrationPrimitive.cs:404-424`(`RollbackCompletedStepsAsync` 忽略 `result.ReworkTarget`) | 失败时回滚到指定步骤 OK，但**从不读取 critic 的 ReworkTarget**，失败后工作流停留在 `RolledBack` 即停，没有任何机制重跑失败步骤。蓝图"精准回退到指定步骤并重跑"只做了一半。 | `RollbackCompletedStepsAsync` 签名只收 `failedStepName/errorDetail`，无 `reworkTarget` 参数；`CriticReviewResult.ReworkTarget` 无引用。 | 回滚后根据 `ReworkTarget` 定位步骤并自动重跑（接回 `RunAsync`）。 |
| **P2** | DetectPreset 脆弱且破坏 Resume | `OrchestrationPrimitive.cs:497-504`；`ResumeAsync:107/112`、`RetryStepAsync:129` | `DetectPreset` 用子串匹配 `workflow.Context` 是否含 `"\"preset\":\"negotiation\""`。格式稍有差异（空格/大小写）就静默退回 Sequential。而 `ResumeAsync`/`RetryStepAsync` **丢失了命令传入的显式 preset**，改用 `DetectPreset` 重推——一个 negotiation 工作流若 Context 不含该精确串，Resume 会变成 Sequential。 | `DetectPreset` 用 `Contains("\"preset\":\"negotiation\"")` 字符串比对；Resume/Retry 调用 `DetectPreset(workflow)` 而非沿用原 preset。 | 把 preset 存进 `Workflow` 聚合根（持久化），Resume/Retry 直接读聚合根，弃用字符串嗅探。 |
| **P3** | Retry 次数 off-by-one | `OrchestrationPrimitive.cs:331` | `while (retryCount <= maxRetries)` 且 `maxRetries` 默认 3 → **共执行 4 次**（首跑 + 3 重试），与蓝图"最多 3 次"不符。 | 循环条件 `<=`，初始 `retryCount=0` → 0,1,2,3 四轮。 | 改为 `< maxRetries`（或显式 `attempts = maxRetries+1` 并注释语义）。 |
| **P3** | 死蜜罐未标 Obsolete | `AutoGenAgentOrchestrator.cs:15` | `AutoGenAgentOrchestrator` 未注册、**也未标 `[Obsolete]`**（不同于已处理的 `WorkflowStateMachineEngine`）。它是"看着很真、其实没人用"的蜜罐，易误导后续开发者以为它是实时编排器。 | DI 中无 `AddScoped<IAgentOrchestrator,...>`；类无 `[Obsolete]`。 | 删除该类，或在类与 `IAgentOrchestrator` 上加 `[Obsolete("Replaced by IOrchestrationPrimitive")]`。 |
| **P3** | 双回滚路径语义冗余 | `RollbackToAsync:132-153` vs `RollbackCompletedStepsAsync:404-424` | 两个方法都做"Order>=target 重置为 Pending"，私有版额外发事件+置 `RolledBack`。语义重叠，易漂移。 | 两处 `Where(s => s.Order >= …)` 重置逻辑一致。 | 统一为一个内部实现，公开 `RollbackToAsync` 复用之。 |

---

## 2. Control Flow Analysis

- **Entry point (live):** `RunWorkflowCommandHandler.Handle` → `IOrchestrationPrimitive.RunAsync(workflow, preset, ct)`
- **Execution path (sequential):** `RunAsync` → `RunSequentialAsync` → `BuildWorkflowContext` → `ExecuteStepWithRetryAsync`(per step) → `ResolveExecutor` → `AgentCallStepExecutor.ExecuteAsync`(调 `IModelClient`) → persist + publish `StepCompleted` → loop.
- **Execution path (negotiation):** `RunAsync` → `RunNegotiationAsync` → `ITerminationCondition.ShouldTerminateAsync`(`CriticConvergenceTermination`) → `ISelectionStrategy.SelectNextAsync`(`RoleBasedSelectionStrategy`) → `ExecuteStepWithRetryAsync` → persist.
- **Dead ends:** `AutoGenAgentOrchestrator.RunCollaborationAsync` 无任何调用方（死代码）；`IWorkflowEngine` 注册即抛异常。
- **Unregistered interfaces:** `IAgentOrchestrator` 已无实现注册（旧编排器退役）。
- **DI 完整性：** `IOrchestrationPrimitive`/`IStepExecutor`×2/`ISelectionStrategy`/`ITerminationCondition` 均真实注册；`OrchestrationPrimitive` 为 Scoped（含 scoped `_repository`）→ 并发工作流各自独立，无共享可变态问题。✓

---

## 3. Test Coverage

- `OrchestrationPrimitiveTests.cs` 存在，含 negotiation 5 例 + sequential 多例（doc 称 73 passed）。
- **未覆盖路径：** Resume/Retry 的"重跑已完成步骤"行为（正是 P1 缺陷，测试用 `TestStateMachineEngine` 类测试件未触及）；Pause 执行中生效；Critic 模拟器恒 APPROVED 未被断言为"应是真实评审"。
- **测试桩：** SpecFlow `MultiAgentPipelineSteps` 用 `TestAgentOrchestrator : IAgentOrchestrator`（旧接口桩），**未覆盖新 `IOrchestrationPrimitive`**——意味着端到端测试仍绕开了新原语。

---

## 4. API / 外部库核对

- `IModelClient.ChatAsync(modelId, messages, ct)`：`AgentCallStepExecutor` 真实调用并消费 `response.Content` / `TokenUsage`。✓（非蜜罐）
- 无 AutoGen.NET 真实符号被使用（类名 `AutoGen*` 已退役），与"线性预设降为 negotiation 退化特例、不依赖群聊库"的设计一致。✓
- `JsonSerializer.Serialize` 已替代字符串拼接（修复 JSON 注入）。✓

---

## 5. Blueprint Alignment（对照已修复的附录 C）

- C.2 单一编排原语 + 预设：**已实现**（`OrchestrationPrimitive` + `OrchestrationPreset`）。✓
- C.3 统一 `WorkflowContext`：**已实现**（统一契约，灭掉双上下文）。✓
- C.3.1 上下文伸缩：**未实现**（见 P2）。✗
- C.5 协商预设真实 selection/termination：**已实现**（`RoleBasedSelectionStrategy` + `CriticConvergenceTermination` 均为真实实现，非 `SequentialGroupChatManager`）。✓
- C.6 critic 循环：**结构在、功能空**（见 P1 critic 模拟器）。⚠️
- C.6 精准回滚：**已实现**（`RollbackToAsync` / `RollbackCompletedStepsAsync` 均 `Order >= target`，且重置所有状态非仅 Completed）。✓
- C.7 逐步持久化恢复：**持久化已实现**（每步 `SaveChangesAsync`）；"抗进程重启恢复"由 Resume 从 DB 重建支撑（但 Resume 有 P1 重跑缺陷）。✓（部分）

---

## 6. Top 3 Runtime Risks

1. **Resume 重跑全量** — `OrchestrationPrimitive.cs:179-237` — 场景：工作流跑到第 5 步暂停后 Resume，第 1–4 步被重新调 LLM 并覆盖结果，产生重复 token 消耗 + 可能不一致的产物。
2. **Critic 恒通过致协商形同虚设** — `CriticStepExecutor.cs:44-55` — 场景：任何产物都被判 APPROVED，C.6 "质量闭环"不生效，坏产物直接流入下一轮。
3. **Pause 执行中不生效** — `OrchestrationPrimitive.cs:179-237` — 场景：长步骤（如调 LLM 120s）运行中发 Pause，要等当前步骤跑完才可能响应，无法真正中断。

---

## 7. 与既有评审记录的关系

- `phases/phase-2-multi-agent-checklist.md` 中 "🔧 Phase 2 ddd-code-reviewer (2026-07-16)"（L555-614）对 `OrchestrationPrimitive` 的判定 **PASS 基本成立**——其列的修复（精准回滚、JSON 注入、glob 匹配、状态一致性、step 持久化）在代码中均真实存在，本次复评已逐条核对确认。
- 但**该评审未覆盖**本次复评新发现的 P1（Resume/Retry 重跑、Pause 无效、Critic 模拟器）与 P2（C.3.1、ReworkTarget 接线、DetectPreset）。这些是新原语自己的缺陷，不是旧漂移的残留。
- `docs/blueprint-drift-postmortem.md` §6 "Phase 2 旧代码仍实现旧设计（漂移态）"——**已过时**，本条复评已纠正：实时路径已重写，A 类漂移解决；待办改为"修新原语的 P1/P2（见上）"。

---

## 8. 质量门状态

**NOT CLEARED。** 现有未清 `src/` 改动含 3×P1 + 3×P2（加 doc 留的 Critic 模拟 waiver），不满足 `.quality-gate.json cleared:true`。
→ 在修完 §1 的 P1/P2 并补测试前，不得写 `cleared:true`，也不得 commit `src/`。

---

## 9. 建议修复顺序（若用户授权）

1. **P1 Resume/Retry 重跑**：`RunSequentialAsync` 执行前跳过 `Completed` 步骤（最小改动，先止血）。
2. **P1 Pause 生效**：循环读持久化状态，Paused 即落盘退出。
3. **P1 Critic**：接真实评审 或 明确标注占位（二选一，不能留"看似生效实则恒通过"）。
4. **P2 C.3.1 / ReworkTarget / DetectPreset / 死蜜罐 / off-by-one**：排期处理。
5. 修完重跑 `ddd-code-reviewer` + 补测试 → 写 `.quality-gate.json`（`cleared:true`）→ 方可 commit `src/`。
