using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Aggregates.Workflows.Events;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Infrastructure.Shared;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Workflows;

// F32 part 2: collaborative negotiation round loop.
// Thread-safety contract: EF DbContext / event bus are touched ONLY sequentially; the parallel
// section is pure outbound model I/O (IModelRouter.RouteAsync) fanned out via Task.WhenAll.

internal sealed partial class NegotiationOrchestrator
{
    private async Task RunCollaborativeLoopAsync(Workflow workflow, CollaborationContext cx, CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        var round = 0;
        var lastProgressUtc = DateTime.UtcNow;
        var fingerprints = new Dictionary<string, int>(StringComparer.Ordinal);
        Guid? lastProposerAgentId = null;

        _logger.LogInformation("Negotiation {WorkflowId} collaborative mode started ({CorrelationId})",
            workflow.Id, correlationId);

        // Acceptance 3: redeliver durable unconsumed messages from prior runs into inboxes.
        await cx.Bus.RepublishUnconsumedAsync(workflow.Id, workflow.TenantId, ct);

        while (!ct.IsCancellationRequested)
        {
            round++;

            // Stall circuit-break (acceptance 4): no step progress across the window -> Paused + alert.
            if ((DateTime.UtcNow - lastProgressUtc).TotalSeconds > cx.Settings.StallTimeoutSeconds)
            {
                await CircuitBreakAsync(workflow,
                    $"round {round}: no progress for {cx.Settings.StallTimeoutSeconds}s");
                return;
            }

            var steps = workflow.Steps.Cast<IWorkflowExecutable>().ToList();
            var ctx = await BuildWorkflowContext(workflow, null, steps, ct);

            // Reuse the proven convergence contract (critic Approved / round hard cap).
            if (await cx.TerminationCondition.ShouldTerminateAsync(ctx, ct))
            {
                await CompleteAsync(workflow, ct);
                return;
            }

            var roundMessages = 0;

            // ---- Parallel proposal phase (acceptance 1): Task.WhenAll == true concurrency ----
            var proposers = steps
                .Where(s => s.State != WorkflowState.Completed && !IsCriticStep(s) && s.AssignedAgentId.HasValue)
                .Take(cx.Settings.MaxAgentsParallel)
                .ToList();

            if (proposers.Count > 0)
            {
                // Sequential EF work stays OUTSIDE the parallel section (DbContext not thread-safe).
                var inboxTexts = new Dictionary<Guid, string?>();
                foreach (var agentId in proposers.Select(p => p.AssignedAgentId!.Value).Distinct())
                    inboxTexts[agentId] = await DrainInboxAsync(cx, agentId, ct);

                var agentMap = new Dictionary<Guid, Agent>();
                foreach (var agentId in proposers.Select(p => p.AssignedAgentId!.Value).Distinct())
                {
                    var agent = await cx.AgentRepository.GetByIdAsync(agentId, ct);
                    if (agent is not null) agentMap[agentId] = agent;
                }

                // Parallel section: pure outbound model I/O, zero EF/event side effects by design.
                var tasks = proposers.Select(async s =>
                {
                    var agentId = s.AssignedAgentId!.Value;
                    if (!agentMap.TryGetValue(agentId, out var agent))
                        return (Step: s, Result: StepExecutionResult.RetryableFailure(
                            $"bound agent {agentId} not found"));
                    return (Step: s, Result: await ProposeAsync(
                        cx, workflow, s, agent, correlationId, round,
                        inboxTexts.GetValueOrDefault(agentId), ct));
                });
                var outcomes = await Task.WhenAll(tasks);

                // Sequential apply: mutations/persistence/events back on a single thread.
                foreach (var pair in outcomes)
                {
                    if (pair.Result.Outcome == StepOutcome.Success)
                    {
                        pair.Step.SetResult(pair.Result.Output ?? "");
                        lastProgressUtc = DateTime.UtcNow;
                    }
                    else if (pair.Result.Outcome == StepOutcome.NeedsIntervention)
                    {
                        workflow.SetState(WorkflowState.Paused);
                        _repository.Update(workflow);
                        await _unitOfWork.SaveChangesAsync(ct);
                        return;
                    }
                }
                _repository.Update(workflow);
                await _unitOfWork.SaveChangesAsync(ct);

                foreach (var pair in outcomes.Where(o => o.Result.Outcome == StepOutcome.Success))
                {
                    await _eventBus.PublishAsync(new StepCompleted(
                        workflow.Id, pair.Step.Id, pair.Step.Name, pair.Step.Order,
                        pair.Result.Output, pair.Result.Duration), ct);

                    lastProposerAgentId = pair.Step.AssignedAgentId;
                    roundMessages++;
                    if (lastProposerAgentId is null) continue; // never: filtered above
                    var proposal = new AgentMessage(
                        Guid.NewGuid(), workflow.Id, correlationId,
                        lastProposerAgentId!.Value, Guid.Empty,
                        AgentMessageType.Proposal,
                        JsonSerializer.Serialize(new
                        {
                            step = pair.Step.Name,
                            output = StringHelpers.Truncate(pair.Result.Output ?? "", 500)
                        }, TraceJsonOpts),
                        round);
                    if (!await PublishGuardedAsync(cx, workflow, proposal, fingerprints, roundMessages, ct))
                        return; // guard already paused the workflow and alerted
                }

                roundMessages += outcomes.Count(o => o.Result.Outcome != StepOutcome.Success);
            }

            // ---- Review / unbound-step phase: existing selection+execution drives the critic ----
            var next = await cx.SelectionStrategy.SelectNextAsync(ctx, workflow.Steps, ct);
            if (next is null)
            {
                await CompleteAsync(workflow, ct);
                return;
            }

            var stepCtx = await BuildWorkflowContext(workflow, next, steps, ct);
            var result = await ExecuteStepWithRetryAsync(workflow, next, stepCtx, ct);

            switch (result.Outcome)
            {
                case StepOutcome.Success:
                    next.SetResult(result.Output ?? "");
                    _repository.Update(workflow);
                    await _unitOfWork.SaveChangesAsync(ct);
                    await _eventBus.PublishAsync(new StepCompleted(
                        workflow.Id, next.Id, next.StepName, next.Order,
                        result.Output, result.Duration), ct);
                    lastProgressUtc = DateTime.UtcNow;

                    if (IsCriticStep(next) && TryGetApproved(result.Output, out var approved) && !approved)
                    {
                        roundMessages += await EmitCritiqueAndHandoffAsync(cx, workflow, next,
                            (result.Output ?? string.Empty), correlationId, round, lastProposerAgentId, fingerprints, ct);
                        if (roundMessages >= cx.Settings.MaxMessagesPerRound)
                        {
                            await CircuitBreakAsync(workflow, $"round {round}: message budget exceeded");
                            return;
                        }
                    }
                    break;

                case StepOutcome.FailedRollback:
                    await RollbackCompletedStepsAsync(workflow, next.Order, next.StepName,
                        result.ErrorMessage ?? "Unrecoverable in negotiation", ct);
                    return;

                case StepOutcome.NeedsIntervention:
                    workflow.SetState(WorkflowState.Paused);
                    _repository.Update(workflow);
                    await _unitOfWork.SaveChangesAsync(ct);
                    return;

                case StepOutcome.FailedRetry:
                    _logger.LogWarning("Step {StepName} failed after retry in negotiation, continuing",
                        next.StepName);
                    _repository.Update(workflow);
                    await _unitOfWork.SaveChangesAsync(ct);
                    break;
            }
        }
    }
}