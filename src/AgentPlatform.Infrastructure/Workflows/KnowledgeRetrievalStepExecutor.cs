using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// 知识库检索节点执行器（<see cref="StepType.Knowledge"/>）。
/// 从配置的知识库向量集合检索相关片段，结果作为节点输出（下游节点可见为 artifact）。
/// 配置（存于 <c>WorkflowNode.ConfigJson</c>）：
/// <c>knowledgeBaseId</c>（优先，按租户校验）、<c>collectionName</c>（直接集合名）、
/// <c>query</c>（显式查询；缺省则拼接上游 artifact）、<c>topK</c>、<c>minScore</c>。
/// </summary>
internal sealed class KnowledgeRetrievalStepExecutor : IStepExecutor
{
    private readonly ILogger<KnowledgeRetrievalStepExecutor> _logger;
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository;
    private readonly IVectorStore _vectorStore;
    private readonly RagSettings _ragSettings;

    public KnowledgeRetrievalStepExecutor(
        ILogger<KnowledgeRetrievalStepExecutor> logger,
        IKnowledgeBaseRepository knowledgeBaseRepository,
        IVectorStore vectorStore,
        IOptions<RagSettings> ragSettings)
    {
        _logger = logger;
        _knowledgeBaseRepository = knowledgeBaseRepository;
        _vectorStore = vectorStore;
        _ragSettings = ragSettings.Value;
    }

    /// <summary>兜底 glob（不应被命中，因为显式 HandlesType 优先）。</summary>
    public string StepType => "*";

    /// <summary>显式处理知识检索节点。</summary>
    public StepType? HandlesType => AgentPlatform.Domain.Enums.StepType.Knowledge;

    public async Task<StepExecutionResult> ExecuteAsync(IWorkflowExecutable step, WorkflowContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(ctx);

        try
        {
            var config = ParseConfig(step.ConfigJson);

            var collectionName = await ResolveCollectionAsync(config, ctx.TenantId, ct);
            if (collectionName is null)
                return StepExecutionResult.FatalFailure("未配置知识库（需 knowledgeBaseId 或 collectionName）");

            var query = config.Query;
            if (string.IsNullOrWhiteSpace(query))
                query = BuildQueryFromUpstream(ctx);
            if (string.IsNullOrWhiteSpace(query))
                return StepExecutionResult.FatalFailure("无可检索内容：请在节点配置 query，或连接上游节点提供上下文");

            var topK = config.TopK ?? _ragSettings.DefaultTopK;
            var minScore = config.MinScore ?? _ragSettings.DefaultMinScore;

            _logger.LogInformation(
                "知识检索节点 {StepName}：集合={Collection}，topK={TopK}，minScore={MinScore}",
                step.Name, collectionName, topK, minScore);

            var docs = await _vectorStore.SearchAsync(collectionName, query, ctx.TenantId, topK, minScore, ct);
            if (docs.Count == 0)
            {
                _logger.LogWarning("知识检索节点 {StepName}：未检索到相关片段", step.Name);
                return StepExecutionResult.Success("", SerializeArtifact([], []));
            }

            var chunks = docs.Select(d => d.Content).ToArray();
            var sources = docs.Select(d => d.DocumentId).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()!;
            var output = string.Join("\n", chunks);

            _logger.LogInformation("知识检索节点 {StepName}：检索到 {Count} 个片段", step.Name, chunks.Length);
            return StepExecutionResult.Success(output, SerializeArtifact(chunks, sources));
        }
        catch (OperationCanceledException)
        {
            return StepExecutionResult.RetryableFailure("知识检索被取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "知识检索节点 {StepName} 失败：{Message}", step.Name, ex.Message);
            return StepExecutionResult.RetryableFailure(ex.Message);
        }
    }

    private async Task<string?> ResolveCollectionAsync(KnowledgeNodeConfig config, Guid tenantId, CancellationToken ct)
    {
        if (config.KnowledgeBaseId is { } kbId)
        {
            var kb = await _knowledgeBaseRepository.GetByIdAsync(kbId, ct);
            if (kb is null || kb.TenantId != tenantId)
                return null;
            return kb.CollectionName;
        }

        return string.IsNullOrWhiteSpace(config.CollectionName) ? null : config.CollectionName;
    }

    private static string BuildQueryFromUpstream(WorkflowContext ctx)
    {
        var parts = ctx.Artifacts.Values
            .Where(a => !string.IsNullOrWhiteSpace(a.Content))
            .OrderBy(a => a.StepOrder)
            .Select(a => a.Content)
            .ToArray();
        return string.Join("\n", parts).Trim();
    }

    private static KnowledgeNodeConfig ParseConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return new KnowledgeNodeConfig(null, null, null, null, null);

        try
        {
            var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;
            Guid? kbId = null;
            if (root.TryGetProperty("knowledgeBaseId", out var kbProp) && kbProp.ValueKind == JsonValueKind.String
                && Guid.TryParse(kbProp.GetString(), out var parsed))
            {
                kbId = parsed;
            }

            string? collection = root.TryGetProperty("collectionName", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString() : null;
            string? query = root.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String
                ? q.GetString() : null;
            int? topK = root.TryGetProperty("topK", out var tk) && tk.TryGetInt32(out var tki) ? tki : null;
            double? minScore = root.TryGetProperty("minScore", out var ms) && ms.TryGetDouble(out var msd) ? msd : null;

            return new KnowledgeNodeConfig(kbId, collection, query, topK, minScore);
        }
        catch (JsonException)
        {
            return new KnowledgeNodeConfig(null, null, null, null, null);
        }
    }

    private static string SerializeArtifact(string[] chunks, string[] sources) =>
        JsonSerializer.Serialize(new { retrievedChunks = chunks, sources });

    private sealed record KnowledgeNodeConfig(
        Guid? KnowledgeBaseId,
        string? CollectionName,
        string? Query,
        int? TopK,
        double? MinScore);
}
