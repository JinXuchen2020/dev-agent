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
### C.2 顺序管线协作（阶段二主模式）

6 个 Agent 按**顺序管线（Pipeline）**逐级传递上下文：

```
用户输入："我需要一个电商平台的订单管理模块"
  │
  ▼
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│ 需求分析师     │ ──→ │ 产品经理      │ ──→ │ 架构师        │
│              │     │              │     │              │
│ 输出：需求文档  │     │ 输出：用户故事  │     │ 输出：技术方案  │
│ - 功能点列表   │     │ - 验收标准    │     │ - 模块划分     │
│ - 约束条件     │     │ - 优先级      │     │ - 数据模型     │
│ - 业务规则     │     │ - 用户流程    │     │ - 接口定义     │
└──────────────┘     └──────────────┘     └──────┬───────┘
                                                │
                    ┌──────────────┐     ┌───────▼───────┐     ┌──────────────┐
                    │ 技术文档工程师  │ ←── │ 测试工程师      │ ←── │ 开发工程师     │
                    │              │     │              │     │              │
                    │ 输出：文档     │     │ 输出：测试报告  │     │ 输出：代码     │
                    │ - API 文档    │     │ - 测试用例     │     │ - 源代码文件   │
                    │ - 使用手册    │     │ - 测试结果     │     │ - 单元测试     │
                    │ - 部署说明    │     │ - 缺陷报告    │     │ - 构建配置     │
                    └──────────────┘     └──────────────┘     └──────────────┘
```

**核心原则：每个 Agent 的输出是下一个 Agent 的输入。**

<a name="c.3"></a>
### C.3 上下文传递机制

通过 `StepContext` 和 `StepResult` 的 Payload 字段实现上下文管道：

```csharp
// Step1：需求分析师执行
var step1Context = new StepContext(workflowId, step1Id, order: 1, inputPayload: 用户原始需求);
var step1Result = await requirementsAnalyst.ExecuteAsync(step1Context);
// step1Result.OutputPayload = JSON 格式的需求分析报告

// Step2：产品经理接收需求分析报告作为输入
var step2Context = new StepContext(workflowId, step2Id, order: 2,
    inputPayload: step1Result.OutputPayload);   // ← 上一轮的输出 = 这一轮的输入
var step2Result = await productManager.ExecuteAsync(step2Context);
// step2Result.OutputPayload = JSON 格式的用户故事 + 验收标准

// 依此类推，像管道一样逐级传递
```

数据流全景：

```
用户需求(文本)
  → [需求分析 JSON]
  → [用户故事 JSON]
  → [技术方案 JSON]
  → [代码 JSON]
  → [测试报告 JSON]
  → [文档 JSON]
```

自研状态机内部的顺序调度逻辑：

```csharp
// CustomWorkflowEngine 内部逻辑（简化）
foreach (var step in workflow.Steps.OrderBy(s => s.Order))
{
    var input = previousResult?.OutputPayload ?? workflow.Context;
    var context = new StepContext(workflow.Id, step.Id, step.Order, input);
    var result = await _stepExecutor.ExecuteAsync(context, ct);

    if (result.Outcome == StepOutcome.Failed)
    {
        await HandleFailureAsync(workflow, step, result);
        break;
    }

    previousResult = result;
    await _mediator.Publish(
        new WorkflowStepCompleted(workflow.Id, step.Id, step.Order, result.OutputPayload));
}
```

<a name="c.4"></a>
### C.4 分支/并行协作（阶段三扩展模式）

当流程复杂度增加时，需要支持分支和并行：

```
需求分析师 ──→ 产品经理 ──→ 架构师 ──┬──→ 开发工程师 ──→ 测试工程师 ──→ 文档工程师
                                     │
                                     └──→ 文档工程师（先写初稿，与开发并行）
```

- **自研状态机**：通过 `StepContext` 的分支标记实现，引擎维护多个并行游标
- **CoreWF**：通过 `Parallel Activity` 原生支持，迁移后自动获得并行编排能力

<a name="c.5"></a>
### C.5 AutoGen.NET 编排层

AutoGen.NET 在架构中承担 **Agent 间对话协商** 角色（可选，阶段二引入）：

```
AutoGen.NET 核心能力：
├── Agent 间多轮对话（不只是传递 JSON，而是自然语言协商）
├── Group Chat（多个 Agent 加入群组讨论）
├── 工具调用委托（Agent 可以把工具调用委托给另一个 Agent）
└── Human-in-the-loop（关键节点等待用户确认）
```

AutoGen.NET 在三层架构中的位置：

```
┌───────────────────────────────┐
│  工作流调度层（自研/CoreWF）     │  ← 管"谁先谁后、什么时候跳过"
└──────────────┬────────────────┘
               │ 调度
               ▼
┌───────────────────────────────┐
│  AutoGen.NET 编排层（可选）     │  ← 管"Agent 之间怎么对话协商"
└──────────────┬────────────────┘
               │ 分配
               ▼
┌───────────────────────────────┐
│  Semantic Kernel (SK)          │  ← 管"怎么调 LLM + 工具"
│  每个 Agent 内部都用 SK        │
└───────────────────────────────┘
```

```csharp
// AutoGen.NET 的 Agent 注册（阶段二实现）
var agents = new List<IAgent>
{
    new AssistantAgent("requirements-analyst", analystPrompt, modelClient),
    new AssistantAgent("product-manager", pmPrompt, modelClient),
    new AssistantAgent("architect", architectPrompt, modelClient),
    new AssistantAgent("developer", developerPrompt, modelClient, tools: codeTools),
    new AssistantAgent("tester", testerPrompt, modelClient),
    new AssistantAgent("technical-writer", writerPrompt, modelClient),
};

// Group Chat 模式——所有 Agent 加入群组，按策略发言
var groupChat = new GroupChat(
    agents,
    admin: agents[0],                         // 需求分析师为管理员
    groupChatManager: new SequentialGroupChatManager()  // 顺序发言策略
);

// 流水线：每个 Agent 处理后自动传给下一个
var workflow = await groupChat.RunAsync(
    "请分析这个需求并产出完整的架构设计 + 代码 + 测试 + 文档");
```

<a name="c.6"></a>
### C.6 失败回退与重试

#### 重试场景（NeedsRetry）

```
需求分析 ✅ → 产品 ✅ → 架构 ✅ → 开发 ✅ → 测试 ❌
                                           │
                                           ▼ 重试（退回开发修复）
                                      开发修复 → 测试 ✅ → 文档 ✅
```

#### 回滚场景（NeedsRollback）

```
需求分析 ✅ → 产品 ✅ → 架构 ❌
                         │
                         ▼ 回滚到产品，重新定义需求
                    产品(修订) ✅ → 架构 ✅ → ...
```

#### 重试/回滚代码逻辑

```csharp
if (result.Outcome == StepOutcome.NeedsRetry)
{
    // 退回到上一步，让 Agent 修复后重试
    var previousStep = workflow.Steps.First(s => s.Order == step.Order - 1);
    await RollbackAsync(workflow.Id, previousStep.Order);
    // MediatR 事件通知相关 Agent 重新执行
}

if (result.Outcome == StepOutcome.NeedsRollback)
{
    // 不可恢复错误，回滚到指定步骤，触发 Human-in-the-loop
    await RollbackAsync(workflow.Id, targetStepOrder);
    await _mediator.Publish(new HumanInterventionRequired(workflow.Id, step.Id, result.ErrorMessage));
}
```

<a name="c.7"></a>
### C.7 完整数据流全景图

```
┌──────────────────────────────────────────────────────────────────────┐
│                          用户输入需求                                    │
└────────────────────────────┬─────────────────────────────────────────┘
                             ▼
┌──────────────────────────────────────────────────────────────────────┐
│  CustomWorkflowEngine（自研状态机）                                     │
│  ├── 创建 Workflow 实例（状态: Pending → Running）                      │
│  ├── 分配 Step1 给 RequirementsAnalyst Agent                           │
│  └── 持久化状态到 Redis + PostgreSQL                                    │
└────────────────────────────┬─────────────────────────────────────────┘
                             ▼
┌──────────────────────────────────────────────────────────────────────┐
│  IStepExecutor.ExecuteAsync()                                         │
│  ├── 通过 Semantic Kernel 调用 LLM（带 System Prompt + 历史上下文）        │
│  ├── LLM 可能触发 Tool Calling（搜索、代码执行等）                        │
│  ├── 返回 StepResult（Success + 需求分析 JSON）                        │
│  └── 记录 TokenUsage 到审计日志                                          │
└────────────────────────────┬─────────────────────────────────────────┘
                             ▼
┌──────────────────────────────────────────────────────────────────────┐
│  MediatR 发布 WorkflowStepCompleted 事件                                │
│  ├── EventHandler 1：更新 Workflow 当前步骤状态为 Completed              │
│  ├── EventHandler 2：持久化步骤结果到 PostgreSQL                         │
│  ├── EventHandler 3：累计 TokenUsage                                    │
│  └── EventHandler 4：触发下一步 Step2（ProductManager）                 │
└────────────────────────────┬─────────────────────────────────────────┘
                             ▼
                      重复 Step2~6，直到所有步骤完成
                             ▼
┌──────────────────────────────────────────────────────────────────────┐
│  Workflow 状态 → Completed                                             │
│  ├── 产出：需求文档 + 用户故事 + 技术方案 + 代码 + 测试报告 + API 文档    │
│  ├── 成本报表：各步骤 Token 消耗汇总                                     │
│  └── 前端（React）展示完整工作流结果                                     │
└──────────────────────────────────────────────────────────────────────┘
```

> **一句话总结**：6 个 Agent 通过顺序管线协作，每个 Agent 的 LLM 输出（JSON）是下一个 Agent 的输入，自研状态机负责调度顺序/重试/回滚，MediatR 领域事件负责步骤间解耦通信，AutoGen.NET 提供高级对话协商能力（可选），Semantic Kernel 负责底层 LLM 调用和工具执行。整条链路的状态全量持久化到 Redis + PostgreSQL，任何一步崩溃都能恢复。

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
