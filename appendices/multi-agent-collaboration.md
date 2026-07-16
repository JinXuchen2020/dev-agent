## 附录 C：多 Agent 协作机制详解

> [← 返回主文档](../AGENT_PLATFORM_BLUEPRINT.md)

> **背景**：平台通过 6 种 Agent 角色协作完成从需求到交付的完整流水线。本附录说明协作模式、上下文传递、失败回退和分层架构。

<a name="c.1"></a>
### C.1 角色职责

```
┌──────────────────────┬─────────────────────────────────────────────┐
│ AgentRole 枚举值       │ 职责                                         │
├──────────────────────┼─────────────────────────────────────────────┤
│ RequirementsAnalyst  │ 需求分析师：解析用户需求，拆解为功能点              │
│ ProductManager       │ 产品经理：定义用户故事、验收标准、优先级           │
│ Architect            │ 架构师：设计技术方案、模块划分、数据模型           │
│ Developer            │ 开发工程师：编写代码、实现功能                    │
│ Tester               │ 测试工程师：生成测试用例、执行测试、报告缺陷        │
│ TechnicalWriter      │ 技术文档工程师：编写 API 文档、使用手册           │
└──────────────────────┴─────────────────────────────────────────────┘
```

<a name="c.2"></a>
### C.2 编排原语与策略预设（阶段二核心）

平台只保留**一个编排原语（Orchestration Primitive）**：一个带可配置 `selectionStrategy` + `terminationCondition` 的执行引擎。所有协作模式都是该原语的**预设（preset）**，而非并列子系统：

- **`sequential` 预设（默认快路径）**：固定顺序的 selection + `termination: afterStep(N)`。即原"顺序管线"——确定性、低成本、易重放。但它只是协商拓扑的一个**退化特例**，不再作为"兄弟范式"独立存在。
- **`negotiation` 预设（协商）**：LLM 驱动的 selection + 基于 critic 反馈的 termination（详见 C.5 / C.6）。用于需要 peer 评审 / 辩论的复杂任务。

> **关键决策（修正 F1 / F4）**：废除"状态机模式 vs 群聊模式"的二分法。原 `WorkflowStateMachineEngine`（C.2/C.3）与 `AutoGenAgentOrchestrator`（C.5）合并为**同一引擎的两个预设**，共享唯一 `WorkflowContext` 契约（C.3）。这从根上消除了"双上下文契约"漂移。

```
用户输入："我需要一个电商平台的订单管理模块"
  │  编排原语（selectionStrategy + terminationCondition）
  ▼
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│ 需求分析师     │ ──→ │ 产品经理      │ ──→ │ 架构师        │   ← sequential 预设：固定顺序
│  (WorkflowContext 注入)            │
└──────────────┘     └──────────────┘     └──────┬───────┘
                                                │
                    ┌──────────────┐     ┌───────▼───────┐     ┌──────────────┐
                    │ 技术文档工程师  │ ←── │ 测试工程师      │ ←── │ 开发工程师     │
                    │  (critic 循环见 C.6)              │
                    └──────────────┘     └──────────────┘     └──────────────┘
```

**核心原则：每个 Agent 消费统一的 `WorkflowContext`，产物以结构化 artifact 沉淀，而非把自然语言历史逐级传递。**

<a name="c.3"></a>
### C.3 上下文契约（统一 WorkflowContext）

所有步骤（无论 `sequential` 还是 `negotiation` 预设）通过唯一的 **`WorkflowContext`** 对象传递上下文，取代原 `StepContext` / `StepResult.OutputPayload` 双轨（修正 F4）：

```csharp
public sealed class WorkflowContext
{
    public Guid WorkflowId { get; init; }
    public int CurrentStepOrder { get; init; }
    public IReadOnlyDictionary<string, StepArtifact> Artifacts { get; init; } // 各步结构化产物（JSON）
    public Blackboard Blackboard { get; init; }        // 共享工作区（C.3.1）
    public RetrievalContext Retrieval { get; init; }   // RAG 召回物（F5）
    public StepHistory Summary { get; init; }          // 逐步压缩摘要（C.3.1，非全量历史）
}
```

编排原语的统一调度逻辑（消费 `WorkflowContext`，不再 `previousResult?.OutputPayload`）：

```csharp
// OrchestrationPrimitive 内部逻辑（简化）
foreach (var step in workflow.Steps.OrderBy(s => s.Order))
{
    var ctx = new WorkflowContext(
        workflowId: workflow.Id,
        currentStepOrder: step.Order,
        artifacts: _store.GetArtifacts(workflow.Id),
        blackboard: _store.GetBlackboard(workflow.Id),
        retrieval: _rag.RetrieveFor(step),                       // F5：RAG 注入
        summary: _store.GetCompressedHistory(workflow.Id, cap: MaxContextTokens)); // C.3.1
    var result = await _stepExecutor.ExecuteAsync(ctx, ct);

    if (result.Outcome == StepOutcome.Failed)
    {
        await HandleFailureAsync(workflow, step, result);
        break;
    }

    await _store.PersistStepAsync(workflow.Id, step.Order, result, ct); // 逐步持久化（C.7）
    await _mediator.Publish(new WorkflowStepCompleted(workflow.Id, step.Id, step.Order, result.Artifact));
}
```

#### C.3.1 上下文伸缩策略（Context Scaling，修正 F3）

为防 token 随轮数线性爆炸，统一采用：

- **共享工作区（Blackboard）**：步骤间通过结构化 artifact 交换，而非把整段自然语言历史往前传。
- **逐步摘要压缩**：`Summary` 仅保留压缩后的历史（按 `MaxContextTokens` 封顶），单 Agent 接收量有上限。
- **检索增强上下文（RAG）**：生成前检索相关知识注入 `Retrieval`，不依赖对话历史记忆（F5）。
- **单 Agent 接收量封顶**：超出部分截断或检索，绝不无界堆历史。

<a name="c.4"></a>
### C.4 分支/并行协作（阶段三扩展模式）

当流程复杂度增加时，需要支持分支和并行：

```
需求分析师 ──→ 产品经理 ──→ 架构师 ──┬──→ 开发工程师 ──→ 测试工程师 ──→ 文档工程师
                                     │
                                     └──→ 文档工程师（先写初稿，与开发并行）
```

- **编排原语**：通过 `WorkflowContext` 的分支标记实现，引擎维护多个并行游标
- **CoreWF**：通过 `Parallel Activity` 原生支持，迁移后自动获得并行编排能力

<a name="c.5"></a>
### C.5 协商预设（negotiation preset）

`negotiation` 预设让多个 Agent 围绕同一 `WorkflowContext` 进行 peer 协商，由可配置的 selection / termination 策略驱动：

- **selection strategy**：决定下一发言者（按角色能力 / 缺陷归属路由），**不再是 `SequentialGroupChatManager` 顺序发言**——顺序发言是 `sequential` 预设的行为，不是协商。
- **termination condition**：基于 critic 反馈收敛（见 C.6），而非固定轮数。
- **工具委托 / Human-in-the-loop**：作为协商中的标准能力（HITL 断点设计见 C.6）。

三层架构（修正）：

```
┌───────────────────────────────┐
│  编排原语（单一引擎）            │  selectionStrategy + terminationCondition
│  · sequential 预设（快路径）     │
│  · negotiation 预设（协商）      │
└──────────────┬────────────────┘
               │ 每个 Agent 内部
               ▼
┌───────────────────────────────┐
│  Semantic Kernel (SK)          │  调 LLM + 工具（RAG 召回物经 WorkflowContext 注入）
└───────────────────────────────┘
```

```csharp
// negotiation 预设的 Agent 注册（阶段二实现）
var agents = new List<IAgent>
{
    new AssistantAgent("requirements-analyst", analystPrompt, modelClient),
    new AssistantAgent("product-manager", pmPrompt, modelClient),
    new AssistantAgent("architect", architectPrompt, modelClient),
    new AssistantAgent("developer", developerPrompt, modelClient, tools: codeTools),
    new AssistantAgent("tester", testerPrompt, modelClient),
    new AssistantAgent("technical-writer", writerPrompt, modelClient),
};

// negotiation 预设：真实 selection + 基于 critic 的 termination
var orchestration = orchestrationPrimitive
    .WithPreset(OrchestrationPreset.Negotiation)
    .WithSelection(new RoleBasedSelectionStrategy())      // 非 SequentialGroupChatManager
    .WithTermination(new CriticConvergenceTermination());  // 见 C.6

var workflow = await orchestration.RunAsync(
    "请分析这个需求并产出完整的架构设计 + 代码 + 测试 + 文档", context);
```

> AutoGen.NET 可作 negotiation 预设的底层实现库，但它**不是独立的编排层**；若选用，必须验证其真实符号存在（`AssistantAgent` / `GroupChat` 等），禁止"类名含 AutoGen 却零符号"的空壳（实现保真项，见 `review-checklist.md` Section C）。

<a name="c.6"></a>
### C.6 失败回退、重试与 Critic 循环

#### Critic 循环（新增，修正 F2）

普通步骤之外引入 **critic 步**：架构师评审开发产物、测试员返回缺陷清单，反馈以**结构化 diff** 形式精准路由回对应 Agent（**范围化返修**），而非"退一步整轮重跑"：

```
开发 ✅ → critic(架构师) ❌「接口缺鉴权」
        │ 范围化返修（仅开发对应文件）
        ▼
开发(修订) ✅ → critic ✅ → 测试 ✅
```

#### 重试场景（NeedsRetry）

```
需求分析 ✅ → 产品 ✅ → 架构 ✅ → 开发 ✅ → 测试 ❌
                                           │
                                           ▼ 退回开发（范围化修复）
                                      开发(修订) ✅ → 测试 ✅ → 文档 ✅
```

#### 回滚场景（NeedsRollback，精准回退指定步骤，非全量重置）

```
需求分析 ✅ → 产品 ✅ → 架构 ❌
                         │
                         ▼ 回滚到**产品**步骤（精准目标）
                    产品(修订) ✅ → 架构 ✅ → ...
```

#### 重试/回滚代码逻辑（精准回退，非全量重置）

```csharp
if (result.Outcome == StepOutcome.NeedsRetry)
{
    // 退回到上一步，让 Agent 范围化修复后重试
    var target = workflow.Steps.First(s => s.Order == step.Order - 1);
    await RollbackToAsync(workflow.Id, target.Order); // 仅重置该步及之后受影响步
    // MediatR 事件通知相关 Agent 重新执行
}

if (result.Outcome == StepOutcome.NeedsRollback)
{
    // 不可恢复错误，回滚到指定步骤，触发 Human-in-the-loop
    await RollbackToAsync(workflow.Id, targetStepOrder); // 精准目标，非全量 Pending 重置
    await _mediator.Publish(new HumanInterventionRequired(workflow.Id, step.Id, result.ErrorMessage));
}
```

<a name="c.7"></a>
### C.7 完整数据流与可恢复性（修正，可验证非绝对承诺）

```
┌──────────────────────────────────────────────────────────────────────┐
│                          用户输入需求                                    │
└────────────────────────────┬─────────────────────────────────────────┘
                             ▼
┌──────────────────────────────────────────────────────────────────────┐
│  编排原语（单一引擎）                                                    │
│  ├── 创建 Workflow 实例（状态: Pending → Running）                      │
│  ├── 按预设（sequential / negotiation）分配 Step 给对应 Agent           │
│  └── 每步完成后**立即落库**（PostgreSQL）                                 │
└────────────────────────────┬─────────────────────────────────────────┘
                             ▼
┌──────────────────────────────────────────────────────────────────────┐
│  IStepExecutor.ExecuteAsync(WorkflowContext)                          │
│  ├── 通过 Semantic Kernel 调用 LLM（System Prompt + WorkflowContext 注入）│
│  ├── LLM 可能触发 Tool Calling（搜索、代码执行等）                        │
│  ├── 返回 StepResult（Success + 结构化 artifact）                      │
│  └── 记录 TokenUsage 到审计日志                                          │
└────────────────────────────┬─────────────────────────────────────────┘
                             ▼
┌──────────────────────────────────────────────────────────────────────┐
│  MediatR 发布 WorkflowStepCompleted 事件                                │
│  ├── EventHandler 1：更新 Workflow 当前步骤状态为 Completed              │
│  ├── EventHandler 2：持久化步骤结果到 PostgreSQL（逐步持久化）            │
│  ├── EventHandler 3：累计 TokenUsage                                    │
│  └── EventHandler 4：触发下一步（按预设的 selection 策略）               │
└────────────────────────────┬─────────────────────────────────────────┘
                             ▼
                      重复各步，直到 termination condition 满足
                             ▼
┌──────────────────────────────────────────────────────────────────────┐
│  Workflow 状态 → Completed                                             │
│  ├── 产出：需求文档 + 用户故事 + 技术方案 + 代码 + 测试报告 + API 文档    │
│  ├── 成本报表：各步骤 Token 消耗汇总                                     │
│  └── 前端（React）展示完整工作流结果                                     │
└──────────────────────────────────────────────────────────────────────┘
```

> **持久化与恢复（可验证，非绝对承诺）**：
> - 每个步骤结果在完成后**立即落库**（PostgreSQL），运行态不依赖内存 `ConcurrentDictionary`。
> - 进程崩溃后，从库里**恢复未完成任务**，从中断步继续，而非丢失在途工作流。
> - **必须有 kill+restart 集成测试**证明：杀掉执行中进程，重启后从中断步恢复且结果一致。
> - 禁止写"任何一步崩溃都能恢复"这类无法验证的绝对措辞；恢复能力由上述测试证明（修正 F9）。

> **一句话总结**：6 个 Agent 通过编排原语的预设（`sequential` 快路径 / `negotiation` 协商）协作，统一消费 `WorkflowContext` 契约，编排原语负责调度顺序 / 重试 / 回滚与 critic 循环，MediatR 领域事件负责步骤间解耦通信，Semantic Kernel 负责底层 LLM 调用和工具执行。每步结果逐步持久化，恢复能力由 kill+restart 集成测试证明。

<a name="c.8"></a>
### C.8 Agent 角色可扩展性：从固定枚举到自定义角色

> **动机**：当前平台的 Agent 角色是硬编码的 6 种固定枚举，用户无法根据自身业务场景（如 DevOps、UI 设计、安全审计）自定义角色。本节分析现状、盘点架构中已预留的扩展空间，并给出从枚举到值对象的改造方案。

#### C.8.1 现状分析

当前 `AgentRole` 是一个 **C# 枚举（enum）**，硬编码了 6 种角色：

```csharp
// Domain/Enums/AgentRole.cs  ——  硬编码枚举，新增角色必须改代码重新编译
public enum AgentRole
{
    RequirementsAnalyst,   // 需求分析师
    ProductManager,        // 产品经理
    Architect,             // 架构师
    Developer,             // 开发工程师
    Tester,                // 测试工程师
    TechnicalWriter        // 技术文档工程师
}
```

**问题**：

```
┌──────────────────────────────────────────────────────────────────┐
│  硬编码枚举的三大痛点                                              │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  1. 无法运行时扩展                                                │
│     用户想要增加「DevOps 工程师」「UI 设计师」等角色，              │
│     必须修改源码 → 重新编译 → 重新部署，平台用户无权操作            │
│                                                                  │
│  2. 多租户无法差异化                                               │
│     所有租户共享同一套 6 种角色，                                   │
│     A 租户（金融）需要「合规审计员」，B 租户（游戏）需要「数值策划」  │
│     枚举无法做到按租户定制                                         │
│                                                                  │
│  3. 角色元数据缺失                                                │
│     枚举只有名字，没有 DisplayName / Description / Icon，          │
│     前端展示需要硬编码映射表，维护成本高                            │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

#### C.8.2 架构中已预留的扩展空间

尽管 `AgentRole` 是枚举，但平台的**核心协作链路**在设计时已埋下了 3 个有利扩展点，使改造的影响面远小于预期：

```
┌──────────────────────────────────────────────────────────────────────┐
│  已预留的扩展点                          所在代码                       │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  ① AssignedAgentId 用 Guid 而非枚举       WorkflowStep.cs (L485)     │
│     步骤 → Agent 的绑定已经是指向具体 Agent 实例的 Guid，              │
│     而非指向角色枚举。这意味着工作流执行时不关心"角色是什么"，          │
│     只关心"哪个 Agent 来执行"。改枚举不影响执行链路。                  │
│                                                                      │
│  ② Agent 是独立聚合根                     Agent.cs (L398)             │
│     Agent 是一等实体（有 Id / Name / SystemPrompt / Tools），         │
│     而非枚举的附庸。角色的本质只是 Agent 的一个属性，                  │
│     把属性类型从枚举换成值对象，聚合根结构不变。                       │
│                                                                      │
│  ③ AgentAssignments 是动态字典            Workflow.cs (L427)         │
│     Dictionary<string, Agent> 按"步骤名 → Agent"映射，               │
│     键是字符串而非枚举值。新增角色对应的 Agent 可直接加入字典，        │
│     无需修改 Workflow 聚合根的任何代码。                               │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

> **结论**：执行链路（WorkflowStep → Agent）已经与角色枚举解耦，改造只需要替换 `AgentRole` 类型本身及其直接引用处，不会波及工作流调度核心。

#### C.8.3 改造方案：AgentRole 枚举 → AgentType 值对象

将 `AgentRole` 从枚举改为 **值对象（record）**，内置预置角色保证向后兼容，同时开放运行时创建自定义角色。

**改造前**：

```csharp
// ──────── 改造前：硬编码枚举 ────────

// Domain/Enums/AgentRole.cs
public enum AgentRole
{
    RequirementsAnalyst,
    ProductManager,
    Architect,
    Developer,
    Tester,
    TechnicalWriter
}

// Domain/Aggregates/Agents/Agent.cs
public class Agent
{
    public AgentRole Role { get; private init; }   // 枚举类型
}

// Domain/Repositories/IAgentRepository.cs
public interface IAgentRepository
{
    Task<IReadOnlyList<Agent>> GetByRoleAsync(AgentRole role, CancellationToken ct = default);
}
```

**改造后**：

```csharp
// ──────── 改造后：值对象 + 预置角色 + 自定义角色 ────────

// Domain/Aggregates/Agents/AgentType.cs
public record AgentType(string Code, string DisplayName, string Description)
{
    // —— 预置角色（向后兼容，等价于原枚举的 6 个值）——
    public static readonly AgentType RequirementsAnalyst =
        new("REQ", "需求分析师", "解析用户需求，拆解为功能点");
    public static readonly AgentType ProductManager =
        new("PM",  "产品经理",   "定义用户故事、验收标准、优先级");
    public static readonly AgentType Architect =
        new("ARC", "架构师",     "设计技术方案、模块划分、数据模型");
    public static readonly AgentType Developer =
        new("DEV", "开发工程师", "编写代码、实现功能");
    public static readonly AgentType Tester =
        new("QA",  "测试工程师", "生成测试用例、执行测试、报告缺陷");
    public static readonly AgentType TechnicalWriter =
        new("DOC", "技术文档工程师", "编写 API 文档、使用手册");

    // —— 预置角色注册表（启动时加载到数据库种子数据）——
    public static readonly IReadOnlyList<AgentType> Presets =
    [
        RequirementsAnalyst, ProductManager, Architect,
        Developer, Tester, TechnicalWriter
    ];

    // —— 用户自定义角色工厂方法 ——
    public static AgentType Create(string code, string displayName, string description)
        => new(code, displayName, description);
}

// Domain/Aggregates/Agents/Agent.cs  —— 仅属性类型变更
public class Agent
{
    public AgentType Role { get; private init; }   // enum → 值对象
}

// Domain/Repositories/IAgentRepository.cs  —— 参数类型变更
public interface IAgentRepository
{
    Task<IReadOnlyList<Agent>> GetByRoleAsync(AgentType role, CancellationToken ct = default);
    // 或更灵活：按 Code 查询
    Task<IReadOnlyList<Agent>> GetByRoleCodeAsync(string roleCode, CancellationToken ct = default);
}
```

**数据库层适配**：

```csharp
// Infrastructure/Persistence/Configurations/AgentConfiguration.cs
// 改造前：Role 列映射为枚举（int）
// 改造后：Role 拆为 Code + DisplayName 两列，Code 作为外键关联 AgentType 种子表

builder.Property(a => a.Role.Code).HasMaxLength(16).IsRequired();
builder.Property(a => a.Role.DisplayName).HasMaxLength(64).IsRequired();
builder.HasIndex(a => a.Role.Code);

// 种子数据：启动时写入 6 个预置角色，租户可追加自定义角色
modelBuilder.Entity<AgentTypeRecord>()
    .HasData(AgentType.Presets.Select((r, i) => new { Id = i + 1, r.Code, r.DisplayName, r.Description }));
```

#### C.8.4 联动改动清单

| 改动位置 | 当前代码 | 改造内容 | 影响程度 |
| :--- | :--- | :--- | :--- |
| `AgentRole.cs` | `public enum AgentRole` (6 值) | 删除枚举，新建 `AgentType` record 值对象 | **高**（核心类型变更） |
| `Agent.cs` 聚合根 | `public AgentRole Role` | 属性类型改为 `AgentType` | 中 |
| `IAgentRepository.cs` | `GetByRoleAsync(AgentRole)` | 参数改为 `AgentType` 或 `string roleCode` | 中 |
| `AgentConfiguration.cs` | 枚举列映射（int） | 改为 `Code` 字符串列 + 种子数据 | 中 |
| `WorkflowStep.cs` | `AssignedAgentId` (Guid) | **无需改动**（已用 Guid） | 无 |
| `Workflow.cs` | `AgentAssignments` 字典 | **无需改动**（已用动态字典） | 无 |
| System Prompt 模板匹配 | 按 `AgentRole` 枚举值 switch | 按 `AgentType.Code` 字符串匹配 | 低 |
| 前端角色选择器 | 硬编码 6 个选项 | 从 API 动态加载角色列表 | 中 |
| 前端角色图标/颜色映射 | 硬编码枚举 → 图标表 | 角色元数据含 Icon 字段，前端按数据渲染 | 低 |

> **关键发现**：工作流调度核心（`WorkflowStep` / `Workflow`）**零改动**，因为它们早已用 Guid 和字典与角色枚举解耦。改造的影响面集中在 Agent 聚合根自身和仓储层。

#### C.8.5 改造后的前端用户体验

角色从枚举变为可配置后，前端 `WorkflowEditor` 的拖拽面板从"固定 6 个"变为"预置 + 自定义"分区展示：

```
┌──────────────────────────────────────────────────────────────┐
│  WorkflowEditor · Agent 角色面板                               │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌─ 预置角色 ─────────────────────────────────────────────┐  │
│  │  [📋 需求分析师]  [📝 产品经理]  [🏗️ 架构师]           │  │
│  │  [💻 开发工程师]  [🧪 测试工程师] [📖 技术文档]        │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                              │
│  ┌─ 自定义角色（本租户）─────────────────────────────────┐  │
│  │  [🚀 DevOps工程师]  [🔍 代码审查员]  [🎨 UI设计师]    │  │
│  │  [🛡️ 安全审计员]                      [ + 新建角色 ]   │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                              │
│  ─ ─ ─ ─ ─ ─ ─ ─ ─  拖拽到工作流画布  ─ ─ ─ ─ ─ ─ ─ ─ ─    │
│                                                              │
│  ┌─ 工作流画布 ──────────────────────────────────────────┐  │
│  │                                                       │  │
│  │  Step1           Step2           Step3          Step4  │  │
│  │  [需求分析师] →  [产品经理]  →  [架构师]   → [DevOps] │  │
│  │                                                       │  │
│  │  Step5           Step6                                 │  │
│  │  [开发工程师] →  [代码审查员]  ← 自定义角色混入管线     │  │
│  │                                                       │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                              │
│  [▶ 运行工作流]   [💾 保存模板]   [📋 克隆]                   │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

用户操作流程：

1. **新建角色**：点击「+ 新建角色」→ 填写 Code / 名称 / 描述 / 选择图标 → 保存到租户角色表
2. **配置 Agent**：基于新角色创建 Agent 实例（绑定模型、System Prompt、工具集）
3. **拖入工作流**：将自定义角色 Agent 拖到画布的某个 Step，替换或补充预置角色
4. **运行**：工作流引擎按 Guid 调度，与角色是预置还是自定义完全无关

> **一句话总结**：`AgentRole` 从枚举改为 `AgentType` 值对象后，用户可在运行时创建自定义角色，而工作流调度核心（`WorkflowStep` / `Workflow`）因已用 Guid + 字典与角色解耦，零改动即可兼容；改造影响面集中在 Agent 聚合根、仓储层和前端角色选择器三处。
