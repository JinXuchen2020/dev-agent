# 03. MediatR 管道 + CQRS + 工作单元模式

> 目标：理解 Command/Query 分离、Pipeline Behavior 的执行顺序、UnitOfWork 怎么自动 SaveChanges。

---

## 3.1 CQRS 的基本概念

**C（Command）：写操作** — Create, Update, Delete
**Q（Query）：读操作** — 查询

```csharp
// Command 写操作 → 触发 UnitOfWork → SaveChanges
public sealed record CreateAgentCommand(string Name, AgentRole Role, ModelEndpoint Endpoint)
    : ICommand<AgentResponse>;

// Query 读操作 → 不触发 SaveChanges
public sealed record GetAgentQuery(Guid AgentId)
    : IRequest<AgentResponse?>;
```

**为什么分离？**

| | 合在一起（传统 Service） | 分离（CQRS） |
|--|------------------------|-------------|
| 一个方法同时做查询和写入 | 常见，难复用 | 明确分开 |
| Pipeline Behavior | 没法区分读写 | Command 走 UnitOfWork，Query 不走 |
| 性能优化 | 查询可能需要不同数据形状 | 可以分别优化 |

---

## 3.2 ICommand\<T\> 标记接口：核心设计

```csharp
// Application/Abstractions/ICommand.cs
public interface ICommand<TResponse> : IRequest<TResponse> { }
```

`IRequest<TResponse>` 是 MediatR 的接口。`ICommand<TResponse>` 是业务标记。

**为什么需要这个标记？**

因为 `UnitOfWorkBehavior` **只想拦截写操作**：

```csharp
// Application/Behaviors/UnitOfWorkBehavior.cs
public sealed class UnitOfWorkBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>  // ← 只有 Command 才触发！
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var response = await next(ct);           // 先执行 Handler
        await _unitOfWork.SaveChangesAsync(ct);   // 再 SaveChanges
        return response;
    }
}
```

Query 没有 `ICommand` 标记，所以不走这个 Behavior，不会触发 `SaveChanges`。

**注册时的约束：**

```csharp
services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
});

// 只对 ICommand<TResponse> 生效
services.AddOpenBehavior(typeof(UnitOfWorkBehavior<,>));
```

---

## 3.3 Pipeline Behavior 执行顺序

当一个 Command 被发送时，经过的管道：

```
Mediator.Send(command)
    │
    ▼
UnitOfWorkBehavior.Handle()  ← 先执行 Handler
    │
    ▼
CreateAgentCommandHandler.Handle()  ← 业务逻辑
    │  ● 创建聚合根
    │  ● 调用仓储.Add()
    │  ● 聚合根内部收集了 _domainEvents
    │
    ▼
UnitOfWorkBehavior 继续
    │  ● 从聚合根取出 _domainEvents
    │  ● 通过 IDomainEventBus 分发
    │  ● SaveChangesAsync
    │
    ▼
响应返回给 Controller
```

**事件分发和 SaveChanges 的顺序：**

```
先发事件？          先提交？
先发事件的好处：      先提交的好处：
  Handler 能看到最新状态    事务内操作更安全
  但此时数据还没落库         但事件分发在事务外

这个项目的选择：先发事件，再提交
```

---

## 3.4 Controller → MediatR → Handler 的全链路

```csharp
// Api/Controllers/AgentsController.cs
[ApiController]
[Route("api/v1/agents")]
public sealed class AgentsController : ControllerBase
{
    private readonly IMediator _mediator;  // 只注入 IMediator！

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAgentRequest request)
    {
        var command = new CreateAgentCommand(request.Name, request.Role, request.ModelEndpoint);
        var result = await _mediator.Send(command);  // → Command → Handler → UoW
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }
}

// Application/Agents/Commands/CreateAgent/Handler.cs
internal sealed class CreateAgentCommandHandler : IRequestHandler<CreateAgentCommand, AgentResponse>
{
    private readonly IAgentRepository _repository;

    public async Task<AgentResponse> Handle(CreateAgentCommand command, CancellationToken ct)
    {
        var agent = new Agent(command.Name, command.Role, command.ModelEndpoint);
        _repository.Add(agent);
        return agent.ToDto();
        // 不需要在这里 SaveChangesAsync！
        // UnitOfWorkBehavior 会自动调用
    }
}
```

**Controller 不应该做的事情：**

```csharp
// ❌ Controller 直接调 Application Service
public class AgentsController
{
    private readonly IAgentRepository _repo; // ← 绕过 MediatR

    [HttpPost]
    public async Task<IActionResult> Create(CreateAgentRequest request)
    {
        var agent = new Agent(request.Name, request.Role, request.ModelEndpoint);
        _repo.Add(agent);
        await _repo.SaveChangesAsync(); // ← 手动 SaveChanges，没有 UnitOfWork
        return Ok();
    }
}
```

**问题：**
- 没有 UnitOfWork 管理事务
- 没有 Pipeline Behavior 的统一日志和异常处理
- 领域事件不会自动分发
- Controller 知道太多基础设施细节

---

## 3.5 Domain 事件如何桥接到 MediatR

Domain 层不依赖 MediatR，但 MediatR 负责分发事件。桥接层在 Application：

```csharp
// Application/Abstractions/IDomainEventBus.cs
public interface IDomainEventBus
{
    Task PublishAsync<T>(T domainEvent, CancellationToken ct = default)
        where T : IDomainEvent;
}

// Application/EventHandlers/DomainEventBus.cs
internal sealed class DomainEventBus : IDomainEventBus
{
    private readonly IPublisher _publisher;  // MediatR 的 IPublisher

    public async Task PublishAsync<T>(T domainEvent, CancellationToken ct = default)
        where T : IDomainEvent
    {
        // 把 IDomainEvent 包装成 MediatR 能处理的 INotification
        var notification = new DomainEventNotification<T>(domainEvent);
        await _publisher.Publish(notification, ct);
    }
}
```

**Handler 监听事件：**

```csharp
// Application/EventHandlers/AgentCreatedEventHandler.cs
internal sealed class AgentCreatedEventHandler
    : INotificationHandler<DomainEventNotification<AgentCreated>>
{
    private readonly ILogger<AgentCreatedEventHandler> _logger;

    public Task Handle(DomainEventNotification<AgentCreated> notification, CancellationToken ct)
    {
        _logger.LogInformation("Agent {Id} ({Name}) created", notification.DomainEvent.AgentId, notification.DomainEvent.Name);
        return Task.CompletedTask;
    }
}
```

---

## 3.6 对比：没有 MediatR 的项目长什么样

```
传统的 Controller → Service → Repository：
┌──────────┐    ┌──────────┐    ┌──────────────┐
│ Controller│───>│ Service  │───>│ Repository   │
│          │    │          │    │              │
│  Http    │    │ 业务逻辑 │    │ 数据库操作   │
│  路由    │    │ + 事务   │    │              │
└──────────┘    └──────────┘    └──────────────┘

问题：
1. Service 越来越胖（业务 + 事务 + 事件）
2. 没法无侵入加日志、验证、缓存
3. Controller 经常调多个 Service，事务边界模糊
```

```
用 MediatR + Pipeline Behavior：
┌──────────┐    ┌──────────────┐    ┌──────────┐    ┌──────────────┐
│ Controller│───>│ Pipeline     │───>│ Handler  │───>│ Repository   │
│          │    │ ● Validation  │    │          │    │              │
│ IMediator│    │ ● UnitOfWork  │    │ 业务逻辑 │    │ 数据库操作   │
│          │    │ ● Logging     │    │          │    │              │
└──────────┘    │ ● ...         │    └──────────┘    └──────────────┘
                └──────────────┘

好处：
1. 每个 Behavior 只做一件事
2. 可以独立开关（比如调试时关掉 UnitOfWork）
3. 新横切关注点 = 新建一个 Behavior class，不改已有代码
```

---

## 3.7 Query Handler 的实现模式

虽然 MediatR 主要用于 Command，但 Query 也可以用同样的模式实现。

### 示例：获取所有 Agents

#### 1. 创建 Query 类

```csharp
// Application/Aggregates/GetAgents/GetAgentsQuery.cs
using MediatR;
using AgentPlatform.Domain.Aggregates.Agents;

public record GetAgentsQuery : IRequest<IEnumerable<Agent>>;
```

#### 2. 创建 Query Handler

```csharp
// Application/Aggregates/GetAgents/GetAgentsQueryHandler.cs
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Repositories;
using MediatR;

internal sealed class GetAgentsQueryHandler : IRequestHandler<GetAgentsQuery, IEnumerable<Agent>>
{
    private readonly IAgentRepository _repository;
    private readonly ITenantProvider _tenantProvider;

    public GetAgentsQueryHandler(
        IAgentRepository repository,
        ITenantProvider tenantProvider)
    {
        _repository = repository;
        _tenantProvider = tenantProvider;
    }

    public async Task<IEnumerable<Agent>> Handle(GetAgentsQuery request, CancellationToken ct)
    {
        var tenantId = _tenantProvider.GetTenantId();
        return await _repository.GetByTenantAsync(tenantId, ct);
    }
}
```

#### 3. 添加 Controller 端点

```csharp
// Api/Controllers/AgentsController.cs
[HttpGet]
public async Task<IActionResult> GetAgents(CancellationToken ct)
{
    var agents = await _mediator.Send(new GetAgentsQuery(), ct);
    var responses = agents.Select(AgentResponse.From);
    return Ok(responses);
}
```

**关键点：**
- Query 不走 `UnitOfWorkBehavior`，不需要手动 `SaveChangesAsync`
- 使用 `ITenantProvider` 实现多租户数据隔离
- 返回只读集合，保持 DDD 封装

### 完整的 CQRS API 端点

#### GetAgents
```
GET /api/v1/Agents              - 获取当前租户的所有 agents
GET /api/v1/Agents/{id}         - 根据 ID 获取单个 agent
POST   /api/v1/Agents          - 创建新 agent
```

#### GetConversations
```
GET /api/v1/conversations       - 获取当前租户的所有 conversations
POST   /api/v1/conversations    - 创建新 conversation
POST /api/v1/conversations/{id}/messages - 发送消息到 conversation
GET /api/v1/conversations/cost-report   - 获取成本报告
```

### 通用模式

对于任何聚合根（Agent, Conversation, Workflow, AgentRoleDefinition），创建查询端点的步骤：

1. **创建 Query 类** `GetAllXxxQuery`：无参数，`IRequest<IEnumerable<Xxx>>`
2. **创建 Query Handler** `GetAllXxxQueryHandler`：
   - 注入 `IXxxRepository` 和 `ITenantProvider`
   - 调用 `repository.GetByTenantAsync(tenantId)`
   - 返回只读集合
3. **添加 Controller 端点** `GET /api/v1/Xxx`
4. **返回映射后的响应对象`

这种模式确保了：
- 查询只返回当前租户的数据（多租户隔离）
- 使用仓储层，符合 DDD
- 通过 MediatR 处理，遵循 CQRS

---

## 3.8 Query 和 Command 的执行路径对比

### Command 执行路径

```
POST /api/v1/Agents
    │
    ▼
AgentsController.CreateAgent()
    │
    ▼
IMediator.Send(CreateAgentCommand)
    │
    ▼
UnitOfWorkBehavior.Handle()  ← 预处理
    │
    ▼
CreateAgentCommandHandler.Handle()
    │  ● 创建聚合根（Add）
    │  ● 收集领域事件
    │
    ▼
UnitOfWorkBehavior 继续
    │  ● 分发领域事件
    │  ● SaveChangesAsync ← 提交事务
    │
    ▼
返回 AgentResponse
```

### Query 执行路径

```
GET /api/v1/Agents
    │
    ▼
AgentsController.GetAgents()
    │
    ▼
IMediator.Send(GetAgentsQuery)
    │
    ▼
GetAgentsQueryHandler.Handle()
    │  ● 从仓储查询数据
    │
    ▼
返回 IEnumerable<Agent>
    │
    ▼
Controller 映射为响应对象
```

**区别：**
- Command：走 `UnitOfWorkBehavior`，触发 `SaveChangesAsync`
- Query：不触发任何 Behavior，直接执行 Handler

---

## 参考代码

- `src/AgentPlatform.Application/Abstractions/ICommand.cs`
- `src/AgentPlatform.Application/Behaviors/UnitOfWorkBehavior.cs`
- `src/AgentPlatform.Application/EventHandlers/DomainEventBus.cs`
- `src/AgentPlatform.Api/Controllers/AgentsController.cs`
- `src/AgentPlatform.Application/Agents/Commands/CreateAgent/`
- `src/AgentPlatform.Application/Aggregates/GetAgents/GetAgentsQuery.cs`
- `src/AgentPlatform.Application/Conversations/Queries/GetConversations/GetConversationsQuery.cs`
- `src/AgentPlatform.ArchitectureTests/DddLayerTests.cs`（检查 Controller 是否只注入了 IMediator）

