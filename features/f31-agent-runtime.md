# F31 · Agent 运行时实体化 + 模型接通 设计文档

> **关联**：`phases/phase-8-agent-runtime.md`、`docs/agent-harness-blueprint.md` §Phase 8、`features/backlog.md` F31
> **状态**：`doing`（2026-08-25 开始，分支 `feat/f31-agent-runtime`，基于 `feat/f30-durable-execution`）
> **优先级**：P0（蓝图标记的最高优先缺陷：「配而不生效」）

---

## 1. 问题陈述（现状核实结论）

用户实测复现：给节点绑定智能体后运行，仍报 `无法调用模型 'deepseek-chat'`。代码走查确认三处漂移：

| # | 漂移点 | 证据 |
|---|--------|------|
| ① | `AgentCallStepExecutor` **完全不消费节点绑定的 agent** | `ExecuteAsync(step, ctx, ct)` 从未读取 `step.AssignedAgentId`（接口已有该属性，Domain/Abstractions/IWorkflowExecutable.cs:25）；prompt 为通用模板 `"You are an agent executing the step..."` |
| ② | 模型 ID 写死 `_settings.DefaultModelId` | AgentCallStepExecutor.cs:50、CriticStepExecutor.cs:83 |
| ③ | `IModelClient` 注入的是仅由平台配置构造的 `SemanticKernelModelClient` | DependencyInjection.cs:101-106；构造函数只读 `OpenAI:Key`/`DeepSeek:Key`/`VLLM:Url` 三项（SemanticKernelModelClient.cs:46-75）。租户 BYO 链路（`ITenantModelClientResolver`→`CreateForTenant`）只有经 `IModelRouter` 的功能（Research 等）能走到 |

**字段现状（防迁移误判）**：`Agent.SystemPrompt`(required) 与 `Agent.ModelEndpoint`(Provider/ModelName/ApiUrl/Temperature/MaxTokens 值对象) 已存在且 EF 映射完备（见 AppDbContextModelSnapshot Agents 表）——**本 feature 无需任何 EF 迁移**。

---

## 2. 目标与范围

把「agent 配置实体」升级为「运行时实体」：执行时真实加载其 `SystemPrompt` / `ModelEndpoint`，并把工作流执行链路的模型调用接通既有 `ModelRouter`（租户 BYO 优先 → 平台回退 → 候选降级）。

### v1 范围（对应验收子项 ①②③）
- **① executor 接管 agent 配置**：`AgentCallStepExecutor` 按 `AssignedAgentId` 加载聚合，SystemPrompt 真实生效。
- **② 模型接通到 agent 级**：LLM 类步骤（AgentCall + Critic）全部改经 `IModelRouter.RouteAsync`；绑定了 agent 的调用携带 `PreferredModel = agent.ModelEndpoint.ModelName`。
- **③ 种子字段补全**：核实后确认字段已齐备，本项转为「核验 + 文档声明」，零代码改动。

### 明确不做（v1 边界）
- Blackboard 按 agent 分区 / 每 agent 独立对话历史（D4 决策延后项，独立排期）
- `AgenticStepExecutor`（F29 自有 IModelClient 用法，独立轨道）
- `NegotiationOrchestrator` 内部的选择策略 prompt（非 executor 模型调用路径）
- `ModelEndpoint.Temperature` / `MaxTokens` 透传（`CreateForTenant` 与平台路径均不支持参数，列已知残留）

---

## 3. 核心设计

### 3.1 AgentCallStepExecutor 改造（叙事性模块 · 强制 ddd-code-reviewer）

```
ExecuteAsync(step, ctx, ct)
  ├─ agentId = step.AssignedAgentId
  ├─ agentId != null
  │    ├─ agent = await _agentRepository.GetByIdAsync(agentId)   // EF HasQueryFilter 天然租户隔离
  │    ├─ agent == null → fail-loud RetryableFailure("绑定的智能体不存在或无权访问")
  │    ├─ messages = BuildPrompt(agent.SystemPrompt + 上下文注入)  // 替换通用模板
  │    └─ RouteAsync(new RoutingRequest(ctx.TenantId, messages,
  │              PreferredModel: agent.ModelEndpoint.ModelName))
  └─ agentId == null（向后兼容，验收 #5）
       ├─ messages = BuildPrompt(通用模板 + 上下文注入)            // 现状不变
       └─ RouteAsync(new RoutingRequest(ctx.TenantId, messages))   // BYO 优先→平台回退
```

依赖变化：`IModelClient` → `IAgentRepository` + `IModelRouter`（DI 均已注册 scoped，无新增注册）。

### 3.2 CriticStepExecutor 接通（同类缺陷一并修复的理由）

用户实测工作流 `Start→Architect→Developer→Critic→End` 中 Critic 节点同样硬编码 `DefaultModelId`——若只修 AgentCall，「我的凭据」方案在第三步依然失败。改动最小化：仅替换调用通道为 `_modelRouter.RouteAsync(...)`（无 preferred model），reviewer system prompt、JSON 解析、`AllowCriticOverride` fail-loud/fail-open 语义全部保持不变。

> 范围说明：backlog 字面只点名 AgentCallStepExecutor，但「② 模型接通」的验收语义（BYO 全链路可用）要求覆盖所有 LLM 步骤通道；Critic 是工作流 DAG 一等节点类型，不修则验收场景无法闭环。

### 3.3 ModelRouter 空候选守卫（体验修复）

现状：租户无 BYO 且平台无 Key 时，候选列表为空 → 循环空转 → 抛笼统的 `AllModelsFailedException("All candidate models failed...")`，丢失原先 SemanticKernelModelClient 里那条有用的中文指引。修复：`RouteAsync`/`PumpStreamAsync` 在候选为空时直接抛：

> 未配置任何可用模型：当前租户无启用的 BYO 凭据，平台也未配置模型目录（Router:Candidates 对应的 Key 为空）。请在「我的凭据」添加模型凭据，或配置平台级 LLM Key。

### 3.4 错误传播语义

- Router 失败（含 AllModelsFailedException）→ executor catch → `RetryableFailure(ex.Message)`（沿用既有重试/回滚管线，错误详情最终落 `WorkflowNode.ErrorDetail`，用户可在 DB/UI 看到）
- Critic 的 fail-loud 分支照旧把异常消息写入 review JSON（`Rejected (critic model error): ...`）

---

## 4. 数据模型

**零变更**。无新聚合、无新列、无 EF 迁移。

---

## 5. 测试计划

新增 `AgentCallStepExecutorTests`（mock IAgentRepository/IModelRouter）：
1. 绑定 agent → 消息含 agent.SystemPrompt；RoutingRequest.PreferredModel == agent 模型名
2. 未绑定节点 → 无 PreferredModel；消息为通用模板
3. 绑定 agent 但仓储返回 null → RetryableFailure 且错误信息明确（fail-loud）
4. Router 抛异常 → RetryableFailure（进入既有重试管线）
5. 成功路径 → StepExecutionResult.Success 携带输出与 token usage

新增/扩展 `CriticStepExecutorTests`：
6. 经 Router 调用（不再触达 IModelClient.DefaultModelId 路径）
7. AllowCriticOverride=false + 模型异常 → Approved=false fail-loud（回归保护）
8. AllowCriticOverride=true + 模型异常 → 自动批准（回归保护）

扩展 ModelRouter 测试：
9. 候选为空 → 中文指引错误（非笼统 AllModelsFailed）

回归：OrchestrationPrimitiveTests 23 例必须全绿（编排器测试 mock IStepExecutor，不受 ctor 变更影响）。

---

## 6. 决策记录

| 编号 | 决策点 | 结论 | 依据 |
|------|--------|------|------|
| D1 | executor 直调 resolver vs 走 IModelRouter | **走 IModelRouter** | 复用 BYO/平台合并、候选降级、成本控制、韧性管道四套现成机制；避免第二份路由逻辑漂移 |
| D2 | 未绑定 agent / Critic 节点的模型解析 | 同样走 RouteAsync（无偏好模型） | 让「我的凭据」对全链路生效；prompt 保持向后兼容（验收 #5） |
| D3 | agent 加载失败语义 | **fail-loud**（RetryableFailure 明确报错），不静默回退默认模型 | 防「配而不生效」以新形态复发；跨租户 id 由 EF query filter 拦截为 not-found |
| D4 | Temperature/MaxTokens | v1 不透传，列已知残留 | CreateForTenant/平台注册路径均无此参数；引入需动 SK 服务构建层，收益低风险高 |

## 7. 验收标准（对照 phase-8）

1. ✅ 同一工作流内不同 agent 节点表现出不同行为与 prompt（配置真实生效）→ 测试 1 + 用户实测
2. ✅ ModelRouter agent 级路由/fallback 生效（某模型不可用按候选回退而非恒失败）→ PreferredModel 排序 + 既有回退循环
3. ✅ 租户自带 Key 执行时按租户解析，跨租户不可越权 → 复用 F13 TenantModelClientResolver（已有隔离单测）+ 本 feature D3 fail-loud
4. ⏸ 多 agent 上下文隔离 → v1 延后项（D4 决策），不阻塞
5. ✅ 存量工作流（未显式配 agent）行为向后兼容 → 测试 2；唯一行为差异是模型解析从「直连平台字典」变为「经 Router」（BYO 存在时会优先用 BYO——这正是修复目标，非退化）

---

## 8. 完成记录（2026-08-25）

**分支**：`feat/f31-agent-runtime`（基于 `feat/f30-durable-execution`）

**核心改动：**
- `AgentCallStepExecutor` 重写：按 `AssignedAgentId` 加载 Agent 聚合（EF 租户过滤器防跨租户）→ SystemPrompt 驱动 prompt（未绑定节点保持通用模板，向后兼容）→ 模型调用改经 `IModelRouter.RouteAsync`，`PreferredModel = agent.ModelEndpoint.ModelName`；agent 缺失 fail-loud（明确中文报错，不静默回退）
- `CriticStepExecutor` 同链路接通 `IModelRouter`（无偏好模型），reviewer prompt 与 AllowCriticOverride fail-loud/open 语义不变
- `ModelRouter` 空候选守卫：新增 `ModelNotConfiguredException`（可操作中文指引），替代笼统 AllModelsFailedException（同步+流式双路径）
- **附带修复 1（F30 回归）**：重跑/恢复工作流时被陈旧 RunningExecution 租约阻断——`TryAcquireLease` 移除「仅 Running 可租」门禁；终态清空 InstanceId 后允许重新获取；Paused→Resume 同步打通。Api.Tests WorkflowTriggersIntegrationTests 2 例由红转绿实证
- **附带修复 2（领域 bug）**：`TryAcquireLease` 原比较为属性自比恒 true → 任意实例可抢活跃租约，多实例幂等守卫实际失效。改为参数 vs 持有者正确比较；新增 `Rehydrate` 工厂支持过期态测试构造
- **附带修复 3（生产缺陷）**：`ProcessCodeSandbox.ResolveBashPath` 的 `where bash` 兜底会命中 System32 WSL 桩（无 Git Bash 的 Windows 上所有 run_command 必败且报乱码）。排除系统目录桩 + `bash -c echo ok` 实测探针

**测试：**
- 新增 `AgentCallStepExecutorTests` 5 例（SystemPrompt/PreferredModel 生效、未绑定兼容、fail-loud、Router 异常传播、artifact 内容）
- 新增 `CriticStepExecutorTests` 4 例（经 Router、fail-loud 回归、override 自动批准回归、无 artifact 不路由）
- 新增 `ModelRouterNotConfiguredTests` 2 例（空候选守卫 + 平台候选正常路径）
- 新增 `RunningExecutionTests` 8 例（租约全生命周期 + 终态重获 + 过期接管 + 抢占拒绝）
- 全绿：App 214/214 · Infra 147+6skip/153 · Api 35/35（含转绿的 2 例触发器集成）· Arch 9/9；build 0 警告 0 错误
- 前端零改动（tsc/vite 无需重跑）

**已知残留：**
- `ModelEndpoint.Temperature/MaxTokens` 未透传到 SK 执行设置（D4）
- AgenticOrchestrator 自有 IModelClient 用法不在本 feature 范围（F29 轨道）
- Negotiation 选择策略 prompt 未实体化（非 executor 模型调用路径）

**质量门**：三道门 PASS，`.quality-gate.json` 推进 `f31-agent-runtime`，`cleared:true`
