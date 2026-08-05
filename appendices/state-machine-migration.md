## 附录 B：状态机引擎迁移方案（自研 → CoreWF）

> [← 返回主文档](../AGENT_PLATFORM_BLUEPRINT.md)

> **背景**：阶段一/二使用自研状态机快速出 MVP，阶段三后期根据流程复杂度评估是否引入 CoreWF。本附录确保迁移过程**只改动 `AgentPlatform.Workflow` 一个项目，其余零修改**。

### B.1 核心原理：概念对齐

自研状态机与 CoreWF 存在天然的映射关系：

| 自研状态机（当前） | CoreWF（目标） | 说明 |
| :--- | :--- | :--- |
| `WorkflowState` (enum) | Bookmark（书签，挂起点） | 挂起/恢复的语义等价 |
| `WorkflowStep` (实体) | Activity（活动，一个执行单元） | 每一步都是一个可编排的单元 |
| `CustomStateMachine` | StateMachine Activity | CoreWF 内置的状态机编排 |
| MediatR 领域事件 | WF Extension + 事件 | CoreWF 通过 Extension 桥接 MediatR |
| EF Core 状态持久化 | InstanceStore（内置持久化） | CoreWF 自带的序列化存储 |
| `IStepExecutor` 接口 | `CodeActivity` 包装 | **关键抽象层，Agent 逻辑零修改** |

> **迁移本质**：换调度引擎，不换业务逻辑。只要自研阶段的每个概念都能映射到 CoreWF 的对应概念，就是"换引擎，不换接口"。

### B.2 迁移前提：三个抽象层必须在自研阶段到位

#### ① 统一步骤接口 IStepExecutor（最关键）

所有 6 种 Agent 角色（需求→产品→架构→开发→测试→文档）的执行逻辑全部写在 `IStepExecutor` 实现里，不直接依赖任何状态机。

```csharp
// 自研阶段：状态机手动调用 IStepExecutor
public class CustomWorkflowEngine : IWorkflowEngine
{
    private readonly IStepExecutor _stepExecutor;
    private readonly IWorkflowRepository _repository;

    public async Task StartAsync(Workflow workflow, CancellationToken ct)
    {
        var firstStep = workflow.Steps.OrderBy(s => s.Order).First();
        var context = new StepContext(workflow.Id, firstStep.Id, firstStep.Order, workflow.Context);
        var result = await _stepExecutor.ExecuteAsync(context, ct);
        // → 处理 result，更新状态...
    }
}

// CoreWF 阶段：把 IStepExecutor 包装成 CodeActivity，不改业务代码
public class StepExecutorActivity : CodeActivity
{
    public required IStepExecutor Executor { get; init; }

    protected override async Task ExecuteAsync(CodeActivityContext context)
    {
        var stepCtx = StepContext.FromDictionary(context.GetExtension<StepData>());
        var result = await Executor.ExecuteAsync(stepCtx, context.CancellationToken);
        result.SetAsOutput(context);  // 写回 CoreWF 变量
    }
}
```

#### ② MediatR 领域事件保持不变

CoreWF 不取代 MediatR，而是通过 Extension 与它共存。所有 EventHandler（状态持久化、Token 统计、下一步触发）**零修改**。

```csharp
// CoreWF ↔ MediatR 桥接 Extension
public class MediatRWorkflowExtension : WorkflowInstanceExtension
{
    private readonly IMediator _mediator;

    public MediatRWorkflowExtension(IMediator mediator) => _mediator = mediator;

    public async Task PublishDomainEventAsync<T>(T domainEvent) where T : INotification
    {
        await _mediator.Publish(domainEvent);  // 发给原有的 MediatR Handler
    }
}

// CoreWF Activity 内部调用
public class AgentStepActivity : CodeActivity
{
    protected override async Task ExecuteAsync(CodeActivityContext context)
    {
        var mediatorExt = context.GetExtension<MediatRWorkflowExtension>();
        await mediatorExt.PublishDomainEventAsync(new WorkflowStepCompleted(...));
        // ↑ 事件处理器完全不用改，它们不知道底层从自研换成了 CoreWF
    }
}
```

#### ③ 持久化存储格式兼容（WorkflowStateConverter）

```csharp
public static class WorkflowStateConverter
{
    // 自研 → CoreWF（迁移时执行一次）
    public static Dictionary<string, object> ToCoreWFVariables(Workflow workflow)
        => new()
        {
            ["WorkflowId"] = workflow.Id,
            ["CurrentStepOrder"] = workflow.Steps
                .First(s => s.State == WorkflowState.Running).Order,
            ["ContextPayload"] = workflow.Context,
            ["StepsCompleted"] = workflow.Steps
                .Where(s => s.State == WorkflowState.Completed)
                .Select(s => new { s.Order, s.Result })
                .ToList()
        };

    // CoreWF → 自研（回滚或并行运行时用）
    public static Workflow FromCoreWFInstance(WorkflowInstance instance) { ... }
}
```

### B.3 分阶段迁移路径（不是一次性切换）

```
阶段二（当前）                     阶段三后期                        阶段四
─────────────                    ─────────────                   ──────────
自研状态机 ──→ 引入抽象层 ──→ 新工作流用 CoreWF ──→ 全量迁移
            IStepExecutor      旧工作流保持自研       移除自研代码
            IWorkflowEngine    新旧引擎 DI 并存
            MediatR Extension
```

#### Phase 1：引抽象层（阶段二中做，不引入 CoreWF）

```
改动范围：零业务逻辑变更，只加接口层

自研状态机 ──→ IStepExecutor ←── Agent 执行逻辑
                    ↑
               唯一的耦合点

此时代码结构：
AgentPlatform.Workflow/
├── Engines/
│   └── CustomWorkflowEngine.cs       # 自研引擎（实现 IWorkflowEngine）
├── StateMachine/
│   ├── CustomStateMachine.cs         # 自研状态机核心逻辑
│   └── StateTransitions.cs
├── Steps/
│   ├── AgentStepExecutor.cs          # 实现 IStepExecutor（不依赖状态机）
│   ├── ToolCallStepExecutor.cs
│   └── CodeRunStepExecutor.cs
└── Persistence/
    └── WorkflowStateStore.cs         # 自研持久化
```

#### Phase 2：引入 CoreWF，新旧引擎 DI 并存（阶段三后期）

```csharp
// Api/Program.cs —— 两个引擎并存，通过配置决定用哪个
builder.Services.AddCustomWorkflowEngine();   // 注册自研引擎
builder.Services.AddCoreWFWorkflowEngine();    // 注册 CoreWF 引擎

// Workflow 实体上标记引擎类型
public class Workflow
{
    public string Engine { get; init; } = "custom";  // 新建的默认 "corewf"
}
```

```
迁移过程：

旧工作流 → Engine="custom" → 继续走自研状态机（稳定不动）
新工作流 → Engine="corewf" → 走 CoreWF 引擎

两类工作流共享：
  ✓ 同一套 IStepExecutor 实现
  ✓ 同一套 MediatR 领域事件
  ✓ 同一套 PostgreSQL 数据
  ✓ 同一套 Agent / Tool 聚合根
```

#### Phase 3：全量迁移，移除自研代码（阶段四）

```
1. 所有在运行中的自研工作流跑完后，标记为 Archived
2. 自研工作流模板迁移为 CoreWF 定义
3. 移除 CustomStateMachine.cs 和 CustomWorkflowEngine.cs
4. DI 注册只保留 CoreWFWorkflowEngine
5. BDD 验收用例全量回归（已写的 Reqnroll 场景不用改——F27 将 SpecFlow 迁移至 Reqnroll，Gherkin 语法 100% 兼容，`.feature` 文件无需改写）
```

### B.4 零修改清单

| 层 | 组件 | 是否需要修改 |
| :--- | :--- | :---: |
| Domain | Agent 聚合根 | ❌ |
| Domain | Workflow 聚合根 | ⚠️ 仅加 `Engine` 字段 |
| Domain | 领域事件 | ❌ |
| Domain | 值对象 | ❌ |
| Domain | 仓储接口 | ❌ |
| Application | IStepExecutor 实现 | ❌ |
| Application | MediatR EventHandler | ❌ |
| Application | ModelRouter | ❌ |
| Application | CQRS 命令/查询 | ❌ |
| Infrastructure | SemanticKernelModelClient | ❌ |
| Infrastructure | PgVectorStore | ❌ |
| Infrastructure | DockerCodeSandbox | ❌ |
| Infrastructure | RedisShortTermMemory | ❌ |
| Infrastructure | EF Core 仓储实现 | ❌ |
| Api | Controllers | ❌ |
| Api | Middleware | ❌ |
| Api | DI 注册（Program.cs） | ✅ 加引擎注册 |
| Web | React 前端 | ❌（API 接口不变） |
| **Workflow** | **状态机 + 持久化 + 引擎** | **✅ 唯一改动层** |

> **迁移只动 `AgentPlatform.Workflow` 一个项目 + `Program.cs` 的 DI 注册，其余全部零修改。** 这就是 DDD 分层 + 依赖倒置的威力——引擎是基础设施细节，领域层完全无感知。

### B.5 迁移风险与应对

| 风险 | 概率 | 影响 | 应对策略 |
| :--- | :--- | :--- | :--- |
| CoreWF 学习曲线拖慢进度 | 中 | 中 | Phase 1/2 不引入 CoreWF，先出 MVP，需要时再引入 |
| 新旧引擎并行导致数据不一致 | 低 | 高 | 共享同一个 `IWorkflowRepository`，读写路径一致，`Engine` 字段路由隔离 |
| CoreWF 持久化格式与自研不兼容 | 低 | 中 | `WorkflowStateConverter` 双向转换，迁移前写 BDD 验收场景 |
| 已有工作流迁移时中断 | 低 | 低 | 旧工作流不迁移，跑完自然退役，历史数据归档 |
| CoreWF 的 Bookmark 语义与自研状态机差异 | 中 | 中 | `IWorkflowEngine` 接口屏蔽差异，Activity 包装器处理 Bookmark 挂起/恢复 |
