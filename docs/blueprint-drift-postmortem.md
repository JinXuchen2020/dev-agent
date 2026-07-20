# 蓝图与实现漂移问题汇总（学习笔记）

> 项目：自研 Agent 编排平台（.NET 9 + DDD + Clean Architecture）
> 时间：2026-07-16
> 性质：学习项目 — 本文记录"为什么会出现严重漂移、为什么质量 skill 没拦住、怎么补"的完整因果链。

---

## 0. 一句话结论

这次不是"一个 bug"，而是**两类不同性质的问题撞在一起**：

- **A 类 · 实现漂移** —— 代码写出来偏离了蓝图（如 `AutoGenAgentOrchestrator` 压根没用 AutoGen.NET）。
- **B 类 · 范式保守** —— 蓝图自己就是"线性瀑布"伪装成多 Agent，代码其实忠实实现了这个保守设计。

这两类问题**必须分到两个质量关去抓**：漂移归"写代码的 Phase + `ddd-code-reviewer`"，范式归"动手前的设计评审关"。把它们混为一谈，是之前治理失效的根源。

---

## 1. A 类：实现漂移的具体证据（均来自真实代码审计）

| 漂移点 | 蓝图承诺 | 代码实际 | 后果 |
|--------|----------|----------|------|
| `AutoGenAgentOrchestrator` | 用 AutoGen.NET 编排 | 手写 `for` 循环 + `IModelClient`，名不副实 | "多 Agent"实为单循环串跑 |
| 角色集 | 文档列 6 角色（含 RequirementsAnalyst/Architect） | 代码 6 角色（PM/Arch/Dev/Tester/Writer/Reviewer） | 文档与代码错位 |
| `WorkflowStateMachineEngine.RollbackCompletedStepsAsync` | 精准回滚到"指定步骤" | **全量重置**所有已完成步骤 | 回滚=推倒重来 |
| `IWorkflowEngine.Pause/Resume/Retry/Rollback` | 可暂停/恢复/重试 | 全是 `log + return` 空 stub | 流程控制是空壳 |
| 执行态存储 | "全量持久化、任意崩溃可恢复" | 仅内存 `ConcurrentDictionary` | 进程崩溃即丢，承诺落空 |

**关键洞察**：蓝图里的绝对措辞（"任何一步崩溃都能恢复"）是**过度承诺**——它没规定"每步结果落库"，于是实现层就心安理得地偷工（内存+全量重置）。**蓝图过度承诺会纵容实现漂移。**

---

## 2. B 类：范式保守（蓝图自身的问题，不是漂移）

设计评审（新建的 `blueprint-architecture-review` skill）初评 **DESIGN NEEDS WORK**，抓出 4×P1 + 5×P2，分别是：

- **F1 拓扑纯线性**：状态机 + 群聊两层调度都是串行（连群聊都用 `SequentialGroupChatManager`），无 peer 协商。
- **F2 无 critique 循环**："重试"=退回上一步重跑，是 re-execution 不是 critique。
- **F3 上下文无伸缩**：6 Agent 全量历史持续追加，token 线性爆炸，无压缩/检索/封顶。
- **F4 双上下文契约**：状态机只传上一步 `OutputPayload`，群聊传全量历史 → 两套语义不自洽。
- **F5–F9（P2）**：RAG 未接地进上下文 / HITL 只声明无断点 / 角色靠 System Prompt 区分 / 质量闭环是"产出报告"而非"跑通" / 恢复能力过度承诺。

**为什么这类问题之前任何 skill 都抓不到**：代码**忠实**实现了线性蓝图 → `ddd-code-reviewer` 的"蓝图比对"会判 PASS；`ddd-phase-quality-gate` 只查 DDD 结构卫生，不评范式。必须由**动手前的设计评审关**拦住，动手后再审 100 遍代码也没用。

---

## 3. 为什么质量 skill 漏掉了 A 类漂移（根因）

### 3.1 流程纪律缺口
Phase 2 只跑了 `ddd-phase-quality-gate`（静态结构 12 类，Blueprint Drift 仅查"显式延期功能"），**没跑** `ddd-code-reviewer`。而 reviewer 恰好有：
- Step 6 蓝图比对
- Section C API 签名比对
- Step 3.2 空 stub 检测

—— 本该至少抓到 3 个漂移点。

### 3.2 skill 自身的两处真盲区
`ddd-code-reviewer` 此前缺：
1. 回滚"全量 vs 指定步骤"未与蓝图比对；
2. 无"进程崩溃恢复"断言。

### 3.3 "蜜罐"陷阱
`AutoGenAgentOrchestrator` 类名 + `AutoGenSettings` 看着很真，但内部方法都是空的、又不抛 `NotImplementedException`。靠快捷 grep 搜"TODO/throw new NotImplemented"的捷径完全抓不到。

---

## 4. 治理架构：三道关如何分工（修复后的闭环）

| 关 | 时机 | 审什么 | 负责 Skill |
|----|------|--------|-----------|
| **设计评审关** | 动手写/改任何蓝图能力**之前** | 蓝图范式对不对（线性/缺 critic/上下文爆炸/RAG 不接地/HITL 无断点/恢复过度承诺） | `blueprint-architecture-review` |
| **§0 路由策略** | 动手后，高风险模块合入前 | 代码有没有照蓝图做（高风险叙事模块强制 reviewer） | `ddd-code-reviewer` / `ddd-phase-quality-gate` |
| **结构门禁** | 各阶段 | DDD 卫生（DI/分层/EF/并发/密封/守卫） | `ddd-phase-quality-gate` |

**责任边界铁律**：漂移归"写代码的 Phase"（编排器漂移归 Phase 2）；范式归"设计评审关"，Phase 3 不替 Phase 2 背范式债。

---

## 5. 实际修复动作清单

1. **补 `ddd-code-reviewer` 盲区** → `review-checklist.md` 加 3 处强制检查：回滚目标比对、崩溃恢复断言、Section C 实现保真（AutoGen 符号存在性 + 空接口方法检查）。
2. **全阶段推广 §0 路由策略** → phase-1/2/3/4 各加统一 §0，高风险叙事模块（状态机/AutoGen 编排/SK 客户端等）钉死强制 reviewer。
3. **新建 `blueprint-architecture-review` skill** → 9 维范式评审 + P0/P1/P2 rubric，补齐"动手前审范式"这一层盲区。
4. **设计评审关写进阶段流程** → phase-1 §0-1（含 **DESIGN READY 准入** + **变更传播规则**），phase-2/3/4 加指针。
5. **治理流程写进 README** → 「## 质量治理流程」三关表格 + 关键约定。
6. **蓝图附录 C 重写（P1 全闭环）**：
   - C.2 合并双引擎为**单一编排原语 + `sequential`/`negotiation` 预设**（线性降为退化特例）；
   - C.3 统一 **`WorkflowContext`** 契约（灭掉 F4 双上下文漂移）；
   - C.3.1 新增上下文伸缩策略（F3）；
   - C.5 改成 negotiation 预设（真实 selection/termination）；
   - C.6 新增 **critic 循环**（F2）；
   - C.7 软化恢复承诺为可验证设计 + kill+restart 测试（F9）。
   - 复审结论：**DESIGN READY**。
7. **Phase 任务清单外科手术式同步** → 仅改编码了被推翻决策的 phase 任务（Phase 2 Module 4 改 negotiation 预设、Module 2 对齐统一契约；Phase 3 补 F3/F5/F6；Phase 4 补 critic/上下文策略）。
8. **修一处 meta 漂移** → `ddd-phase-quality-gate` 之前会自动生成独立的 `phase-N-checklist.md`（飘在版本控制外）。改为清单**就地写入**被引用的 phase 文档小节，不再新建文件。
9. **全量扫描引用漂移** → 发现并修复两处同源遗漏：phase-1 L258、README L91 的评审结论仍写旧 "DESIGN NEEDS WORK"，已对齐为"初评 NEEDS WORK → 复审 DESIGN READY"。

---

## 6. ⚠️ 关键边界：蓝图 READY ≠ 代码 READY（当前待办）

这是本次最容易踩的坑：

> 蓝图修的是**设计层**。Phase 2 现有**代码**仍实现**旧设计**（双引擎 / 内存态 / 全量重置 / 空心 AutoGen）。新蓝图已取代旧蓝图，因此该代码现在相对新蓝图处于**"漂移"态**。

按变更传播规则，下一步必须：
1. 以**新蓝图**为 spec 重写 Phase 2 编排器/状态机；
2. 落实统一 `WorkflowContext` + 逐步持久化 + 精准回滚；
3. 重新跑 `ddd-code-reviewer` 闭环（旧的"忠实 PASS"已作废）。

---

## 6.1 更正（2026-07-16 复评发现，推翻上面 §6 的"当前待办"）

> ⚠️ **本节对 §6 的结论做了重大修正**：上面 §6 写的"Phase 2 旧代码仍实现旧设计、处于漂移态"**已不准确**。

复评（`docs/quality/phase-2-rereview-2026-07-16.md`）逐行核对了实际代码，结论反转：

- **A 类实现漂移（双引擎 / 内存态 / 全量重置 / 空心 AutoGen）在新基线上已解决**。编排的**实时执行路径**早已重写为 `OrchestrationPrimitive`（`RunWorkflowCommandHandler → IOrchestrationPrimitive.RunAsync → OrchestrationPrimitive`），单一编排原语 + `sequential`/`negotiation` 预设、统一 `WorkflowContext`、精准回滚（Order≥target）、逐步持久化、真实 `Pause/Resume/Retry/Rollback`。
- 旧的 `AutoGenAgentOrchestrator`（蜜罐）**根本没在 DI 注册** → 死代码；`WorkflowStateMachineEngine`/`IWorkflowEngine`/`StubWorkflowEngine` 已标 `[Obsolete]` 或注册即抛异常。
- `RoleBasedSelectionStrategy` 与 `CriticConvergenceTermination` 是**真实实现**（非 stub），协商预设确实落地。

**所以"蓝图 READY ≠ 代码 READY"这句话对实时路径已不成立**——代码现在照新蓝图实现了。但新原语有**自己的新缺陷**（非旧漂移）：
- P1：`Resume`/`Retry` 会**重跑全部步骤**（不跳已完成）；`Pause` 执行中**不生效**；`Critic` 是**恒通过模拟器**（C.6 功能空）。
- P2：上下文伸缩（C.3.1）未实现；`ReworkTarget` 未接线；`DetectPreset` 字符串嗅探脆弱且破坏 Resume。
- P3：Retry off-by-one（4 次非 3）；死蜜罐未标 Obsolete；双回滚路径冗余。

**修正后的"当前待办"**：不是"重写编排器"，而是**修新原语的 P1/P2**（跳过已完成步骤、Pause 生效、Critic 真实化或显式标注占位），修完重跑 `ddd-code-reviewer` 闭环、补测试，再写 `.quality-gate.json cleared:true` 才能 commit `src/`。`src/` 目前仍**不可 commit**（质量门未清）。

> 教训：写"代码仍漂移"这类结论前要**先 grep 实时路径的 DI 注册与调用方**，不能只凭旧文件存在就断言它还活着。蜜罐类（名带 Orchestrator 却死代码）最容易让人误判。

---

## 7. 给学习项目的经验

1. **漂移 vs 范式是两种病，药方不同**：别指望代码审查抓到范式债，也别让下游 Phase 替上游背债。
2. **蓝图过度承诺是腐败源**：写"任何崩溃都能恢复"之前，先规定"每步落库"并附 kill+restart 测试。绝对措辞会纵容偷工。
3. **质量是流程纪律，不是 skill 魔法**：skill 加再多检查，Phase 2 不跑它就没用。路由策略要写进 phase 文档并强制执行。
4. **类名是蜜罐**：`XxxOrchestrator` 配空方法最骗人。审查要查"符号是否真被调用/真有实现"，不能只 grep 名字。
5. **质量证据也要进 git**：`phase-N-checklist.md` 飘在版本控制外，正是"流程缺口"的又一例。文档资产一律 `git add`。
6. **改了 verdict 要全局搜一次**：初评结论被多份文档中转引用，改一处漏一处（phase-1 L258 + README L91 同源）。以后改报告结论，搜全仓 "DESIGN NEEDS WORK" 一次性同步。
7. **单一编排原语 + 预设**是好抽象：线性 = negotiation 的退化特例，合并后消除双引擎/双上下文漂移，设计层一次性闭环 F1/F4。

---

_关联文档：_
- 评审报告：`docs/blueprint-architecture-review-2026-07-16.md`
- 治理约定：`README.md` → 「## 质量治理流程」
- 阶段流程：`phases/phase-1-baseline-mvp.md` §0-1（含变更传播规则）
