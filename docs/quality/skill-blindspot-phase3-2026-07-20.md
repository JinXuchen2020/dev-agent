# Skill 盲点分析（Phase 3）— 为什么两个 quality skill 没抓到 SSE 订阅内存泄漏

> 对应发现：`docs/quality/phase-3-reevaluation-2026-07-20.md` 的 P1 — `WorkflowProgressController.cs:51` 用 `var (_, reader)` 丢弃 `subscriberId`，`catch` 与 `break` 两条路径都不调 `Unsubscribe`，导致 `Unsubscribe` 全仓零调用（死方法），Singleton 的 `_channels` 字典随连接数永久增长。
>
> 本文回答：“为什么 `ddd-code-reviewer` 和 `ddd-phase-quality-gate` 没发现它？”

---

## 0. 一句话结论

两个 skill 都是**符号/结构级 + 测试绿即信**的检查器。它们能验证“`Unsubscribe` 方法存在、接口声明了、类用了 `ConcurrentDictionary` 线程安全”，但**从不追踪调用图去确认“消费者在每条退出路径上都真正调用了它”**。泄漏是一个**接线缺口（wiring gap）**，不是符号缺口（symbol gap）——而两类 skill 的死代码检查都只到**类（class）级别**，到不了**方法/调用点（method/call-site）级别**。

更重要的是：**我 Phase 2 给 skill 加的行为不变量只覆盖了“状态机”这一类缺陷**（resume 连续性、pause 响应、retry 次数、rollback 精度）。Phase 3 这个 SSE 泄漏属于**“资源获取/释放对称性”这一类完全不同的缺陷**，连加固后的 skill 也没覆盖。所以这不是“老盲点没修”，而是**加固本身的范围盲区**——证明单纯给状态机加不变量不足以泛化到全生命周期资源管理。

---

## 1. 缺陷回顾（证据确凿）

```csharp
// WorkflowProgressController.cs:51
var (_, reader) = _broadcaster.Subscribe(id);   // ← subscriberId 被 _ 丢弃

try { await foreach (var evt in reader.ReadAllAsync(ct)) { /* ... */ if (terminal) break; } }
catch (OperationCanceledException) { /* Client disconnected — graceful cleanup */ }  // ← 空 catch，从不 Unsubscribe
// break / catch 两条退出路径都不调 _broadcaster.Unsubscribe(subscriberId)
```

- 全仓 grep `Unsubscribe`：**零调用点**（仅接口声明 + 实现 + XML 注释）。
- `IExecutionProgressBroadcaster` 注册为 **Singleton**（`DependencyInjection.cs:169`）→ 字典与进程同寿 → 泄漏永久累积直到重启。

---

## 2. 逐条根因（每条锚定 skill 真实检查项）

### 根因 1 — 检查“方法存在”即视为“能力已交付”，不查调用图

- `ddd-phase-quality-gate` 的审计项 **Concurrency Risks** 原文：`static/Singleton with mutable state without lock/ConcurrentXxx`。
  - skill 看到 `_channels` 是 `ConcurrentDictionary` → **打勾通过**。它只验证“访问线程安全”，**从不问“有没有代码路径从字典里移除条目”**。
- `ddd-code-reviewer` 的 **Section Z / Wiring vs dead code** 原文：`For every public/registered class, confirm it is reachable from a registered DI type or a real call site. An unreferenced implementation class is DEAD CODE`。
  - `Unsubscribe` 是**被引用类上的一个方法**，不是“未被引用的实现类” → 不触发死代码判定。

→ 两者都把“符号存在”当成“能力已交付”。泄漏是**接线缺口**，恰恰是它们不查的维度。

### 根因 2 — 死代码检测是“类级”，不是“方法级”

- 两个 skill 的 dead-code 检查对象都是**实现类**（蜜罐 / 未引用类）。
- `Unsubscribe` 是**一个声明在接口上、实现在类里、但任何调用方都不 invoke 的方法**。方法级死代码在两个 skill 的检查项里**完全没有对应行**。
- 结果：一个堂而皇之的“已实现的清理方法”从未被调用，但 structural gate 与 code reviewer 都看不到——因为它“看起来”是完整实现。

### 根因 3 — 任何检查表都没有“获取/释放对称性”不变量

- `ddd-code-reviewer` Step 3.6 **Behavioral Invariant Tracing** 列出的 5 条不变量是：
  1. resume 跳过已完成步骤
  2. pause 是否观察外部状态
  3. retry 实际次数
  4. 审批/校验方法是否基于真实输入分支
  5. placeholder 字段是否被填充
  - **没有一条**涉及“每条退出路径（happy / 异常 / 取消）是否释放了获取的订阅/连接/句柄”。
- `ddd-phase-quality-gate` 的 **general-checklist §7 Concurrency and Lifecycle** 行为项：`Pause/Cancel 被循环观察`、`retry 边界用 < 非 <=`、`placeholder 字段非空`、`审批基于真实输入` —— 同样是状态机语义，**无资源生命周期条目**。
- 资源获取到释放的对称（Subscribe↔Unsubscribe、Open↔Close、Acquire↔Release、Allocate↔Dispose）是所有长连接/流式系统最常见的泄漏源，但**没有 skill 为它设行**。

### 根因 4 — `var (_, reader)` 的丢弃模式不可见

- `_` 丢弃是“我故意忽略返回值”的信号。这里被忽略的正是释放所需的句柄 `subscriberId`。
- 两个 skill 都没有“**被丢弃的返回值若是对应的释放句柄，则报警**”的检查。reviewer 扫到 `_broadcaster.Subscribe(id)` 被使用就略过，丢弃的 `subscriberId` 被 `_` 掩埋。

### 根因 5 — 绿测试 = 假信心（Phase 2 的元教训仍未强制执行）

- `ddd-code-reviewer` Step 4 Test Coverage 是**场景驱动**的：它追踪 `.feature` 文件里的 Scenario。SSE 流式端点**没有 SpecFlow 场景** → 没有可追踪的对象 → 断开清理路径的测试缺口**不可见**，除非 reviewer 主动问“断开清理有没有测试”。
- skill 没有一条强制“对长生命周期/流式资源，必须存在断开/取消/异常退出路径的测试”。
- 81/81 全绿反而让 reviewer 默认“正确”，掩盖了覆盖缺口。

### 根因 6 — “Singleton × 只增不减的字典”跨维度推断缺失

- skill **知道** broadcaster 是 Singleton（Concurrency Risks 项），**知道** `_channels` 是可变共享状态。
- 但它**从不在一次推理里组合**：“Singleton（进程级生命） + 字典只增不减（无移除路径） = 永久内存泄漏”。
- 它把每个属性**孤立**检查，每个都 PASS，于是组合后的泄漏被放行。这是典型的“属性级检查看不到系统级后果”。

### 根因 7 — 模块分类把 SSE 端点误导向了错误的检查表

- `WorkflowProgressController` 被 `ddd-code-reviewer` Step 2 路由到 **Section G（API Controller / Endpoint）**。
- Section G 只查：MediatR 用法、Validation、`[Required]`、Error Handling。**完全没有资源生命周期检查**。
- 一个 SSE 端点在结构上是个 API Controller，在行为上是个**连接/订阅生命周期管理器**。skill 的模块分类法里**没有“流式/长连接/订阅生命周期”这一桶**，所以相关不变量从未被选中。

---

## 3. 与 Phase 2 盲点分析的关系

| 维度 | Phase 2 盲点（状态机） | Phase 3 盲点（资源生命周期） |
|---|---|---|
| 缺陷类 | resume 重跑已完成、pause 无效、retry 差一 | 订阅获取后从不释放 |
| 上轮加固覆盖？ | ✅ 已加 Section A 状态机不变量 | ❌ **未覆盖**——加固范围盲区 |
| 共同根因 | 符号级检查看不到接线/行为缺口 | 同左，且新增“方法级死代码”+“丢弃返回值”盲点 |
| 共同元教训 | 绿测试 ≠ 正确；必须独立读调用方 + grep 调用点 | 同左，且需“获取/释放对称性”不变量 |

**结论**：Phase 2 的加固是必要但不充分的——它把“状态机行为”这一类补上了，但“资源生命周期”这一类（更常见于真实系统的 SSE/WebSocket/IDisposable/Channel 订阅）仍是空白。这解释了为什么我亲自独立读调用方 + grep `Unsubscribe` 时才发现，而两个 skill 跑分都没抓到。

---

## 4. 提议的 skill 加固（待用户确认后落地）

针对根因 1–7，在两个 skill 各加一类**资源生命周期不变量**：

### 4.1 `ddd-code-reviewer` — 新增 Section H2（Resource Lifecycle / Acquire-Release Symmetry）
- **获取/释放对称（P1）**：对每个 `Subscribe`/`Open`/`Acquire`/`Allocate`/`Create` 调用，必须存在对应的 `Unsubscribe`/`Close`/`Release`/`Dispose`/`Remove`，且覆盖**所有退出路径**（happy / `catch` / `break` / `CancellationToken` 取消）。应由 `finally` 或 `using` 保证。缺失任一条路径 → P1。
- **方法级死代码（P2）**：对接口上声明、但 grep 全仓**零调用点**的清理方法（如 `Unsubscribe`），判定为死代码——不是“未引用类”，而是“未被调用的清理 API”。要求或在 `finally` 中接线，或标记 `[Obsolete]`。
- **丢弃返回值检查（P2）**：`var (_, x) = Acquire()` 形式丢弃的返回值，若该类存在对应的释放方法且需要该返回值作句柄 → 报警。
- **Singleton × 只增集合推理（P1）**：若服务为 Singleton 且持有 `Dictionary`/`List`/`ConcurrentDictionary` 等只增不减的集合，且无任何移除路径 → 标记“疑似永久泄漏”，除非证明有上限/定期清理。

### 4.2 `ddd-phase-quality-gate`
- **general-checklist §7** 增加行为行：`Behavioral: 每个 Subscribe/Open/Acquire 都有匹配的 Unsubscribe/Close/Release 在所有退出路径（finally/using）— 缺失即 P1`。
- **Audit Categories** 扩展 `Dead Code` 为：`class-level 未引用类` **+** `method-level 已声明但零调用的清理/释放 API`。
- **Concurrency Risks** 文案从“无锁即报”改为“无锁或**无移除路径**即报”，明确包含生命周期泄漏。

### 4.3 强制的测试缺口检查（覆盖根因 5）
- 任一长生命周期/流式资源（SSE、WebSocket、Channel、IDisposable、Timer、HostedService 持有状态）模块，**必须存在断开/取消/异常退出路径的测试**，否则测试覆盖判为缺口（P2）。

---

## 5. 元教训（与 Phase 2 一致，且更强）

> **“方法存在”≠“能力已交付”。** 验证一个修复/特性是否端到端生效，必须**独立读调用方 + grep 调用点**，不能只看被改文件本身或接口声明。结构门禁（查符号/层/生命周期）和匆匆跑分的 reviewer（信任绿测试）都对“半截修复 / 接线缺口”天然不敏感——这正是 AI 能发现、而当前两个 skill 漏报的重合区。

修复本 P1 的方案见 `phase-3-reevaluation-2026-07-20.md`：捕获 `subscriberId` + `finally` 中 `Unsubscribe` + 补一个 SSE 断连清理测试。skill 加固（§4）与代码修复（P1）建议分开提交。
