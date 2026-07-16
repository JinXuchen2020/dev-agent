namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Unified context contract consumed by all orchestration presets (sequential / negotiation).
/// Replaces the dual-track StepContext/StepResult.OutputPayload (Blueprint Appendix C.3).
/// </summary>
public sealed record WorkflowContext
{
    /// <summary>The workflow being executed.</summary>
    public required Guid WorkflowId { get; init; }

    /// <summary>The 0-based order of the current step being dispatched.</summary>
    public required int CurrentStepOrder { get; init; }

    /// <summary>Structured artifacts from all completed steps, keyed by step name.</summary>
    public required IReadOnlyDictionary<string, StepArtifact> Artifacts { get; init; }
        = new Dictionary<string, StepArtifact>();

    /// <summary>Shared work area (Blackboard) for cross-step data exchange (C.3.1).</summary>
    public required Blackboard Blackboard { get; init; } = Blackboard.Empty;

    /// <summary>RAG-retrieved knowledge injected for the current step (F5).</summary>
    public required RetrievalContext Retrieval { get; init; } = RetrievalContext.Empty;

    /// <summary>Compressed step history with a token cap (C.3.1).</summary>
    public required StepHistory Summary { get; init; } = StepHistory.Empty;

    /// <summary>Tenant scope for multi-tenant isolation.</summary>
    public required Guid TenantId { get; init; }
}

/// <summary>
/// Structured output from a single workflow step.
/// The artifact is JSON-serializable content produced by the step (design doc, code, test report, etc.).
/// </summary>
public sealed record StepArtifact
{
    /// <summary>The step name that produced this artifact.</summary>
    public required string StepName { get; init; }

    /// <summary>The 0-based order of the step.</summary>
    public required int StepOrder { get; init; }

    /// <summary>The JSON-serialized content of the artifact.</summary>
    public required string Content { get; init; }

    /// <summary>Content type hint (e.g. "architecture", "code", "test-report", "doc").</summary>
    public required string ContentType { get; init; }

    /// <summary>When the artifact was produced.</summary>
    public DateTime ProducedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Empty artifact singleton.</summary>
    public static readonly StepArtifact Empty = new()
    {
        StepName = "", StepOrder = -1, Content = "", ContentType = ""
    };
}

/// <summary>
/// Shared work area for cross-step data exchange (Blueprint C.3.1).
/// Steps can read/write structured key-value pairs here to avoid passing full natural-language history.
/// </summary>
public sealed record Blackboard
{
    private readonly Dictionary<string, string> _data;

    private Blackboard(Dictionary<string, string> data) => _data = data;

    /// <summary>Creates an empty blackboard.</summary>
    public static Blackboard Empty => new(new Dictionary<string, string>());

    /// <summary>Reads a value from the blackboard.</summary>
    public string? Get(string key) =>
        _data.TryGetValue(key, out var value) ? value : null;

    /// <summary>Writes or overwrites a value on the blackboard.</summary>
    public Blackboard Set(string key, string value)
    {
        var copy = new Dictionary<string, string>(_data) { [key] = value };
        return new Blackboard(copy);
    }

    /// <summary>All entries currently stored.</summary>
    public IReadOnlyDictionary<string, string> Entries => _data;
}

/// <summary>
/// RAG-retrieved knowledge injected into the current step context (Blueprint F5).
/// </summary>
public sealed record RetrievalContext
{
    /// <summary>Relevant document chunks for the current step.</summary>
    public required IReadOnlyList<string> Chunks { get; init; } = [];

    /// <summary>Source references for provenance.</summary>
    public required IReadOnlyList<string> Sources { get; init; } = [];

    /// <summary>Whether any retrieval data is available.</summary>
    public bool HasContent => Chunks.Count > 0;

    /// <summary>Empty retrieval context singleton.</summary>
    public static readonly RetrievalContext Empty = new()
    {
        Chunks = [], Sources = []
    };
}

/// <summary>
/// Compressed step history with a token cap (Blueprint C.3.1).
/// Instead of passing the full natural-language history linearly,
/// each step appends a compressed summary keyed by step order.
/// </summary>
public sealed record StepHistory
{
    /// <summary>Summaries keyed by step order.</summary>
    public required IReadOnlyDictionary<int, string> Summaries { get; init; } = new Dictionary<int, string>();

    /// <summary>The maximum token budget for this history (prevents unbounded growth).</summary>
    public required int MaxTokens { get; init; } = 8000;

    /// <summary>Empty history singleton.</summary>
    public static StepHistory Empty => new()
    {
        Summaries = new Dictionary<int, string>(),
        MaxTokens = 8000
    };

    /// <summary>Estimated token count of all summaries combined.</summary>
    public int EstimatedTokenCount => Summaries.Values.Sum(s => s.Length / 2); // rough char→token estimate
}
