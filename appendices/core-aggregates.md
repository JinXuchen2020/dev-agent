## 附录 A：核心聚合字段与状态枚举

> [← 返回主文档](../AGENT_PLATFORM_BLUEPRINT.md)

> **用途**：锁定 AI 生成代码时的领域模型字段一致性。AI Agent 在任何阶段创建实体类时，必须严格匹配以下定义。

### A.1 枚举定义

```csharp
// Domain/Enums/AgentRole.cs
public enum AgentRole
{
    RequirementsAnalyst,   // 需求分析师
    ProductManager,        // 产品经理
    Architect,             // 架构师
    Developer,             // 开发工程师
    Tester,                // 测试工程师
    TechnicalWriter        // 技术文档工程师
}

// Domain/Enums/AgentStatus.cs
public enum AgentStatus
{
    Active,                // 活跃
    Inactive,              // 停用
    Error                  // 异常
}

// Domain/Enums/WorkflowState.cs
public enum WorkflowState
{
    Pending,               // 等待启动
    Running,               // 执行中
    Paused,                // 已暂停（可恢复）
    Completed,             // 已完成
    Failed,                // 失败（可重试）
    RolledBack             // 已回滚
}

// Domain/Enums/ConversationStatus.cs
public enum ConversationStatus
{
    Active,                // 进行中
    Closed,                // 已关闭
    Archived               // 已归档
}

// Domain/Enums/MessageRole.cs
public enum MessageRole
{
    User,                  // 用户消息
    Agent,                 // Agent 回复
    System,                // 系统提示
    Tool                   // 工具调用结果
}

// Domain/Enums/AuditActionType.cs
public enum AuditActionType
{
    ModelCall,             // 模型调用
    CodeExecute,           // 代码执行
    ConfigChange,          // 配置变更
    Login,                 // 登录
    KeyRotation,           // Key 轮换
    WorkflowStart,         // 工作流启动
    WorkflowComplete,      // 工作流完成
    ToolCall               // 工具调用（含 Skill / MCP）
}

// Domain/Enums/ToolSource.cs  ← 附录 F 引入：能力来源
public enum ToolSource
{
    NativeTool,            // 原生 C# 函数（进程内执行）
    SkillPackage,          // Semantic Kernel Plugin（多个函数打包）
    McpServer              // MCP 协议（外部服务，JSON-RPC）
}
```

### A.2 聚合根

```csharp
// Domain/Aggregates/Agents/Agent.cs
public class Agent                                // 聚合根
{
    private readonly List<ToolDefinition> _tools = [];
    private readonly List<string> _skillPackageNames = [];
    private readonly List<string> _mcpServerNames = [];

    public Guid Id { get; private init; }
    public string Name { get; private set; }                      // Agent 名称
    public AgentRole Role { get; private init; }                  // 角色枚举
    public ModelEndpoint ModelEndpoint { get; private set; }      // 模型端点（值对象）
    public string SystemPrompt { get; private set; }              // 系统提示词
    public IReadOnlyList<ToolDefinition> Tools => _tools;          // 关联的原生工具列表
    public IReadOnlyList<string> SkillPackages => _skillPackageNames;  // SK Plugin 名称列表
    public IReadOnlyList<string> McpServers => _mcpServerNames;        // MCP Server 名称列表
    public AgentStatus Status { get; private set; }               // 状态枚举
    public Guid TenantId { get; private init; }                    // 租户 ID（多租户隔离）
    public DateTime CreatedAt { get; private init; }
    public DateTime UpdatedAt { get; private set; }
}

// Domain/Aggregates/Workflows/Workflow.cs
public class Workflow                             // 聚合根
{
    private readonly List<WorkflowStep> _steps = [];

    public Guid Id { get; private init; }
    public string Name { get; private set; }                      // 工作流名称
    public IReadOnlyList<WorkflowStep> Steps => _steps;           // 步骤列表
    public WorkflowState CurrentState { get; private set; }       // 当前状态
    public Dictionary<string, Agent> AgentAssignments { get; }    // 步骤 → Agent 映射
    public string Context { get; private set; }                   // 工作流上下文（JSON）
    public Guid TenantId { get; private init; }
    public DateTime CreatedAt { get; private init; }
    public DateTime UpdatedAt { get; private set; }
}

// Domain/Aggregates/Conversations/Conversation.cs
public class Conversation                        // 聚合根
{
    private readonly List<Message> _messages = [];

    public Guid Id { get; private init; }
    public Guid? WorkflowId { get; private set; }                 // 关联工作流（可为空）
    public IReadOnlyList<Message> Messages => _messages;
    public TokenUsage TotalTokenUsage { get; private set; }       // 累计 token 用量（值对象）
    public ConversationStatus Status { get; private set; }
    public Guid TenantId { get; private init; }
    public DateTime CreatedAt { get; private init; }
}

// Domain/Aggregates/ToolDefinitions/ToolDefinition.cs
public class ToolDefinition                      // 聚合根
{
    public Guid Id { get; private init; }
    public string Name { get; private init; }                     // 工具名称
    public string Description { get; private init; }              // 工具描述（供模型理解）
    public string ParametersSchema { get; private set; }          // 参数 JSON Schema
    public string HandlerName { get; private init; }                // 处理器标识（用于 DI 解析）
    public bool IsEnabled { get; private set; }

    // ↓↓↓ 附录 F 引入：能力来源扩展 ↓↓↓
    public ToolSource Source { get; private init; } = ToolSource.NativeTool;  // 能力来源
    public string? EndpointUrl { get; private init; }             // MCP Server 地址（仅 McpServer）
    public string? SkillPluginName { get; private init; }         // SK Plugin 名（仅 SkillPackage）
}
```

// Domain/Aggregates/Users/User.cs
public class User : ITenantScoped, IAggregateRoot   // 用户聚合（F2 新增）
{
    public Guid Id { get; private init; }
    public Guid TenantId { get; private init; }                // 租户 ID（多租户隔离）
    public string Email { get; private set; }                  // 登录邮箱（租户内唯一）
    public string PasswordHash { get; private set; }           // PBKDF2：$pbkdf2$<iter>$<saltB64>$<hashB64>
    public string Role { get; private set; }                    // Admin / Operator / Viewer
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private init; }
}

### A.3 实体（非聚合根）

```csharp
// Domain/Aggregates/Conversations/Message.cs
public class Message                             // 实体（属于 Conversation 聚合）
{
    public Guid Id { get; private init; }
    public MessageRole Role { get; private init; }                 // 消息角色
    public string Content { get; private init; }                   // 消息内容
    public string? ToolCalls { get; private init; }                // 工具调用 JSON（可选）
    public TokenUsage? TokenUsage { get; private init; }           // 本次消息的 token 消耗
    public DateTime CreatedAt { get; private init; }
}

// Domain/Aggregates/Workflows/WorkflowStep.cs
public class WorkflowStep                        // 实体（属于 Workflow 聚合）
{
    public Guid Id { get; private init; }
    public int Order { get; private init; }                       // 步骤序号
    public string StepName { get; private init; }                 // 步骤名称
    public Guid? AssignedAgentId { get; private set; }            // 分配的 Agent ID
    public WorkflowState State { get; private set; }              // 步骤状态
    public string? Result { get; private set; }                   // 步骤结果（JSON）
    public string? ErrorDetail { get; private set; }              // 错误详情（失败时）
}

// Infrastructure/Audit/AuditLog.cs（基础设施层，非领域概念）
public class AuditLog
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid UserId { get; init; }
    public AuditActionType ActionType { get; init; }
    public string ResourceType { get; init; }                     // Agent / Workflow / ApiKey / Sandbox
    public Guid ResourceId { get; init; }
    public string Detail { get; init; }                            // JSON 操作上下文
    public string IpAddress { get; init; }
    public DateTime CreatedAt { get; init; }
}
```

### A.4 值对象

```csharp
// Domain/ValueObjects/TokenUsage.cs
public record TokenUsage(int PromptTokens, int CompletionTokens)
{
    public int TotalTokens => PromptTokens + CompletionTokens;
}

// Domain/ValueObjects/ModelEndpoint.cs
public record ModelEndpoint(
    string Provider,        // openai / azure / vllm / deepseek / ...
    string ModelName,       // gpt-4o / deepseek-chat / qwen ...
    string ApiUrl,          // https://api.openai.com/v1 ...
    int MaxTokens = 4096,
    double Temperature = 0.7
);

// Domain/ValueObjects/Money.cs
public record Money(decimal Amount, string Currency = "USD")
{
    public static Money Zero => new(0);
    public static Money operator +(Money a, Money b)
        => new(a.Amount + b.Amount, a.Currency);
}
```

### A.5 EF Core 映射注意事项

聚合根和实体使用私有构造函数 + `private set` + `= null!` 模式与 EF Core 配合：

1. **私有构造函数**：EF Core 通过反射调用私有构造函数创建实体实例，属性通过 `private set` 设值。
2. **`= null!`**：抑制 CS8618 非空警告，因为 EF Core 在构造后会立即设置属性值。
3. **集合属性**：`IReadOnlyList<T>` 配合私有 `List<T>` 支持字段，需在 `OnModelCreating` 中配置 `UsePropertyAccessMode(PropertyAccessMode.Field)` 让 EF Core 直接写支持字段。
4. **值对象**：`record` 类型默认不可变，EF Core 2.1+ 支持作为拥有类型（`OwnsOne`）映射。
5. **枚举**：默认映射为 int，可通过 `HasConversion<string>()` 映射为字符串以增强可读性。

```csharp
// 示例：IReadOnlyList 的 EF Core 配置
builder.OwnsMany(a => a.Messages, msg =>
{
    msg.WithOwner().HasForeignKey("ConversationId");
    msg.Property<Guid>("Id");
    msg.HasKey("Id");
});
```

### A.6 仓储接口

```csharp
// Domain/Repositories/IAgentRepository.cs
public interface IAgentRepository
{
    Task<Agent?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Agent>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<Agent>> GetByRoleAsync(AgentRole role, CancellationToken ct = default);
    void Add(Agent agent);
    void Update(Agent agent);
}

// Domain/Repositories/IWorkflowRepository.cs
public interface IWorkflowRepository
{
    Task<Workflow?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Workflow>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
    void Add(Workflow workflow);
    void Update(Workflow workflow);
}

// Domain/Repositories/IConversationRepository.cs
public interface IConversationRepository
{
    Task<Conversation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Conversation?> GetByIdWithMessagesAsync(Guid id, CancellationToken ct = default);
    void Add(Conversation conversation);
}

// Domain/Repositories/IUserRepository.cs（F2 新增）
public interface IUserRepository
{
    Task<User?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct = default);
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    void Add(User user);
}
```

### A.6 工作流引擎抽象（自研 / CoreWF 双引擎共用）

> **用途**：为后期从自研状态机无缝迁移到 CoreWF 预留抽象层。所有 Agent 执行逻辑只依赖 `IStepExecutor`，不直接依赖任何具体引擎。

```csharp
// Application/Abstractions/StepContext.cs
public record StepContext(
    Guid WorkflowId,
    Guid StepId,
    int StepOrder,
    string InputPayload          // JSON 格式的步骤输入（上一个步骤的输出）
);

// Application/Abstractions/StepOutcome.cs
public enum StepOutcome
{
    Success,                     // 成功
    Failed,                      // 失败（可重试）
    NeedsRetry,                   // 需要重试（临时性错误）
    NeedsRollback                 // 需要回滚（不可恢复错误）
}

// Application/Abstractions/StepResult.cs
public record StepResult(
    StepOutcome Outcome,
    string OutputPayload,         // JSON 格式的步骤输出
    TokenUsage? TokenUsage,
    string? ErrorMessage,
    int? RetryCount = 0
);

// Application/Abstractions/IStepExecutor.cs
public interface IStepExecutor
{
    /// <summary>
    /// 执行单个工作流步骤。
    /// 自研状态机和 CoreWF 都通过此接口调度 Agent 执行，
    /// 实现类不感知上层引擎类型。
    /// </summary>
    Task<StepResult> ExecuteAsync(
        StepContext context,
        CancellationToken ct = default);
}

// Application/Abstractions/IWorkflowEngine.cs
public interface IWorkflowEngine
{
    /// <summary>启动一个工作流</summary>
    Task StartAsync(Workflow workflow, CancellationToken ct = default);

    /// <summary>暂停一个运行中的工作流</summary>
    Task PauseAsync(Guid workflowId, CancellationToken ct = default);

    /// <summary>恢复一个已暂停的工作流</summary>
    Task ResumeAsync(Guid workflowId, CancellationToken ct = default);

    /// <summary>重试失败的步骤</summary>
    Task RetryAsync(Guid workflowId, int stepOrder, CancellationToken ct = default);

    /// <summary>回滚到指定步骤</summary>
    Task RollbackAsync(Guid workflowId, int targetStepOrder, CancellationToken ct = default);

    /// <summary>获取当前工作流状态快照</summary>
    Task<WorkflowStateSnapshot> GetStateAsync(Guid workflowId, CancellationToken ct = default);
}

// Application/Abstractions/WorkflowStateSnapshot.cs
public record WorkflowStateSnapshot(
    Guid WorkflowId,
    WorkflowState CurrentState,
    int CurrentStepOrder,
    IReadOnlyList<StepSnapshot> Steps
);

public record StepSnapshot(
    Guid StepId,
    int Order,
    string StepName,
    WorkflowState State,
    string? Result,
    string? ErrorDetail
);
```
