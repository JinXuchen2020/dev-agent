using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Shared;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// Executes a workflow step/node by invoking the LLM through <see cref="IModelRouter"/>
/// (tenant BYO credentials first, platform catalog fallback, candidate degradation — F31 ②).
/// When the node binds an agent (<see cref="IWorkflowExecutable.AssignedAgentId"/>), the agent
/// aggregate is loaded at execution time and its <c>SystemPrompt</c> drives the prompt while its
/// <c>ModelEndpoint.ModelName</c> becomes the routing preference (F31 ① runtime materialization).
/// Unbound nodes keep the legacy generic prompt and route without a model preference (acceptance #5).
/// Falls back to any node whose <see cref="IStepExecutor.HandlesType"/> is not explicitly handled.
/// </summary>
internal sealed class AgentCallStepExecutor : IStepExecutor
{
    private readonly ILogger<AgentCallStepExecutor> _logger;
    private readonly IAgentRepository _agentRepository;
    private readonly IModelRouter _modelRouter;

    public AgentCallStepExecutor(
        ILogger<AgentCallStepExecutor> logger,
        IAgentRepository agentRepository,
        IModelRouter modelRouter)
    {
        _logger = logger;
        _agentRepository = agentRepository;
        _modelRouter = modelRouter;
    }

    /// <summary>Legacy glob fallback — matches any step name.</summary>
    public string StepType => "*";

    /// <summary>Handles LLM-type nodes explicitly.</summary>
    public StepType? HandlesType => AgentPlatform.Domain.Enums.StepType.LLM;

    public async Task<StepExecutionResult> ExecuteAsync(IWorkflowExecutable step, WorkflowContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(ctx);

        _logger.LogInformation("Executing step: {StepName} (workflow: {WorkflowId})",
            step.Name, ctx.WorkflowId);

        try
        {
            // F31 ①: resolve the bound agent at execution time. The repository's EF query filter
            // enforces tenant isolation, so a cross-tenant id surfaces as not-found (fail-loud below)
            // instead of leaking another tenant's agent configuration.
            Agent? agent = null;
            if (step.AssignedAgentId.HasValue)
            {
                agent = await _agentRepository.GetByIdAsync(step.AssignedAgentId.Value, ct);
                if (agent is null)
                {
                    var missing = $"节点 '{step.Name}' 绑定的智能体 {step.AssignedAgentId.Value} 不存在或当前租户无权访问。请检查节点绑定或重新保存工作流。";
                    _logger.LogError("Step {StepName}: bound agent {AgentId} not found for tenant — failing loud",
                        step.Name, step.AssignedAgentId.Value);
                    return StepExecutionResult.RetryableFailure(missing);
                }
                _logger.LogInformation("Step {StepName} materialized agent {AgentName} (model: {Provider}/{Model})",
                    step.Name, agent.Name, agent.ModelEndpoint.Provider, agent.ModelEndpoint.ModelName);
            }

            var messages = BuildPrompt(step, ctx, agent);

            // F31 ②: route through ModelRouter — BYO credentials take priority, then the platform
            // catalog with candidate fallback. PreferredModel boosts the agent's own model to the
            // front of the candidate list without hard-failing when it is unavailable.
            var preferredModel = agent?.ModelEndpoint.ModelName;
            var request = new RoutingRequest(ctx.TenantId, messages, PreferredModel: preferredModel);
            var response = await _modelRouter.RouteAsync(request, ct);

            var output = response.Content;
            var artifact = JsonSerializer.Serialize(new
            {
                step = step.Name,
                agent = agent?.Name,
                output = Truncate(output, 500)
            });

            _logger.LogInformation("Step {StepName} completed via model {ModelId} (tokens: {Tokens})",
                step.Name, response.ModelId, response.TokenUsage?.TotalTokens ?? 0);
            return StepExecutionResult.Success(output, artifact, tokenUsage: response.TokenUsage);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Step {StepName} was cancelled", step.Name);
            return StepExecutionResult.RetryableFailure("Step execution was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step {StepName} failed: {Message}", step.Name, ex.Message);
            return StepExecutionResult.RetryableFailure(ex.Message);
        }
    }

    private List<ChatMessage> BuildPrompt(IWorkflowExecutable step, WorkflowContext ctx, Agent? agent)
    {
        // F31 ①: bound agents contribute their real SystemPrompt; unbound nodes keep the legacy
        // generic template so existing workflows behave identically (acceptance #5 backward compat).
        var systemPrompt = agent is not null
            ? agent.SystemPrompt
            : $"You are an agent executing the step \"{step.Name}\"." +
              " Produce a concise, actionable output relevant to this step.";

        var userParts = new List<string>
        {
            $"Execute workflow step: {step.Name} (order {step.Order})."
        };

        if (ctx.Artifacts.Count > 0)
        {
            var artifactLines = ctx.Artifacts.Values
                .Select(a => $"- {a.StepName}: {Truncate(a.Content, 300)}");
            userParts.Add("Previous step artifacts:\n" + string.Join("\n", artifactLines));
        }

        if (ctx.Blackboard.Entries.Count > 0)
        {
            var boardLines = ctx.Blackboard.Entries
                .Select(e => $"- {e.Key}: {Truncate(e.Value, 200)}");
            userParts.Add("Shared blackboard:\n" + string.Join("\n", boardLines));
        }

        if (ctx.Summary.Summaries.Count > 0)        {
            // F33：压缩历史（含 [semantic-recall] 召回条目）真正进入 prompt
            var summaryLines = ctx.Summary.Summaries
                .OrderBy(kv => kv.Key)
                .Select(kv => kv.Value);
            userParts.Add("History summary:\n" + string.Join("\n", summaryLines));
        }

        if (ctx.Retrieval.HasContent)
        {
            // F33：RAG/语义召回片段注入
            var retrievalLines = ctx.Retrieval.Chunks
                .Select((chunk, i) => $"- ({i + 1}) {Truncate(chunk, 300)}");
            userParts.Add("Relevant knowledge:\n" + string.Join("\n", retrievalLines));
        }

        userParts.Add("Provide your output for this step.");

        return
        [
            new ChatMessage(MessageRole.System, systemPrompt),
            new ChatMessage(MessageRole.User, string.Join("\n\n", userParts))
        ];
    }

    private static string Truncate(string value, int maxLength) =>
        StringHelpers.Truncate(value, maxLength);
}