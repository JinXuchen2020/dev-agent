using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.ValueObjects;

namespace AgentPlatform.Domain.Aggregates.Conversations;

/// <summary>
/// Represents a conversation aggregate root, managing a sequence of messages between
/// users and agents, tracking total token usage, and maintaining lifecycle state.
/// </summary>
public sealed class Conversation : ITenantScoped, IAggregateRoot
{
    private readonly List<Message> _messages = [];
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>
    /// Gets the unique identifier of the conversation.
    /// </summary>
    public Guid Id { get; private init; }

    /// <summary>
    /// Gets or sets the optional identifier of the associated workflow.
    /// </summary>
    public Guid? WorkflowId { get; private set; }

    /// <summary>
    /// Gets a read-only list of messages that belong to this conversation.
    /// </summary>
    public IReadOnlyList<Message> Messages => _messages;

    /// <summary>
    /// Gets or sets the cumulative token usage across all messages in the conversation.
    /// </summary>
    public TokenUsage TotalTokenUsage { get; private set; } = null!; // EF Core proxy

    /// <summary>
    /// Gets or sets the current lifecycle status of the conversation.
    /// </summary>
    public ConversationStatus Status { get; private set; }

    /// <summary>
    /// Gets the unique identifier of the tenant that owns this conversation.
    /// </summary>
    public Guid TenantId { get; private init; }

    /// <summary>
    /// Gets the UTC timestamp when the conversation was created.
    /// </summary>
    public DateTime CreatedAt { get; private init; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the conversation was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Gets the collection of domain events raised by this conversation aggregate.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private Conversation() { }

    private void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>
    /// Clears all pending domain events from this conversation aggregate.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>
    /// Initializes a new instance of the <see cref="Conversation"/> class.
    /// </summary>
    /// <param name="id">The unique identifier of the conversation.</param>
    /// <param name="tenantId">The unique identifier of the tenant that owns the conversation.</param>
    /// <param name="workflowId">The optional identifier of the associated workflow.</param>
    public Conversation(Guid id, Guid tenantId, Guid? workflowId = null)
    {
        Id = id;
        TenantId = tenantId;
        WorkflowId = workflowId;
        TotalTokenUsage = new TokenUsage(0, 0);
        Status = ConversationStatus.Active;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>
    /// Adds a message to the conversation and accumulates its token usage into the conversation total.
    /// </summary>
    /// <param name="message">The message to add.</param>
    public void AddMessage(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _messages.Add(message);
        UpdatedAt = DateTime.UtcNow;
        if (message.TokenUsage != null)
        {
            TotalTokenUsage = new TokenUsage(
                TotalTokenUsage.PromptTokens + message.TokenUsage.PromptTokens,
                TotalTokenUsage.CompletionTokens + message.TokenUsage.CompletionTokens);
        }
    }

    /// <summary>
    /// Closes the conversation, preventing further messages from being added.
    /// </summary>
    public void Close()
    {
        Status = ConversationStatus.Closed;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Archives the conversation for historical reference.
    /// </summary>
    public void Archive()
    {
        Status = ConversationStatus.Archived;
        UpdatedAt = DateTime.UtcNow;
    }
}
