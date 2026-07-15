# 04. EF Core 映射聚合根的坑与对策

> 目标：DDD 的聚合根设计（私有 setter、只读集合、值对象）和 EF Core 的映射机制（反射构造、影子属性、OwnsMany）之间有大量摩擦。这篇文章总结所有踩过的坑。

---

## 4.1 私有构造函数 + `= null!`

### 问题

聚合根需要私有 setter 保证封装性，但 EF Core 从数据库读取数据时需要构造对象并设置属性。

### 解法

```csharp
public sealed class Agent : IAggregateRoot
{
    public Guid Id { get; private set; }   // 私有 setter
    public string Name { get; private set; }

    // EF Core 反射构造使用
    private Agent() { }

    // 业务代码使用的构造函数
    public Agent(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = Guid.NewGuid();
        Name = name;
    }
}
```

**要点：**
- `private Agent() { }` — EF Core 反射调用的无参构造
- 所有属性 `private set` — 业务代码必须通过领域方法修改
- 业务构造器做参数校验 — 反射构造不走这里，但 EF Core 进来的数据已经是持久化的，不需要再校验

---

## 4.2 只读集合 + `UsePropertyAccessMode.Field`

### 问题

DDD 要求聚合根暴露只读集合，不允许外部直接增删：

```csharp
public sealed class Conversation
{
    // ❌ 外部可以 conversation.Messages.Add(...)
    public List<Message> Messages { get; private set; }

    // ✅ 外部只能读，不能改
    private readonly List<Message> _messages = [];
    public IReadOnlyList<Message> Messages => _messages.AsReadOnly();
}
```

但 EF Core 默认把属性映射到字段时，会用属性（property）而不是字段（field）。对 `IReadOnlyList` 类型，EF Core 不知道怎么写入。

### 解法

```csharp
// Infrastructure/Persistence/Configurations/ConversationConfiguration.cs
internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.OwnsMany(c => c.Messages, msg =>
        {
            msg.WithOwner().HasForeignKey("ConversationId");
            msg.Property<Guid>("Id").ValueGeneratedOnAdd();
            msg.Property(m => m.Content).IsRequired();
            msg.Property(m => m.Role).HasMaxLength(50);
        });

        // 告诉 EF Core：通过 _messages 字段读写集合，不走属性
        builder.Navigation(c => c.Messages)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
```

**`UsePropertyAccessMode.Field`** 是关键。没有这行，EF Core 会尝试给 `IReadOnlyList` Add，抛异常。

---

## 4.3 值对象的 `OwnsOne` / `OwnsMany`

### 问题

值对象（`record` / `record struct`）在 DDD 里是聚合根的一部分，不是独立的表。但 EF Core 默认把它们当成独立的实体。

### 解法

```csharp
// Agent 聚合根里有一个值对象
public ModelEndpoint? ModelEndpoint { get; private set; }

// Infrastructure/Persistence/Configurations/AgentConfiguration.cs
builder.OwnsOne(a => a.ModelEndpoint, ep =>
{
    ep.Property(e => e.Provider).HasColumnName("ModelProvider").HasMaxLength(100);
    ep.Property(e => e.ModelId).HasColumnName("ModelId").HasMaxLength(100);
    ep.Property(e => e.EndpointUrl).HasColumnName("ModelEndpointUrl").HasMaxLength(500);
    ep.Property(e => e.ApiKey).HasColumnName("ModelApiKey").HasMaxLength(500);
    ep.Property(e => e.DeploymentName).HasColumnName("ModelDeploymentName").HasMaxLength(100);
});
```

**效果：**
- `ModelEndpoint` 的值对象的属性扁平化到 `Agents` 表的 5 个列
- 没有独立的 `ModelEndpoints` 表
- 每个 `Agent` 行有自己的 `ModelProvider`、`ModelId` 等列

### 集合值对象

```csharp
// Conversation 里的多条消息（值对象，不是独立实体）
builder.OwnsMany(c => c.Messages, msg =>
{
    msg.WithOwner().HasForeignKey("ConversationId");
    msg.Property<Guid>("Id").ValueGeneratedOnAdd();
    msg.Property(m => m.Content).IsRequired();
});
```

`OwnsMany` 会创建一个 `Messages` 表，但 `Message` 不是独立聚合根——它不能单独查询，只能通过 Conversation 访问。

---

## 4.4 影子属性（Shadow Properties）+ `ValueGeneratedOnAdd`

### 问题

EF Core 的 `OwnsMany` 需要外键和主键，但 DDD 的聚合根不希望暴露这些基础设施字段。

### 解法：影子属性

```csharp
builder.OwnsMany(c => c.Messages, msg =>
{
    // "ConversationId" 是影子属性 — 在 C# 类里不存在，只在数据库有
    msg.WithOwner().HasForeignKey("ConversationId");

    // "Id" 也是影子属性
    msg.Property<Guid>("Id")
       .ValueGeneratedOnAdd();  // ← 这行很关键
});
```

**如果漏了 `ValueGeneratedOnAdd()`：**

- 新建 Conversation 时，Message 的 Id 在没有显式赋值时会默认 `Guid.Empty`
- 如果有多个 Message，主键冲突，EF Core 抛异常

---

## 4.5 列名冲突 + `.HasColumnName`

### 问题

当 `OwnsOne` 或 `OwnsMany` 的值对象有和主表重名的属性时，EF Core 默认列名会冲突。

```csharp
// Conversation 聚合根
public TokenUsage? TotalTokenUsage { get; private set; }

// Message 值对象
public sealed record Message(MessageRole Role, string Content, TokenUsage? TokenUsage);
```

`TokenUsage` 里有 `Prompt` 和 `Completion`，`Conversation` 和 `Message` 都有 `TokenUsage`。数据库里列名会冲突。

### 解法

```csharp
// ConversationConfiguration.cs
builder.OwnsOne(c => c.TotalTokenUsage, usage =>
{
    usage.Property(u => u.Prompt).HasColumnName("TotalPromptTokens");
    usage.Property(u => u.Completion).HasColumnName("TotalCompletionTokens");
});

// Message 的 TokenUsage 映射（在 OwnsMany 内部）
msg.OwnsOne(m => m.TokenUsage, usage =>
{
    usage.Property(u => u.Prompt).HasColumnName("MessagePromptTokens");
    usage.Property(u => u.Completion).HasColumnName("MessageCompletionTokens");
});
```

每个列名显式指定，绝不依赖 EF Core 的默认命名约定。

---

## 4.6 完整配置示例

```csharp
internal sealed class WorkflowConfiguration : IEntityTypeConfiguration<Workflow>
{
    public void Configure(EntityTypeBuilder<Workflow> builder)
    {
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Name).IsRequired().HasMaxLength(200);
        builder.Property(w => w.Status)
               .HasConversion<string>()
               .HasMaxLength(50);

        // OwnsMany 集合
        builder.OwnsMany(w => w.Steps, step =>
        {
            step.WithOwner().HasForeignKey("WorkflowId");
            step.Property<Guid>("Id").ValueGeneratedOnAdd();
            step.Property(s => s.StepName).HasMaxLength(200);
            step.Property(s => s.Status)
                .HasConversion<string>()
                .HasMaxLength(50);
        });

        // 只读集合的字段访问模式
        builder.Navigation(w => w.Steps)
               .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
```

---

## 4.7 映射检查清单

每次加新聚合根时检查：

- [ ] `private Agent() { }` — EF Core 反射构造
- [ ] 所有集合用 `IReadOnlyList<T>` 暴露，`private readonly List<T>` 支持字段
- [ ] 有 `IEntityTypeConfiguration<T>` 类
- [ ] 值对象用 `OwnsOne` 或 `OwnsMany`
- [ ] 影子外键用 `WithOwner().HasForeignKey(...)`
- [ ] 影子主键用 `.ValueGeneratedOnAdd()`
- [ ] 集合用 `.UsePropertyAccessMode(PropertyAccessMode.Field)`
- [ ] 列名冲突用 `.HasColumnName()` 消歧

---

## 参考代码

- `src/AgentPlatform.Infrastructure/Persistence/Configurations/AgentConfiguration.cs`
- `src/AgentPlatform.Infrastructure/Persistence/Configurations/ConversationConfiguration.cs`
- `src/AgentPlatform.Infrastructure/Persistence/Configurations/WorkflowConfiguration.cs`
- `src/AgentPlatform.Infrastructure/Persistence/AppDbContext.cs`
- `src/AgentPlatform.Infrastructure/Persistence/DatabaseInitializer.cs` — 数据库初始化和种子数据
- `src/AgentPlatform.ArchitectureTests/DddLayerTests.cs` — 自动检查每个聚合根是否有配置

---

## 4.8 Database Initializer 的事务处理

### 问题：SQLite "no such table" 错误

在实现 `DatabaseInitializer` 初始化数据库和种子数据时，遇到以下错误：

```
SQLite Error 1: 'no such table: AgentRoleDefinitions'
```

尽管日志显示所有 `CREATE TABLE` 命令都成功执行，但后续查询时数据库报告表不存在。

### 根本原因

SQLite 的 `EnsureCreatedAsync()` 方法在创建表后不会立即提交事务。当 EF Core 在同一个 `DbContext` 实例上立即执行后续查询时，事务可能尚未完全提交，导致查询失败。

### 解决方案

修改 `DatabaseInitializer` 的初始化时序：

```csharp
// src/AgentPlatform.Infrastructure/Persistence/DatabaseInitializer.cs
public async Task InitializeAsync()
{
    try
    {
        _logger.LogInformation("Initializing database...");

        // 1. 显式创建数据库和表
        var created = await _context.Database.EnsureCreatedAsync();
        if (created)
        {
            _logger.LogInformation("Database created for the first time.");
        }
        else
        {
            _logger.LogInformation("Database already exists.");
        }

        // 2. 添加种子数据（查询会触发事务提交）
        await SeedDataAsync();

        // 3. 保存所有更改
        var saved = await _context.SaveChangesAsync();
        _logger.LogInformation("Database initialization completed with {Count} entities saved.", saved);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to initialize database");
        throw;
    }
}
```

**关键点：**
- 在 `SeedDataAsync()` 之前确保表创建事务已提交
- 最后调用 `SaveChangesAsync()` 确保所有更改被持久化

### 额外优化：连接字符串配置

修改连接字符串以使用 `Private` 缓存模式：

```json
// appsettings.json
"ConnectionStrings": {
  "DefaultConnection": "Data Source=agent_platform.db;Cache=Private"
}
```

**区别：**
- `Cache=Shared` - 多个 `DbContext` 共享缓存（可能导致事务隔离问题）
- `Cache=Private` - 每个 `DbContext` 使用独立缓存（推荐）

### 验证结果

初始化成功后，6 个 `AgentRoleDefinition` 种子数据正确插入：

```json
[
  {
    "id": "2dd472aa-2e0f-488b-a734-3e885174f686",
    "name": "系统架构",
    "roleCode": "architecture",
    "description": "负责系统架构设计和技术选型"
  },
  {
    "id": "2162b1ef-69ac-41c0-b5fd-120c5a348b15",
    "name": "代码实现",
    "roleCode": "development",
    "description": "负责功能开发和代码实现"
  },
  {
    "id": "d3a44722-8b6e-4452-b17e-415ef6185b39",
    "name": "文档编写",
    "roleCode": "documentation",
    "description": "负责技术文档和用户文档编写"
  },
  {
    "id": "33a73ad3-391d-425f-b812-b05d3de3c205",
    "name": "产品经理",
    "roleCode": "product",
    "description": "负责产品规划、功能设计和路线图制定"
  },
  {
    "id": "bb7ca091-1543-49b6-8e8d-67522cfdf616",
    "name": "需求分析师",
    "roleCode": "requirement",
    "description": "负责收集、分析和整理业务需求"
  },
  {
    "id": "0dd38879-5447-4be1-b4b1-ec621ad814b9",
    "name": "质量保证",
    "roleCode": "testing",
    "description": "负责功能测试和质量保证"
  }
]
```

---

## 4.9 CQRS 查询端点的实现

### 问题：缺少查询所有聚合根的 API

在实现 CQRS 架构时，只有命令端点（如 `CreateAgentCommand`），缺少查询端点（如 `GetAllAgents`、`GetAllConversations`）。

### 解决方案：MediatR 查询模式

#### 1. 创建查询类

```csharp
// Application/Aggregates/GetAgents/GetAgentsQuery.cs
using MediatR;
using AgentPlatform.Domain.Aggregates.Agents;

public record GetAgentsQuery : IRequest<IEnumerable<Agent>>;
```

#### 2. 创建查询处理器

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

**关键点：**
- 使用 `ITenantProvider` 获取当前租户 ID
- 调用仓储的 `GetByTenantAsync()` 方法
- 返回只读集合，避免外部修改

#### 3. 添加 Controller 端点

```csharp
// Api/Controllers/AgentsController.cs
using AgentPlatform.Application.Agents.Queries.GetAgents;

[ApiController]
[Route("api/v1/[controller]")]
public sealed class AgentsController : ControllerBase
{
    private readonly IMediator _mediator;

    [HttpGet]
    public async Task<IActionResult> GetAgents(CancellationToken ct)
    {
        var agents = await _mediator.Send(new GetAgentsQuery(), ct);
        var responses = agents.Select(AgentResponse.From);
        return Ok(responses);
    }
}
```

#### 4. 注册到 DI 容器

`AddApplication()` 扩展方法自动注册所有 `IRequestHandler<TRequest, TResponse>` 实现：

```csharp
// Application/DependencyInjection.cs
public static IServiceCollection AddApplication(this IServiceCollection services)
{
    var assembly = typeof(DependencyInjection).Assembly;

    services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssembly(assembly);
        cfg.AddOpenBehavior(typeof(UnitOfWorkBehavior<,>));
    });

    return services;
}
```

### 完整的 CQRS API 端点

#### GetAgents
```
GET /api/v1/Agents              - 获取当前租户的所有 agents
GET /api/v1/Agents/{id}         - 根据 ID 获取单个 agent
POST   /api/v1/Agents          - 创建新 agent
```

#### GetConversations
```
GET /api/v1/conversations       - 获取当前租户的所有 conversations ⭐ 新增
POST   /api/v1/conversations    - 创建新 conversation
POST /api/v1/conversations/{id}/messages - 发送消息到 conversation
GET /api/v1/conversations/cost-report   - 获取成本报告
```

### 验证结果

```bash
# 获取所有 agents（空数组，因为没有创建 agents）
curl http://localhost:5000/api/v1/agents
# []

# 获取所有 conversations（空数组，因为没有创建 conversations）
curl http://localhost:5000/api/v1/conversations
# []
```

### 通用模式总结

对于任何聚合根（Agent, Conversation, Workflow, AgentRoleDefinition），创建查询端点的步骤：

1. **创建查询类** `GetAllXxxQuery`：无参数，`IRequest<IEnumerable<Xxx>>`
2. **创建查询处理器** `GetAllXxxQueryHandler`：
   - 注入 `IXxxRepository` 和 `ITenantProvider`
   - 调用 `repository.GetByTenantAsync(tenantId)`
   - 返回只读集合
3. **添加 Controller 端点** `GET /api/v1/Xxx`
4. **返回映射后的响应对象**

这种模式确保了：
- 查询只返回当前租户的数据（多租户隔离）
- 使用仓储层，符合 DDD
- 通过 MediatR 处理，遵循 CQRS
