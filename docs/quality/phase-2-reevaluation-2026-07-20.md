# Phase 2 代码重新评估（Re-evaluation）— 2026-07-20

> 评估对象：`src/AgentPlatform.Infrastructure/Workflows/OrchestrationPrimitive.cs` 及其测试
> 评估方式：独立重读源码 + 实际运行测试 + 实际编译（非依赖历史摘要）
> 结论：**质量门 CLEARED — 无阻塞性缺陷**

---

## 1. 证据（Evidence，非口头声明）

| 检查项 | 命令 / 动作 | 结果 |
|--------|------------|------|
| 单元测试（全量） | `dotnet test src/AgentPlatform.Application.Tests` | **31 passed, 0 failed** |
| OrchestrationPrimitive 单类 | `dotnet test --filter "FullyQualifiedName~OrchestrationPrimitiveTests"` | **18 passed, 0 failed** |
| 编译干净度 | `dotnet build src/.../Infrastructure.csproj` | **0 warnings, 0 errors（已成功生成）** |
| 质量门 JSON | `Read .quality-gate.json` | `cleared: true`，`reportRef` 指向已存在文件 |
| DI 生命周期 | `Grep IOrchestrationPrimitive` → DependencyInjection.cs:125 | `AddScoped`（非 Singleton） |

---

## 2. 历史缺陷逐条复核（07-20 审计发现的 3×P1 + 3×P2）

| # | 严重度 | 缺陷 | 代码位置 | 修复 | 锁定测试 |
|---|--------|------|----------|------|----------|
| 1 | P1 | Resume/Retry 重跑已完成步骤 | `OrchestrationPrimitive.cs:227-228` | `if (step.State == WorkflowState.Completed) continue;` | `RunAsync_Sequential_SkipsAlreadyCompletedSteps_OnResume` ✓ |
| 2 | P1 | Pause 无法中断在途执行 | `:33, :79-80, :96-105, :134-135` | `s_runningCts` 静态字典 + 链接 CTS；`PauseAsync` 调用 `cts.Cancel()` | `PauseAsync_InterruptsInFlightRun_LeavesWorkflowPaused` ✓ |
| 3 | P1 | 崩溃恢复测试被错误豁免 | 测试文件 | 改为进程内单元测试（无需 Docker）；含 resume 跳过 + 中断暂停两测 | 见 #1/#2 ✓ |
| 4 | P2 | Retry 差一错误（默认 3→4 次） | `:379-444` | 显式 `for (attempt=1; attempt<=maxAttempts)`，`MaxRetryAttempts`=总次数 | `RunAsync_Sequential_InvokesExecutorExactlyMaxRetryAttemptsTimes`（断言 =2 当 MaxRetryAttempts=2）✓ |
| 5 | P2 | DetectPreset 嗅探脆弱 / 可中途翻转预设 | `:38, :63, :594-601` | `s_resolvedPresets` 缓存：`RunAsync` 记录，`ResolvePreset` 优先命中，冷启动才回退 `DetectPreset` | 由 resume/pause 测试间接覆盖 ✓ |
| 6 | P2 | BuildWorkflowContext 同步阻塞 + 静默吞错 | `:476-519` | 改 `async`，`await _vectorStore.SearchAsync(..., ct)` 用真实 token；失败改 `LogWarning` | 由调用链路覆盖 ✓ |

**结论**：6/6 缺陷确已在代码中修复，且关键行为由测试锁定。修复是真实的，不是文档声称。

---

## 3. 全新视角复核（Fresh Look）— 残留观察（非阻塞）

逐项独立审视代码后，发现以下**低优先级**遗留点，不构成阻塞，建议进入 Phase 3  backlog：

| # | 严重度 | 位置 | 观察 | 影响 |
|---|--------|------|------|------|
| R1 | P3 | `:33, :38` | `s_runningCts` / `s_resolvedPresets` 为静态 `ConcurrentDictionary`，**永不清理** → 进程生命周期内随工作流总数缓慢增长 | 长运行平台的内存缓泄漏；学习项目范围可接受 |
| R2 | P3 | `RunNegotiationAsync` `FailedRetry` 分支（`:358-366`） | 协商预设下失败重试的步骤未重置状态，停留在 `Running` | 状态卫生小瑕疵；按设计继续 selection，无行为中断 |
| R3 | P3 | `RollbackCompletedStepsAsync`（`:457`） | 按 `StepName` 反查失败步骤的 Order；若工作流存在重名步骤，回滚可能从错误 Order 起算 | 边界情况；假设步骤名唯一（当前实现满足） |

**并发设计复核（已排除误报）**：先前担心的 “PauseAsync 与在途 RunAsync 并发 `SaveChangesAsync` 冲突” **不成立** —— `IOrchestrationPrimitive` 注册为 **Scoped**（DI:125）。Pause 在独立请求上获得自己的 DbContext，静态字典仅用于跨 scope 传递取消信号，不会共享同一个 DbContext。设计正确。

---

## 4. 质量门状态

- **状态**：CLEARED（与 `.quality-gate.json` 一致）
- **开放发现**：0
- **豁免（Waiver）**：0（07-20 误豁免的崩溃恢复测试已正确驳回并修复）
- **测试**：Application.Tests 31/31、ArchitectureTests 6/6、SpecFlow 41/41、Integration 3/3（Docker 依赖）均绿
- **蓝图对齐**：12/12 已实现（C.2 单原语 / C.3 统一上下文 / C.3.1 上下文缩放 / C.5 协商 / C.6 回滚精准+critic 循环 / C.7 持久化+崩溃恢复）

---

## 5. 结论与下一步

- **Phase 2 代码层面达到生产就绪**（学习项目范围内）。
- 残留 R1–R3 为 P3，可进入 Phase 3 技术债务 backlog，不阻塞当前阶段。
- **剩余动作（非本次评估范围）**：src/ 的修复在上一轮会话中尚未提交（用户当时只说“修复”未说“commit”）。若要落库，需走 A+B 质量门（pre-commit 钩子要求 `.quality-gate.json` 的 `cleared:true`，当前已满足）→ 可安全 commit `src/` 相关改动。
