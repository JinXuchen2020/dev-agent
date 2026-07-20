# 04. EF Core 映射聚合根的坑与对策

> 目标：DDD 的聚合根设计（私有 setter、只读集合、值对象）和 EF Core 的映射机制（反射构造、影子属性、OwnsMany）之间有大量摩擦。这篇文章总结所有踩过的坑。

> **一句话**：EF Core 映射 DDD 的摩擦全在「封装性 vs 反射构造」，靠私有构造、`UsePropertyAccessMode(Field)`、`OwnsOne/OwnsMany`、影子属性解决。

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

## 复盘自测

- 只读集合 `IReadOnlyList<T>` 为什么要配合 `UsePropertyAccessMode(Field)`？
- `OwnsMany` 的影子主键为什么必须 `ValueGeneratedOnAdd()`？漏了会怎样？
- 值对象（如 `TokenUsage`）和主表同名属性时，怎么避免列名冲突？

---

## 参考代码

- `src/AgentPlatform.Infrastructure/Persistence/Configurations/AgentConfiguration.cs`
- `src/AgentPlatform.Infrastructure/Persistence/Configurations/ConversationConfiguration.cs`
- `src/AgentPlatform.Infrastructure/Persistence/Configurations/WorkflowConfiguration.cs`
- `src/AgentPlatform.Infrastructure/Persistence/AppDbContext.cs`
- `src/AgentPlatform.Infrastructure/Persistence/DatabaseInitializer.cs` — 数据库初始化和种子数据
- `src/AgentPlatform.ArchitectureTests/DddLayerTests.cs` — 自动检查每个聚合根是否有配置

---

> 注：数据库初始化与种子数据（原 §4.8）已迁入 `02-clean-architecture.md` §2.7；CQRS 查询端点（原 §4.9）与 `03-mediatr-cqrs.md` §3.7/§3.8 重复，已移除，避免重复维护。
