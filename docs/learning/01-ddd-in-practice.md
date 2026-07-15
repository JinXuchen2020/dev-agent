# 01. DDD 在实践中的真实长相

> 目标：不是重复 DDD 书上的概念，而是回答 "DDD 的每个概念在 .NET 代码里长什么样？为什么这么写？不这么写会有什么问题？"

---

## 1.1 聚合根（Aggregate Root）：管好自己的孩子

### 书上说的

> 聚合根是唯一能被外部引用的实体，外部不能直接修改聚合内部的对象。

### 代码里的样子

```csharp
// AgentPlatform.Domain/Aggregates/Agents/Agent.cs
public sealed class Agent : IAggregateRoot, ITenantScoped
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public AgentStatus Status { get; private set; }

    private Agent() { } // EF Core 反射构造用

    public Agent(string name, AgentRole role, ModelEndpoint endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = Guid.NewGuid();
        Name = name;
        Status = AgentStatus.Active;
        ModelEndpoint = endpoint;

        _domainEvents.Add(new AgentCreated(Id, name, role));
    }

    public void Activate()
    {
        if (Status != AgentStatus.Inactive)
            throw new InvalidOperationException("Only inactive agents can be activated.");
        Status = AgentStatus.Active;
    }
}
```

**关键模式：**

| 模式 | 为什么 |
|------|--------|
| `private setter` | 状态只能通过领域方法改变，不能从外部直接赋值 |
| `private Agent()` | EF Core 反射构造需要无参构造，但不让业务代码误用 |
| `_domainEvents` | 聚合根自持领域事件，工作单元自动分发 |
| `IAggregateRoot` | 标记接口，让 UnitOfWorkBehavior 知道哪些实体需要检查事件 |

### 贫血模型 vs 富模型

**不这么写（贫血模型）：**

```csharp
// ❌ 反模式：任何人都能直接改状态
public class Agent
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public AgentStatus Status { get; set; }
}

// 在 Controller 里直接改
agent.Status = AgentStatus.Active; // 跳过验证，没人知道状态改了
```

**问题：** `Activate()` 方法里的 `if (Status != AgentStatus.Inactive)` 这个业务规则没人执行了。业务逻辑散落在 Controller、Service、测试里，改一个地方其他地方就不同步。

---

## 1.2 值对象（Value Object）：不可变、无身份、比较的是值

### 代码里的样子

```csharp
// AgentPlatform.Domain/ValueObjects/TokenUsage.cs
public sealed record TokenUsage(int Prompt, int Completion)
{
    public int TotalTokens => Prompt + Completion;
}

// AgentPlatform.Domain/ValueObjects/Money.cs
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Zero => new(0, "USD");

    public static Money operator +(Money a, Money b)
    {
        if (a.Currency != b.Currency)
            throw new InvalidOperationException("Cannot add different currencies.");
        return new Money(a.Amount + b.Amount, a.Currency);
    }

    public static bool operator <=(Money a, Money b) => a.Amount <= b.Amount && a.Currency == b.Currency;
    public static bool operator >=(Money a, Money b) => a.Amount >= b.Amount && a.Currency == b.Currency;
}
```

**为什么用 `record`：**

- `record` 自带值相等（两个 TokenUsage(100, 50) 相等）
- `init` 属性不可变（不能创建后修改）
- `record struct` 是结构体，零堆分配

**不用 `class` 的问题：**

```csharp
public class Money
{
    public decimal Amount { get; set; }
    public string Currency { get; set; }
}

var a = new Money { Amount = 10, Currency = "USD" };
var b = new Money { Amount = 10, Currency = "USD" };
a == b; // false！因为 class 默认引用比较
```

---

## 1.3 领域事件（Domain Event）：让副作用不散落

### 代码里的样子

```csharp
// Domain/Aggregates/Agents/Events/AgentCreated.cs
public sealed record AgentCreated(Guid AgentId, string Name, AgentRole Role) : IDomainEvent;

// Domain/Abstractions/IDomainEvent.cs
public interface IDomainEvent { }
```

**事件流：**

```
Agent 构造函数
  → _domainEvents.Add(new AgentCreated(...))  // 聚合根收集事件
  → 仓储 SaveChangesAsync
    → UnitOfWorkBehavior 取出所有 _domainEvents
    → 通过 IDomainEventBus 分发
    → DomainEventBus 适配器转成 MediatR INotification
    → MediatR 调用对应的 INotificationHandler
    → SaveChangesAsync 提交数据库
```

**为什么不用 MediatR 直接发布？**

因为 Domain 项目**不能依赖任何外部库**。如果 Agent 构造函数直接 `mediator.Publish(...)`：

- Domain 需要引用 `MediatR` NuGet 包
- 如果你有一天换掉 MediatR（比如用 Brighter），Domain 层也要跟着改
- 违反 "Domain 零外部依赖" 铁律

**适配器模式桥接：**

```
Domain（零依赖）      Application（知道 MediatR）
    │                        │
    │  IDomainEventBus       │
    │  ────────────────────> │
    │                        │  DomainEventBus（适配器）
    │                        │     → MediatR.IPublisher
```

---

## 1.4 仓储模式（Repository）：假装数据库不存在

### 接口在 Domain，实现在 Infrastructure

```csharp
// Domain/Repositories/IAgentRepository.cs
public interface IAgentRepository
{
    Task<Agent?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Agent>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
    void Add(Agent agent);
}

// Infrastructure/Persistence/Repositories/AgentRepository.cs
internal sealed class AgentRepository : IAgentRepository
{
    private readonly AppDbContext _context;

    public async Task<Agent?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.Agents.FirstOrDefaultAsync(a => a.Id == id, ct);

    public void Add(Agent agent) => _context.Agents.Add(agent);
}
```

**接口在 Domain 层的意义：**

1. Application 层只需要 `IAgentRepository`，不需要知道 EF Core 存在
2. 测试时可以替换成 `NSubstitute` mock，不需要真数据库
3. 如果需要从 EF Core 换成 Dapper，改 Infrastructure 层就行，Domain 和 Application 不改

**铁律：** 仓储接口定义在 `Domain/Repositories/`，实现类放在 `Infrastructure/.../Repositories/`，DI 注册在 `Infrastructure/DependencyInjection.cs`。三处各司其职。

---

## 1.5 常见误区

| 误用 | 现象 | 正确做法 |
|------|------|---------|
| 聚合根用 `public set` | 任何人都能绕过领域方法改状态 | 全部 `private set` |
| 值对象用 `class` | `a == b` 永远 false | 用 `record` |
| 领域事件在 Controller 里发布 | 事件散落，事务边界模糊 | 聚合根自持 `_domainEvents`，UoW 自动分发 |
| 仓储实现在 Application 层 | 违反依赖方向，编译不报错但架构坏了 | 实现在 Infrastructure |
| Domain 层引用第三方 NuGet | 哪天换库 Domain 跟着改 | Domain 零 PackageReference |

---

## 参考代码

- `src/AgentPlatform.Domain/Aggregates/Agents/Agent.cs`
- `src/AgentPlatform.Domain/ValueObjects/Money.cs`
- `src/AgentPlatform.Domain/Abstractions/IDomainEvent.cs`
- `src/AgentPlatform.Domain/Repositories/IAgentRepository.cs`
- `src/AgentPlatform.Infrastructure/Persistence/Repositories/AgentRepository.cs`
- `src/AgentPlatform.Application/EventHandlers/DomainEventBus.cs`
