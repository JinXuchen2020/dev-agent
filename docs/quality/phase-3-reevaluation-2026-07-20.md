# Phase 3 代码重新评估（独立复核）— 2026-07-20

> 评估方法：不盲信 `phases/phase-3-platformization.md` 的"100% 完成 / 63/63"声明，独立重读高风险叙事模块源码，并以**实测**取证（构建 / 测试 / 前端构建 / 调用点 grep）。
> 评估人：AgentsOrchestrator（独立复核，非原实现者，非质量 skill 自动跑分）。

## 一、实测证据（非声称）

| 检查项 | 命令 / 方式 | 结果 |
|--------|------------|------|
| 全量后端构建 | `dotnet build src/AgentPlatform.sln` | **0 warnings, 0 errors**（"已成功生成"） |
| 全量后端测试 | `dotnet test src/AgentPlatform.sln` | **81/81 passed**（Arch 6 / App 31 / SpecFlow 41 / Integration 3） |
| 前端构建 | `npm run build`（tsc && vite build） | **成功**，exit 0；仅 bundle >500kB 警告 |
| SSE 发布链 | grep `PublishAsync` 调用点 | 5 个 EventHandler 真实调用 broadcaster（Started/Completed/RolledBack/StepCompleted/StepFailed） |
| OTel 指标注册 | grep `AddMeter` / `UseMiddleware` | 两个 Meter 均注册 + Prometheus exporter + Middleware 已挂载 |
| DI 生命周期 | grep `AddSingleton`/`AddScoped`/`AddHostedService` | broadcaster=Singleton、cleanupJob=HostedService、yamlParser=Singleton、dbInit=Scoped，均符合文档 §4/§7 |
| 控制器分层 | 读 `AgentRolesController` | 全程走 `IMediator.Send`，无直连仓储/DbContext |

> 注：文档称 "63/63 测试通过"，实际 **81/81**——Phase 2 新增 18 个 OrchestrationPrimitive 测试所致，文档数字已陈旧（非缺陷，仅漂移）。

## 二、缺陷发现（按严重度）

### 🔴 P1 — SSE 订阅内存泄漏未真正修复（行为缺陷，skills 漏报）

**位置**：`src/AgentPlatform.Api/Controllers/WorkflowProgressController.cs:51,53-74`

**现象**：
- 第 51 行 `var (_, reader) = _broadcaster.Subscribe(id);` —— **用 `_` 丢弃了 subscriberId**。
- 断连分支 `catch (OperationCanceledException)` 只做"优雅清理"注释，**从不调用 `Unsubscribe`**。
- 正常完成分支（`break`）同样**不调用 `Unsubscribe`**。
- 全仓 grep `Unsubscribe`：**零调用点**（仅接口声明 + 实现 + XML 注释）。`Unsubscribe` 是死代码。

**后果**：客户端断连或工作流完成后，其 `Channel<ExecutionProgressEvent>` 永远留在 Singleton 的 `_channels` 字典中（每个 orphan 通道最多缓冲 256 个事件）。`PublishAsync` 的"死通道清理"仅在 writer 已完成时触发，而 writer 仅在 `Unsubscribe`/`Dispose` 中 `TryComplete`——因 `Unsubscribe` 永不调用，writer 永不完成，通道永不移除。**内存随 SSE 连接数持续增长，直到进程重启。**

**与文档矛盾**：`phases/phase-3-platformization.md` "DDD 对抗性代码审查修复记录" 声称 P1 已修复——"Subscribe 返回 (Guid, ChannelReader)；新增 Unsubscribe 方法"。接口与实现确实补了 `Unsubscribe`，但**消费者（controller）从未调用**，泄漏依旧。属于典型的"名不副实现 / 半截修复"，正是 `ddd-phase-quality-gate`（仅查结构）与匆匆跑过的 `ddd-code-reviewer` 都漏掉、人工独立审查可读出的行为缺陷。

**对应文档自身的高风险预测**：§"Phase 3 High-Risk Predictions #1"（SSE 重连产生重复订阅者）—— 该预测已兑现为真实泄漏。

**修复方向**（待用户确认后实施）：
```csharp
var (subscriberId, reader) = _broadcaster.Subscribe(id);
try { /* ... ReadAllAsync ... */ }
catch (OperationCanceledException) { /* client disconnected */ }
finally { _broadcaster.Unsubscribe(id, subscriberId); }
```

---

### 🟡 P3 — `workflow.active_steps` 是死指标（空转）

**位置**：`src/AgentPlatform.Application/Diagnostics/WorkflowMetrics.cs:27`（`ActiveStepsHistogram`）

**现象**：该 Histogram 被声明（`Meter.CreateHistogram<double>("workflow.active_steps", ...)`），但全仓 grep 无任何 `.Record(...)` 调用点。其余 7 个指标（api.requests.total / api.errors.total / api.request.duration_ms / workflow.step.duration_ms / workflow.completed.total / model.call.total / model.call.duration_ms）均有真实 Record/Add 调用点，**非空转**。

**后果**：Grafana 面板若引用 `workflow.active_steps` 将永远无数据。蓝图 §8.1 的"活跃步骤数"观测项未真正落地。

**修复方向**：在状态机步进时 `ActiveStepsHistogram.Record(当前活跃步数)`；或若暂不打算实现，删除该声明以避免误导。

---

### 🟡 P3 — Phase 3 文档与代码漂移（多处）

1. **`WorkflowStateMachineEngine` 已整体废弃**：当前是 `[Obsolete]` 空壳（构造函数 No-op，逻辑已迁至 `OrchestrationPrimitive`）。但文档"DDD 对抗性代码审查修复记录"仍描述其 `RetryAsync`/`RollbackAsync`→`NotSupportedException` 的**旧状态**，与实际文件不符。
2. **测试计数陈旧**：文档 Final Regression 写 "63/63"，实际 81/81（见上）。
3. **Module 5/6 的"建议补 reviewer"未闭环**：文档 §0 自承 "Module 2 SSE / Module 5 React Flow / Module 6 OTel 此前仅过 quality-gate，建议补一轮 reviewer"，但本报告独立审查即在 SSE（Module 2）发现 P1，说明该补审从未真正发生（至少 SSE 部分）。

---

### 🟡 P3 — 前端 bundle 体积 & Module 5 行为未验

- 前端 `npm run build` 成功，但产物 `index-CUUE6noG.js` 达 **1.34MB**（>500kB 警告）。建议按需 `import()` 代码分割。
- **Module 5（React Flow 拖拽编辑器"真连通状态机"）的行为正确性无法在本环境验证**（需浏览器运行时）。文档本身已将其列为 reviewer 缺口，本报告未额外验证——建议后续补一轮 `ddd-code-reviewer` + 浏览器 E2E。

---

## 三、复核结论

| 维度 | 结论 |
|------|------|
| 构建 / 测试 | ✅ 真实全绿（build 0 warning，test 81/81，前端 build 成功） |
| SSE 发布链 | ✅ 真实（5 个 handler 发布事件） |
| OTel 指标 | ✅ 7/8 指标真实埋点；1 个死指标（P3） |
| DI / 分层 / 控制器 | ✅ 符合 DDD 与文档约定 |
| **SSE 订阅清理** | 🔴 **P1 未修复**（文档声明已修复，实则不漏的是接口而非行为） |
| 文档准确性 | 🟡 多处漂移（状态机废弃、测试数、未闭环的补审） |

**质量门判定：NOT CLEARED（1 × P1 开放）**。

> 教训复现：与 Phase 2 一致——`ddd-phase-quality-gate` 查结构、`ddd-code-reviewer` 匆匆跑分时，对"接口/方法签名已存在但调用方未真正使用"的半截修复不敏感。验证"修复是否真的端到端生效"必须靠**独立读调用方 + 测调用点**，而非只看被改文件本身。

## 四、建议下一步

1. **（必修）修 P1**：`WorkflowProgressController` 捕获 subscriberId + `finally` 中 `Unsubscribe`；补一个 SSE 断连清理的集成测试（断言 `_channels` 条目归零或 `Unsubscribe` 被调用）。
2. **（建议）修 P3**：删除或真正 Record `workflow.active_steps`；前端代码分割；更新 Phase 3 文档（状态机废弃、测试数、补审状态）。
3. **（建议）补 Module 5/6 reviewer**：React Flow 编辑器与 OTel 指标补一轮 `ddd-code-reviewer`，重点验证"拖拽保存/执行真连通状态机"与"指标非空转"。
