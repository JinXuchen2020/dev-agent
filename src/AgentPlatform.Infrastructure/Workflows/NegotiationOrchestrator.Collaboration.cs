using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Workflows;

// F32 part 1: collaboration gate. Main loop lives in NegotiationOrchestrator.CollaborativeLoop.cs,
// message helpers in NegotiationOrchestrator.Messaging.cs (partial class split keeps tooling happy).

internal sealed partial class NegotiationOrchestrator
{
    private static bool IsCriticStep(IWorkflowExecutable step) =>
        step.Type == StepType.Critic
        || step.Name.Contains("critic", StringComparison.OrdinalIgnoreCase);

    private sealed record CollaborationContext(
        IAgentMessageBus Bus,
        IAgentMessageLogRepository LogRepository,
        IAgentRepository AgentRepository,
        IModelRouter Router,
        AgentCollaborationSettings Settings,
        ISelectionStrategy SelectionStrategy,
        ITerminationCondition TerminationCondition);

    // Gate: every infrastructure dependency must exist AND the workflow must have at least one
    // pending non-critic step bound to an agent. Otherwise -> null -> legacy serial loop.
    // "No collaboration subject" is an honest degradation, not a disguised fake parallelism.
    private CollaborationContext? TryBuildCollaborationContext(Workflow workflow)
    {
        var bus = _serviceProvider.GetService(typeof(IAgentMessageBus)) as IAgentMessageBus;
        var logRepo = _serviceProvider.GetService(typeof(IAgentMessageLogRepository)) as IAgentMessageLogRepository;
        var agentRepo = _serviceProvider.GetService(typeof(IAgentRepository)) as IAgentRepository;
        var router = _serviceProvider.GetService(typeof(IModelRouter)) as IModelRouter;
        if (bus is null || logRepo is null || agentRepo is null || router is null)
            return null;

        var settings = _serviceProvider.GetService(typeof(IOptions<AgentCollaborationSettings>))
            is IOptions<AgentCollaborationSettings> opts
            ? opts.Value
            : new AgentCollaborationSettings();

        using var scope = _serviceProvider.CreateScope();
        var selection = scope.ServiceProvider.GetService(typeof(ISelectionStrategy)) as ISelectionStrategy;
        var termination = scope.ServiceProvider.GetService(typeof(ITerminationCondition)) as ITerminationCondition;
        if (selection is null || termination is null)
            return null;

        workflow.EnsureGraphSynced();
        var hasBoundProposer = workflow.Steps.Any(s =>
            s.State != WorkflowState.Completed &&
            !IsCriticStep(s) &&
            s.AssignedAgentId.HasValue);
        if (!hasBoundProposer)
            return null;

        return new CollaborationContext(bus, logRepo, agentRepo, router, settings, selection, termination);
    }
}