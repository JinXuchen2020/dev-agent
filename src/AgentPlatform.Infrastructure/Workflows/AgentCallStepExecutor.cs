using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Workflows;

internal sealed class AgentCallStepExecutor : IStepExecutor
{
    private readonly ILogger<AgentCallStepExecutor> _logger;
    private readonly IAgentOrchestrator _orchestrator;

    public AgentCallStepExecutor(
        ILogger<AgentCallStepExecutor> logger,
        IAgentOrchestrator orchestrator)
    {
        _logger = logger;
        _orchestrator = orchestrator;
    }

    public string StepType => "*";

    public async Task<StepExecutionResult> ExecuteAsync(WorkflowStep step, Workflow context, CancellationToken ct)
    {
        _logger.LogInformation("Executing step: {StepName} (workflow: {WorkflowId})", step.StepName, context.Id);

        try
        {
            var result = await _orchestrator.RunCollaborationAsync(step.StepName, ct);
            return new StepExecutionResult(true, result, null);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Step {StepName} was cancelled", step.StepName);
            return new StepExecutionResult(false, null, "Step execution was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step {StepName} failed with error: {Message}", step.StepName, ex.Message);
            return new StepExecutionResult(false, null, ex.Message);
        }
    }
}
