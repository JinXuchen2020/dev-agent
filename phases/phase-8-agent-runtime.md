# 阶段八：Agent 运行时实体化 + 模型接通

> 学习目标：把 `Agent` 从"配置实体"提升为"运行时实体"——执行时加载其 `SystemPrompt` / `ModelEndpoint`，并接通既有 `ModelRouter` + `TenantModelClientResolver`。这是蓝图标记的**最高优先缺陷**（配而不生效）。
> **关联**：`../docs/agent-harness-blueprint.md`（总体蓝图 §1.2 / §1.6 / §3 层2 / §4 Phase 8 / §5 D4 / §6 / §7）、`./phase-7-durable-execution.md`（前置持久化）、`../docs/quality/*`（质量门）。

## 学习目标

- [x] **配置实体 vs 运行时实体**：理解 Agent 作为"一等运行时公民"需要的独立上下文窗口与生命周期
- [x] **Semantic Kernel 模型路由接线**：`ModelRouter` + `TenantModelClientResolver.CreateForTenant` 从"已存在未接"到"agent 级真实生效"
- [ ] **Blackboard 分区 / 独立对话历史**：agent 上下文隔离粒度（决策 D4）——v1 延后，独立排期
- [x] **多租户模型 BYO 隔离**：租户自带模型 Key 在执行时按租户解析，不污染他租户
- [x] **EF 迁移铁律**：新字段 / 新聚合的迁移写法（`#pragma warning disable IDE0161` + `ValueGeneratedNever()`）

## 前置依赖

- [x] 阶段七执行持久化已完成并提交（非强制阻塞，但 agent 上下文落库建议复用 durable 检查点机制）
- [x] 已确认 `Agent` 聚合根字段清单——核实 SystemPrompt/ModelEndpoint 已存在且映射完备（F31 零迁移）
- [x] 已锁定蓝图决策 **D4**（Blackboard 按 agent 分区 + 每 agent 独立 message 历史，二者结合）→ v1 延后执行

## 任务清单

### 现状核实（动手前必做，防历史漂移）

- [x] 重核实 `AgentCallStepExecutor`——确认硬编码 prompt 与 `_settings.DefaultModelId`（F31 设计文档 §1 三处漂移证据）
- [x] 重核实节点绑定链路：`WorkflowNode.AssignedAgentId` 经 `IWorkflowExecutable` 暴露，但 executor 从未消费
- [x] 重核实 `ModelRouter` + `TenantModelClientResolver.CreateForTenant`——已存在且完整，仅缺 executor 接线

### 实现任务

- [x] **Agent 种子补全（前置必修）**：核实字段已齐备，零代码零迁移（转为文档声明）
- [x] **修复 AgentCallStepExecutor**：按 AssignedAgentId 加载 agent → SystemPrompt 进消息 → `IModelRouter.RouteAsync(PreferredModel: ModelEndpoint.ModelName)`；缺失 fail-loud
- [ ] **Agent 上下文窗口**：Blackboard 按 agent 分区（决策 D4）→ v1 延后项，独立排期
- [x] **租户模型 BYO 隔离**：经 `ITenantModelClientResolver` 链路解析（F13 已有隔离单测覆盖）；executor 侧 fail-loud 防跨租户静默回退

## 验收标准

1. 同一工作流内不同 agent 节点表现出**不同**行为与 prompt（配置真实生效）。✅ AgentCallStepExecutorTests 锁定
2. `ModelRouter` agent 级路由 / fallback 生效——某模型不可用时按候选回退而非恒失败。✅ PreferredModel 排序 + 既有回退循环；空候选可操作报错
3. 租户自带模型 Key 在执行时按租户解析，跨租户不可越权使用他租户 Key。✅ 复用 F13 链路 + EF 过滤器 fail-loud
4. 多 agent 节点上下文隔离：一个 agent 的历史 / Blackboard 不被另一 agent 读取或污染。⏸ v1 延后（D4）
5. 存量工作流（未显式配 agent 或沿用默认）行为向后兼容，不退化。✅ UnboundNode_KeepsLegacyPrompt_RoutesWithoutPreference

▶ **设计评审关（动手前强制）**：进入本 Phase 前须已过 `blueprint-architecture-review`（见 phase-1 §0-1）。AgentCallStepExecutor 修复 / Agent 运行时上下文 / 模型路由属"叙事性能力"且为蓝图已知漂移（配而不生效），合入前强制 `ddd-code-reviewer`。

## 0. Quality Skill Routing Policy（质量 Skill 路由策略）

本平台有两个互补 skill，职责不同、不可互相替代：

| 模块类型 | 强制 Skill | 目的 |
|----------|-----------|------|
| 实现"叙事性能力"的模块（AgentCallStepExecutor 修复 / Agent 运行时上下文 / 模型路由——**类名即承诺某种能力**） | **`ddd-code-reviewer`**（对抗式审查） | 验证实现行为是否忠于蓝图 §1.2/§4 Phase 8、依赖是否真实使用、是否真接入管道、是否存在"配置失效"漂移 |
| 纯基础设施 / 结构卫生模块（Agent 种子字段 / DI / EF 映射 / 迁移 / 配置） | `ddd-phase-quality-gate`（静态结构门禁） | DI / DDD 层 / EF / 并发 / 密封 / 守卫等结构卫生 |

**硬性规则（WHY）**：`ddd-phase-quality-gate` 的 "Blueprint Drift" 仅查"蓝图声明要做、但被标记未来的功能"，**不查"实现行为 vs 蓝图叙事"的深度一致性**。凡是"类名 / 接口名承诺了某种能力"的模块，都是"名不副实现"的高风险区，必须由 `ddd-code-reviewer` 把关。

**`ddd-code-reviewer` 报告必须包含**：对所审模块，显式写出"已核对的蓝图章节 / 验收标准"（例如 "verified against 蓝图 §1.2 / §4 Phase 8 / 阶段八验收标准"）。缺此项即视为未通过。

### Phase 8 强制范围（高风险叙事性模块）

- **AgentCallStepExecutor 修复**：核对 §1.2 / §4 Phase 8；重点验证 agent 的 `SystemPrompt` / `ModelEndpoint` 真实进入执行、硬编码默认模型被替换、agent 级 router fallback 生效。
- **Agent 运行时上下文 / 租户 BYO**：核对 §1.6 / §5 D4；重点验证多 agent 上下文隔离、跨租户模型 Key 不泄漏。

> 规划提示：阶段八消除"agent 配置失效"这一最高优先缺陷，本 §0 要求在此阶段启动前即明确——上述模块合入前**必须**走 `ddd-code-reviewer`。

## 学习笔记

### 第一天（YYYY-MM-DD）

```

```

### 第二天（YYYY-MM-DD）

```

```

## 进度

- **开始日期**：2026-08-25
- **完成日期**：2026-08-25（v1；D4 上下文隔离延后独立排期）
- **完成度**：█████████░ 90%（v1 范围 100%）

## 回顾（完成后填写）

### 做得好的
- 现状核实先行：发现字段已齐备，避免了一次不必要的迁移
- 复用 ModelRouter 全套机制而非在 executor 手搓 resolver——BYO/回退/成本/韧性四合一套餐零重复
- 实现过程中三个附带 bug 被"新测试 + 旧集成测试"双重网络捕获并当场修复（租约重获回归、自比恒 true、WSL bash 桩）

### 下次改进
- Api.Tests 的触发器集成测试此前在 f30 分支就已转红却未被察觉——全量测试应在每个 feature 收口时必跑而非抽查
- Windows 开发机环境差异（商店 python 别名 / WSL bash 桩）值得写进 docs/learning 排障手册

### 对蓝图文档的反馈
- §1.2「配而不生效」的判断与代码事实完全吻合，行号级定位准确
- D4（上下文隔离）作为 v1 延后项是正确切分——executor prompt 实体化与多 agent 隔离是两个独立风险面
