using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Infrastructure.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// Executes a workflow step/node by calling the configured LLM model via <see cref="IModelClient"/>.
/// Builds a role-based prompt from the step context and workflow history (Blueprint C.3).
/// Falls back to any node whose <see cref="IStepExecutor.HandlesType"/> is not explicitly handled.
/// </summary>
internal sealed class AgentCallStepExecutor : IStepExecutor
{
    private readonly ILogger<AgentCallStepExecutor> _logger;
    private readonly IModelClient _modelClient;
    private readonly StateMachineSettings _settings;

    public AgentCallStepExecutor(
        ILogger<AgentCallStepExecutor> logger,
        IModelClient modelClient,
        IOptions<StateMachineSettings> settings)
    {
        _logger = logger;
        _modelClient = modelClient;
        _settings = settings.Value;
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
            var messages = BuildPrompt(step, ctx);
            var modelId = _settings.DefaultModelId;

            _logger.LogDebug("Calling model {ModelId} for step {StepName}", modelId, step.Name);
            var response = await _modelClient.ChatAsync(modelId, messages, ct);

            var output = response.Content;
            var artifact = JsonSerializer.Serialize(new
            {
                step = step.Name,
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

    private List<ChatMessage> BuildPrompt(IWorkflowExecutable step, WorkflowContext ctx)
    {
        var systemPrompt = $"You are an agent executing the step \"{step.Name}\"." +
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
