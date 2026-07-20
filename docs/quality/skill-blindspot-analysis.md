# 为什么两个 quality skill 漏了这些问题，而手动复评能抓到

> 学习项目复盘。结论先说：**不是 skill "笨"，是 skill 的检查清单粒度太粗 + 模块路由把关键检查项排除了。**
> 手动复评做的事，恰恰是 `ddd-code-reviewer` 的 SKILL.md **明令要求**但 `review-checklist.md` 没有强制到位的那一步——对控制流做"语义级追踪、拿蓝图意图当不变量去证伪"。

## 0. 一句话根因

两个 skill 是**结构/活动/能力/风险扫描器**（看"有没有、做没做、能力在不在、已知风险类是否出现"）；
它们**不验证"行为是否符合契约/蓝图意图"**（证伪级语义正确性）。

9 个 findings 全部属于后者，所以清单上能"勾选通过"，实际是漏的。

## 1. 证据：skill 实际抓到 / 没抓到什么

### 1.1 它其实抓到了 Critic 模拟器 —— 但被"豁免"了
`phases/phase-2-multi-agent-checklist.md` L555-614 的 reviewer 报告：
- L575 / L594 两条 **Waivers**：`Critic Simulation — Critic always approves ("Artifact meets quality standards.") with no real review logic`
- 豁免理由写的是 "Requires production critic agent implementation... out of scope for Phase 2 structural fix"

→ **抓到了，但判了豁免。** 而 `ddd-code-reviewer` 的 Auto-Fix Rule（SKILL.md L119-128）明确：只有"需要用户做结构性决策"才能豁免；"生产 critic 还没写"是**未完工**，不是结构性决策，**本不该豁免**。这是**豁免误判（流程问题）**，不是 skill 盲区。

### 1.2 它没跑 Section A —— 关键运行时正确性检查被排除
报告 L557：`Mode: ddd-code-reviewer (Section C + Section Z)`。
但 reviewer 的 Workflow（SKILL.md L34-48）要求 "Run ALL applicable sections"；
而 `OrchestrationPrimitive` **本质就是个状态机**，应同时触发 Section A。

Section A 里恰好有能抓到 resume/pause/crash 的检查项：
- L36：*"Can the workflow resume from the last completed step?"*
- L37：*"Require an integration test that kills and restarts the host mid-workflow and asserts resume."*

→ 因为路由到 Section C，**Section A 的 resume/crash 断言根本没执行**。这是**模块路由漏判（系统性缺口）**。

## 2. 9 个 finding 逐一映射：哪个检查项该抓、为什么没抓

| # | Finding | 应在哪抓 | 该检查项现状 | 为什么漏 |
|---|---------|----------|--------------|----------|
| P1-1 | Resume/Retry 重跑**全部**步骤（不跳 Completed） | Section A L36 "resume from last completed step" | 问"能力在不在"，没问"resume 是否重跑已完成步骤" | 检查项问**能力**，不问**正确性**；且 A 没跑 |
| P1-2 | Pause 执行中无效（循环只读 `ct`，从不读 `CurrentState`） | Section A L35 "check CancellationToken at each step" | 只问"接没接 CT、每步查没查"，不问"外部 Pause 状态变更能否中断循环" | 检查项锁定在**token**维度，没覆盖**状态维度** |
| P1-3 | Critic 恒通过模拟器（`Approved=true` 硬编码） | Section C L97 "hollow interface-method check" | 定义 stub = "only logs and returns (no state mutation)"；Critic 做了 `Blackboard.Set`+返回，算"非平凡工作" | 检查项把**活动**等同于**有效**；模拟器"做了事"就不算 stub |
| P2-1 | 上下文伸缩 C.3.1 没做（`Retrieval/Summary=.Empty`） | Section C L85-88 "Context Propagation" | 问"会不会超 token、有没有截断"，不问"伸缩机制本身是空占位还是真实现" | 检查项探测**风险**，不问**特性是否落地** |
| P2-2 | `ReworkTarget` 没接线（永远 null，循环不消费） | 无对应项 | 蓝图 C.6 说 critic 指定重做目标；清单无"critic 输出是否被消费"检查 | 清单无此不变量 |
| P2-3 | `DetectPreset` 字符串嗅探脆弱且破坏 Resume 的 preset | 无对应项 | 清单无"preset 检测鲁棒性"检查 | 清单无此不变量 |
| P3-1 | Retry off-by-one（`<= maxRetries` 跑 N+1 次） | Section A L17 "what is the max retry count" | 只问"最大次数是多少、配置还是硬编码"，不问"实际次数 == 配置值（无 off-by-one）" | 检查项要**报数**，不要求**等于预期** |
| P3-2 | 死蜜罐 `AutoGenAgentOrchestrator` 没标 `[Obsolete]` | （此前已加，见 L566） | 已修 | — |
| P3-3 | 双回滚路径冗余 | 无对应项 | 清单无"回滚路径唯一性"检查 | 清单无此不变量 |

**共同模式**：P1-1/2/3、P2-1/2/3、P3-1 全部是 **"行为是否符合契约/蓝图意图"** 类缺陷；
而 skill 的检查项是 **"符号/能力/风险类"** 粒度，二者错位。

## 3. 为什么手动复评能抓到 —— 我做了什么 skill 没强制做的事

1. **读真实控制流并拿蓝图当不变量证伪**
   - 读 `RunSequentialAsync` L179：`foreach (step in orderedSteps)` 无 `s.State == Completed` 跳过 → resume 重跑全部。
   - 读循环体：只 `ct.ThrowIfCancellationRequested()`，从不读 `workflow.CurrentState` → Pause 执行中无效。
2. **打开方法体看语义，不只看签名**
   - `CriticStepExecutor.ExecuteAsync` L50：`Approved = true` 硬编码 → 恒通过模拟器（不是"log+return"型 stub，而是"做假动作"型 stub）。
3. **数循环边界**
   - `while (retryCount <= maxRetries)` → 跑 `maxRetries+1` 次，off-by-one。
4. **追占位字段是否被填充**
   - `BuildWorkflowContext` L451-452：`Retrieval = RetrievalContext.Empty` / `Summary = StepHistory.Empty` → C.3.1 未实现。

注意：第 1-4 步**正是 `ddd-code-reviewer` SKILL.md 要求的**（L12 "证明它不工作"、L50-59 Step3 控制流追踪、L87-95 Step7 运行时风险、Section A L36-37）。
**差异不在能力，在强制粒度**：SKILL.md 是"软指令"，`review-checklist.md` 的勾选项是"粗粒度"，agent 勾完"✅ ResumeAsync 存在、加载 workflow、调 RunAsync"就当过了，不会自动下钻到"RunAsync 是否重跑已完成步骤"。

## 4. 根因归类（两类 + 一例误判）

- **(A) 检查清单粒度缺口（系统性）**：具体检查项是"存在/活动/能力/风险"级，缺"行为正确性/不变量"级。即使 agent 认真勾选也能漏。
- **(B) 模块路由漏判（系统性）**：`OrchestrationPrimitive` 是状态机，却只路由到 Section C，Section A 的 resume/crash 断言未执行。
- **(C) 豁免误判（流程）**：Critic 模拟器被抓到却以"out of scope / structural decision"豁免，违反 Auto-Fix Rule（未完工 ≠ 结构性决策）。

## 5. 修复方向（落到 skill 文件，非本次执行）

### 5.1 `review-checklist.md`
- **Section A 增加"Resume 正确性"硬检查**：resume/retry 后，已 `Completed` 步骤不得被重执行（要求读 `RunSequentialAsync` 循环体确认有 `s.State != Completed` 跳过，或测试断言）。
- **Section A 增加"外部 Pause 中断"检查**：运行中 `PauseAsync` 设的状态变更，循环须在下个检查点响应（不只查 `ct`，还需查 `workflow.CurrentState`）。
- **Section A Retry 检查改为"实际次数 == 配置值"**，显式要求核对 `<=` / `<` 边界。
- **Section C "hollow method"定义收紧**：stub = "方法体不依赖真实输入产生结果 / 恒返回固定值 / 关键分支不触发"；模拟器（恒 APPROVED、恒返回假数据）属 P1 stub，无论是否"做了状态变更"。

### 5.2 `ddd-code-reviewer` SKILL.md
- **模块路由规则加硬约束**：实现含 `RunAsync`/`ResumeAsync`/`RollbackAsync` 等状态迁移方法的类，**必须同时跑 Section A**（状态机），即使它被归类为 Multi-Agent/Orchestrator。
- **Auto-Fix Rule 补强**：豁免仅限"需用户拍板的结构性决策"；"生产实现未写/模拟器占位"一律视为未完工 **必须修或显式记为 P1 open**，禁止以"out of scope"豁免。

### 5.3 `ddd-phase-quality-gate` general-checklist.md
- 第 7 类（Concurrency & Lifecycle）或第 8 类增加"状态机执行中状态可观测性"与"占位字段非空"检查项，使 Pause 有效性 / 上下文伸缩落地可被审计捕获。

## 6. 给学习项目的一句话教训

**skill 的可靠性 = 指令意图 × 检查清单粒度 × 路由正确性。**
意图对（SKILL.md 写得好）不够；清单不写到"不变量级"、路由把关键 section 漏掉，
再强的意图也会在执行时塌成"勾选式走过场"。证伪级审查无法靠"读签名"获得，必须写进**可被勾选的具体不变量**。

## 7. 已落地（2026-07-16）

§5 的修复已全部写入两个 skill 文件，并**额外加了更细的颗粒度**：

### `review-checklist.md`
- **Section A 新增「Behavioral Invariant Checks」**：resume 连续性（不重跑 Completed）、外部 Pause 响应（循环须读状态而非仅 `ct`）、Retry 实际次数 `==` 配置值（查 `<`/`<=`）、Retry 质量（跳非瞬态+退避）。
- **Section C Implementation Fidelity 收紧**：符号检查泛化（不限于 AutoGen，任何"类名暗示框架 X 却零 X 符号"都标 P1 蜜罐）；stub 定义收紧为"结果不依赖真输入/恒返回常量/做假动作"，明确"做了事 ≠ 有效"；新增**占位字段检查**（`.Empty`/`null` = P1 未实现）、**目标解析检查**（produced 值须被消费）。
- **Section C Context Propagation 新增**：上下文伸缩须真实现（非 `.Empty`）、预设嗅探脆弱性。
- **Section Z 新增「Behavioral Invariant Verification」**：结果依赖输入、循环边界、CancellationToken 透传、锁全路径、接线 vs 死代码（蜜罐）。

### `ddd-code-reviewer` SKILL.md
- **Step 2 加硬约束**：含状态迁移方法的类**必须跑 Section A**（状态机），即使名义是 Multi-Agent/Orchestrator。
- **Step 3 加「Behavioral Invariant Tracing」**：关键方法读方法体证伪不变量，逐条 `VERIFIED/VIOLATED file:line`。
- **Auto-Fix Exception 加禁令**：模拟器/占位返回**不得以 out-of-scope 豁免**，必须修或记 open P1。

### `ddd-phase-quality-gate`
- **general-checklist 第 7 类新增行为项**：Pause 可观测、Retry 边界无 off-by-one、占位字段非空、审批依赖真输入。
- **SKILL 审计表新增「Dead Code / Misnamed Hollow Class」行**：蜜罐、占位字段、死代码。

→ 下次 Phase 2 复评时，这 9 个 finding（resume 重跑/Pause 无效/Critic 模拟器/伸缩空占位/ReworkTarget 未接线/DetectPreset 脆弱/Retry off-by-one/死蜜罐/双回滚）应能被 skill **自动**抓到，无需手动复评。
