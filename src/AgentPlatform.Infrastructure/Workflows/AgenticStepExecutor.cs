using System.Text.Json;
using AgentPlatform.Application.Agents.Agentic;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// Executes an <see cref="AgentPlatform.Domain.Enums.StepType.Agentic"/> DAG node: runs an autonomous ReAct control loop
/// for the configured agent + goal via <see cref="AgenticOrchestrator"/>, turning a single agent
/// into a self-driving node inside a workflow graph (hybrid orchestration per F29 §6).
/// </summary>
internal sealed class AgenticStepExecutor : IStepExecutor
{
    private readonly AgenticOrchestrator _orchestrator;
    private readonly IAgentRepository _agentRepository;
    private readonly ILogger<AgenticStepExecutor> _logger;

    public AgenticStepExecutor(
        AgenticOrchestrator orchestrator,
        IAgentRepository agentRepository,
        ILogger<AgenticStepExecutor> logger)
    {
        _orchestrator = orchestrator;
        _agentRepository = agentRepository;
        _logger = logger;
    }

    /// <summary>Legacy glob fallback — not used for DAG routing (HandlesType is set).</summary>
    public string StepType => "*";

    /// <summary>Handles the autonomous-agent node type explicitly.</summary>
    public StepType? HandlesType => AgentPlatform.Domain.Enums.StepType.Agentic;

    public async Task<StepExecutionResult> ExecuteAsync(IWorkflowExecutable step, WorkflowContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(step);

        try
        {
            var agentId = step.AssignedAgentId?.ToString();
            var goal = step.Name;

            if (!string.IsNullOrWhiteSpace(step.ConfigJson) && step.ConfigJson != "{}")
            {
                using var doc = JsonDocument.Parse(step.ConfigJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("agentId", out var a) && a.ValueKind == JsonValueKind.String)
                    agentId = a.GetString();
                if (root.TryGetProperty("goal", out var g) && g.ValueKind == JsonValueKind.String)
                    goal = g.GetString();
            }

            if (string.IsNullOrWhiteSpace(agentId))
                return StepExecutionResult.FatalFailure(
                    "Agentic node requires an agentId (step config 'agentId' or assigned agent).");
            if (string.IsNullOrWhiteSpace(goal))
                return StepExecutionResult.FatalFailure("Agentic node requires a goal (step config 'goal').");

            var agent = await _agentRepository.GetByIdAsync(Guid.Parse(agentId), ct);
            if (agent is null)
                return StepExecutionResult.FatalFailure($"Agent '{agentId}' not found.");

            _logger.LogInformation("Running agentic node for agent {AgentId} (goal: {Goal})", agentId, goal);
            var result = await _orchestrator.RunGoalAsync(goal!, agent, Guid.NewGuid(), ct);

            var artifact = JsonSerializer.Serialize(new
            {
                iterations = result.Iterations,
                trace = result.Trace.Select(t => new { t.Iteration, t.ToolName, t.Success }).ToList()
            });

            return StepExecutionResult.Success(
                result.FinalAnswer,
                artifact,
                tokenUsage: new TokenUsage(result.TotalTokensIn, result.TotalTokensOut));
        }
        catch (AgentIterationLimitExceededException ex)
        {
            _logger.LogWarning(ex, "Agentic node hit iteration limit");
            return StepExecutionResult.FatalFailure($"Agent exceeded max iterations ({ex.Message}).");
        }
        catch (OperationCanceledException)
        {
            return StepExecutionResult.RetryableFailure("Agentic step was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agentic step failed");
            return StepExecutionResult.RetryableFailure(ex.Message);
        }
    }
}
