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
/// <para>
/// 设计为可变：编排器在单次运行中维护单一 <see cref="Blackboard"/> 实例并贯穿全程，
/// <see cref="AgentPlatform.Domain.Enums.StepType.Variable"/> 节点的 set 操作原地写入，使跨节点读写生效。
/// </para>
/// </summary>
public sealed class Blackboard
{
    /// <summary>
    /// F36：agent 分区键前缀约定。<c>agent:{agentId}:{key}</c> 归属该 agent 的分区；
    /// 无前缀的键 = 全局共享区（既有行为不变）。
    /// </summary>
    public const string AgentKeyPrefix = "agent:";

    private readonly Dictionary<string, string> _data;

    private Blackboard(Dictionary<string, string> data) => _data = data;

    /// <summary>Creates an empty blackboard.</summary>
    public static Blackboard Empty => new(new Dictionary<string, string>());

    /// <summary>Reads a value from the blackboard.</summary>
    public string? Get(string key) =>
        _data.TryGetValue(key, out var value) ? value : null;

    /// <summary>Writes or overwrites a value on the blackboard (in-place, mutates this instance).</summary>
    public Blackboard Set(string key, string value)
    {
        _data[key] = value;
        return this;
    }

    /// <summary>All entries currently stored.</summary>
    public IReadOnlyDictionary<string, string> Entries => _data;

    /// <summary>
    /// Writes a key inside the given agent's partition (F36 D1=A 软分区)：实际存储键为
    /// <c>agent:{agentId}:{key}</c>，对其他 agent 的分区视图不可见。
    /// </summary>
    public Blackboard SetInPartition(Guid agentId, string key, string value) =>
        Set(PartitionKey(agentId, key), value);

    /// <summary>Reads a key from the given agent's partition; returns null when absent.</summary>
    public string? GetFromPartition(Guid agentId, string key) =>
        Get(PartitionKey(agentId, key));

    /// <summary>
    /// F36：agent 的上下文视图 = 全局共享区（无 <c>agent:</c> 前缀的键）+ 该 agent 自己的分区。
    /// agent 步骤的 prompt 注入只用此视图，杜绝其他 agent 的中间产物无声泄漏进本 agent 的 prompt。
    /// 自分区键返回时<b>剥离</b> <c>agent:{agentId}:</c> 前缀（prompt 可读性）；全局键原样。
    /// </summary>
    public IReadOnlyDictionary<string, string> GetPartitionView(Guid agentId)
    {
        var ownPrefix = PartitionKey(agentId, string.Empty);
        var view = new Dictionary<string, string>();
        foreach (var (key, value) in _data)
        {
            if (key.StartsWith(ownPrefix, StringComparison.Ordinal))
            {
                view[key[ownPrefix.Length..]] = value;
            }
            else if (!key.StartsWith(AgentKeyPrefix, StringComparison.Ordinal))
            {
                view[key] = value;
            }
        }

        return view;
    }

    /// <summary>
    /// F36：全局共享区视图（剔除所有 <c>agent:</c> 前缀键）。
    /// 未绑定 agent 的 LLM 步骤用此视图替代全量 <see cref="Entries"/>——行为对既有工作流零变化
    /// （存量数据无 agent: 键），但不会无声读到 agent 分区的中间产物。
    /// </summary>
    public IReadOnlyDictionary<string, string> GetGlobalView()
    {
        var view = new Dictionary<string, string>();
        foreach (var (key, value) in _data)
        {
            if (!key.StartsWith(AgentKeyPrefix, StringComparison.Ordinal))
            {
                view[key] = value;
            }
        }

        return view;
    }

    /// <summary>Builds the storage key for a key inside an agent's partition.</summary>
    public static string PartitionKey(Guid agentId, string key) => $"{AgentKeyPrefix}{agentId}:{key}";

    /// <summary>
    /// F36 D4=A：agent 步骤最终回复的全局回写键——下游步骤（Condition / 后续 LLM）经
    /// <see cref="Get"/> 显式引用，键名带 agentId、无泄漏歧义。
    /// </summary>
    public static string AgentOutputKey(Guid agentId) => PartitionKey(agentId, "output");
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
    public required int MaxTokens { get; init; }

    /// <summary>
    /// Total estimated token count of all summaries combined.
    /// Populated by <c>ITokenCounter</c> at construction time in the orchestration layer.
    /// </summary>
    public int EstimatedTokenCount { get; init; }

    /// <summary>Empty history singleton.</summary>
    public static StepHistory Empty => new()
    {
        Summaries = new Dictionary<int, string>(),
        MaxTokens = 8000,
        EstimatedTokenCount = 0
    };
}
