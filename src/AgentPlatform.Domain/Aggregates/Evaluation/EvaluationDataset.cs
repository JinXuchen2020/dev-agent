using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Aggregates.Evaluation;

/// <summary>
/// A tenant-scoped dataset used for regression evaluation of workflows (F24).
/// Contains a bounded collection of <see cref="EvaluationCase"/> items that are
/// replayed against a target workflow to compute a pass rate / score report.
/// </summary>
public sealed class EvaluationDataset : ITenantScoped, IAggregateRoot
{
    private readonly List<EvaluationCase> _cases = [];

    /// <summary>Gets the unique identifier of the dataset.</summary>
    public Guid Id { get; private init; }

    /// <summary>Gets the tenant that owns this dataset (auto query-filtered).</summary>
    public Guid TenantId { get; private init; }

    /// <summary>Gets the display name of the dataset.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>Gets an optional description of the dataset's purpose.</summary>
    public string? Description { get; private set; }

    /// <summary>Gets the UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; private init; }

    /// <summary>Gets the UTC timestamp of the last mutation.</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>Gets a read-only view of the evaluation cases.</summary>
    public IReadOnlyList<EvaluationCase> Cases => _cases.AsReadOnly();

    /// <inheritdoc/>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => Array.Empty<IDomainEvent>();

    /// <inheritdoc/>
    public void ClearDomainEvents() { }

    private EvaluationDataset() { }

    /// <summary>Initializes a new evaluation dataset.</summary>
    public EvaluationDataset(Guid id, Guid tenantId, string name, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        TenantId = tenantId;
        Name = name;
        Description = description;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>Replaces the dataset's name, description, and full case set (PUT semantics).</summary>
    public void Update(string name, string? description, IReadOnlyList<EvaluationCase> cases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(cases);

        Name = name;
        Description = description;
        _cases.Clear();
        _cases.AddRange(cases);
        UpdatedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// A single evaluation case: an input to replay against a workflow and the expected
/// output to compare the actual result against, using <see cref="MatchMode"/>.
/// Owned by <see cref="EvaluationDataset"/> (EF owned collection).
/// </summary>
public sealed class EvaluationCase
{
    /// <summary>Gets the unique identifier of the case.</summary>
    public Guid Id { get; private init; }

    /// <summary>Gets the input replayed as the workflow's initial context.</summary>
    public string Input { get; private init; } = null!;

    /// <summary>Gets the expected output to compare against.</summary>
    public string ExpectedOutput { get; private init; } = null!;

    /// <summary>Gets the match mode used for comparison.</summary>
    public EvaluationMatchMode MatchMode { get; private init; }

    private EvaluationCase() { }

    /// <summary>Initializes a new evaluation case.</summary>
    public EvaluationCase(Guid id, string input, string expectedOutput, EvaluationMatchMode matchMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedOutput);

        Id = id;
        Input = input;
        ExpectedOutput = expectedOutput;
        MatchMode = matchMode;
    }
}
