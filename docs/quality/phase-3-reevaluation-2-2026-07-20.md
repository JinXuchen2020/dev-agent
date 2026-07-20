# Phase 3 复评（修复验证）— 2026-07-20

> 对照 `phase-3-reevaluation-2026-07-20.md`（当日首评，结论 NOT CLEARED / 1×P1 开放），
> 验证所报问题是否已在代码中修复。方法同 `quality-reeval-hardening` Phase A：实读源码 + grep 调用点 + 跑构建/测试取证，不盲信文档。

## 实测证据（全绿，非声称）
- `dotnet build src/AgentPlatform.sln` → **0 warning / 0 error**
- `dotnet test src/AgentPlatform.sln` → **81/81 通过**（Arch 6 + App 31 + SpecFlow 41 + Integration 3）
- `grep -rn "Unsubscribe" src --include=*.cs` → 唯一真实调用点在 `WorkflowProgressController.cs:77`（接口声明 + 实现 + 该调用点；`bin/obj` 的 xml 为生成文档，忽略）
- `grep -rn "ActiveStepsHistogram" src --include=*.cs` → `OrchestrationPrimitive.cs:392` 与 `:417` 各一处 `Record(...)`

## 首评问题逐条核对

| # | 首评问题 | 当前状态 | 证据 |
|---|---|---|---|
| P1 | SSE 订阅内存泄漏（`var (_, reader)` 丢弃 subscriberId，断连/完成不清理） | ✅ **已修复** | `WorkflowProgressController.cs:51` 捕获 `subscriberId`；`:74-78` `finally { _broadcaster.Unsubscribe(id, subscriberId); }` 覆盖 happy-break / 断连(catch) / 任意异常三条退出路径；`Unsubscribe` 在消费者侧有真实调用点（不再死代码） |
| P3-① | `workflow.active_steps`(ActiveStepsHistogram) 声明但从未 Record（死指标） | ✅ **已修复** | `OrchestrationPrimitive.cs:392 Record(1,…)`、`:417 Record(0,…)` 真实埋点 |
| P3-② | 文档漂移：`WorkflowStateMachineEngine` 已 `[Obsolete]` 空壳、测试数陈旧、Module 5/6 "建议补 reviewer" 未闭环 | ⚠️ **仍存在（文档性，非阻塞）** | 引擎确为废弃空壳（逻辑迁 OrchestrationPrimitive），功能无影响；文档描述滞后 |
| P3-③ | 前端 bundle 1.34MB 警告；Module 5 拖拽"真连通状态机"未验 | ⚠️ **仍存在（非阻塞）** | 需浏览器手验，本环境未覆盖 |

## 新发现的 P2 缺口（首评时未强调）
- **SSE 修复无回归测试锁定**：`grep` 全仓 `*.cs` 测试文件，无任何测试引用 `Unsubscribe` / `StreamProgress` / `Channel` / `ReadAllAsync`。即 P1 的修复是"对的"但**没有被自动化测试保护**——一旦有人把 `finally` 改回 `break` 不清理，CI 不会报警。这与刚加固的 `ddd-code-reviewer` Section H2「Long-lived resource test coverage (P2)」要求直接冲突。建议补一个测试：订阅→模拟断连(cancel)→断言 `_channels` 中该 subscriber 已移除。

## 版本控制状态（重要）
`git status --short` 显示以下文件**均为未提交修改（` M`）**：
```
 M phases/phase-3-platformization.md
 M src/AgentPlatform.Api/Controllers/WorkflowProgressController.cs   ← P1 修复
 M src/AgentPlatform.Infrastructure/Workflows/OrchestrationPrimitive.cs  ← P3 修复(+其他)
 M src/AgentPlatform.Api/Controllers/WorkflowsController.cs
 M src/AgentPlatform.Application/Workflows/Commands/RunWorkflow/RunWorkflowCommand.cs
 M src/AgentPlatform.Application/Workflows/Commands/RunWorkflow/RunWorkflowCommandHandler.cs
 M src/AgentPlatform.Web/src/pages/WorkflowDetailPage.tsx
 M src/AgentPlatform.Web/src/pages/WorkflowEditorPage.tsx
 M src/AgentPlatform.Web/src/services/api.ts
```
- 说明：这些修复发生在上一轮会话之外（用户或他人已改代码），目前只在工作区，**尚未进版本库**。
- `.quality-gate.json` 仍指向 `phase-2 / cleared:true` —— 未为 phase-3 更新。当前若直接 `git commit` 这些 `src/` 改动，A+B 钩子会放行（因 `cleared:true` 仍存在），但门本身是 phase-2 的，语义不匹配。**正确做法**：phase-3 门应重新出据 + 更新 `.quality-gate.json` 到 phase-3（P1 已 resolved，仅余 1 P2 测试缺口）后再提交。

## 结论
- **代码层**：首评的 P1（SSE 泄漏）与 P3-①（死指标）**确已修复**，且有构建/测试/调用点 grep 三重证据。质量门对"代码正确性"可视为 **phase-3 CLEARED**。
- **残余项**：
  1. **P2**：SSE 修复缺回归测试（需补）
  2. **P3**：文档漂移（phase-3 文档仍写"100%/63-63"、把 SSE 列为待 reviewer）—— 需同步更新
  3. **流程**：所有修复未提交 + 质量门未切到 phase-3
- **本次仅做复评，未修改任何代码、未提交**（用户仅要求"看看是否已修复"）。
