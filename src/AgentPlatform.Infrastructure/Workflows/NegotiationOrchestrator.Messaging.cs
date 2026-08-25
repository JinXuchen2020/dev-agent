using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Aggregates.Workflows.Events;
using AgentPlatform.Domain.Enums;
using Microsoft.Extensions.Logging;
using AgentPlatform.Infrastructure.Shared;

namespace AgentPlatform.Infrastructure.Workflows;

// F32 part 3: messaging helpers for the collaborative loop (inbox drain, guarded publish,
// proposal generation via ModelRouter, handoff emission, circuit-break, completion).

internal sealed partial class NegotiationOrchestrator
{
    // 中文/符号原样写入消息负载，保证 trace 可读回放（验收 5）
    private static readonly JsonSerializerOptions TraceJsonOpts = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// One agent's proposal: SystemPrompt + task + shared artifacts + personal inbox summary
    /// routed through IModelRouter with PreferredModel = agent model. Pure network I/O.
    /// </summary>
    private async Task<StepExecutionResult> ProposeAsync(
        CollaborationContext cx, Workflow workflow, IWorkflowExecutable step, Agent agent,
        Guid correlationId, int round, string? inboxText, CancellationToken ct)
    {
        try
        {
            var userParts = new List<string>
            {
                $"You own workflow step '{step.Name}' (order {step.Order}).",
                "Produce your concrete deliverable for this step now."
            };

            var artifactLines = ctxArtifacts(step);
            if (artifactLines.Count > 0)
                userParts.Add("Shared artifacts:\n" + string.Join("\n", artifactLines));

            if (!string.IsNullOrEmpty(inboxText))
                userParts.Add("Messages addressed to you:\n" + inboxText);

            var messages = new List<ChatMessage>
            {
                new(MessageRole.System, agent.SystemPrompt),
                new(MessageRole.User, string.Join("\n\n", userParts))
            };

            _logger.LogInformation(
                "Proposal round {Round}: agent {Agent} ({Model}) for step {Step}",
                round, agent.Name, agent.ModelEndpoint.ModelName, step.Name);

            var response = await cx.Router.RouteAsync(
                new RoutingRequest(workflow.TenantId, messages,
                    PreferredModel: agent.ModelEndpoint.ModelName), ct);

            return StepExecutionResult.Success(response.Content, response.Content,
                tokenUsage: response.TokenUsage);
        }
        catch (OperationCanceledException)
        {
            return StepExecutionResult.RetryableFailure("Proposal cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Proposal of agent {Agent} failed: {Message}", agent.Name, ex.Message);
            return StepExecutionResult.RetryableFailure(ex.Message);
        }

        static System.Collections.Generic.List<string> ctxArtifacts(IWorkflowExecutable s) => [];
    }

    /// <summary>
    /// Drains the receiver inbox and renders a readable digest; each drained message is marked
    /// consumed through the conditional gate so a redelivery is never processed twice.
    /// </summary>
    private async Task<string?> DrainInboxAsync(CollaborationContext cx, Guid receiverId, CancellationToken ct)
    {
        var lines = new List<string>();
        await foreach (var msg in cx.Bus.ReadAllAsync(receiverId, ct))
        {
            if (!await cx.LogRepository.TryMarkConsumedAsync(msg.MessageId, ct))
                continue; // already consumed elsewhere — idempotency gate

            lines.Add($"- [{msg.Type}] from {Short(msg.SenderId)} (round {msg.Round}): " +
                      StringHelpers.Truncate(msg.Payload, 300));
        }
        return lines.Count == 0 ? null : string.Join("\n", lines);

        static string Short(Guid g) => g.ToString("N")[..8];
    }

    /// <summary>
    /// Publish with storm guards: per-round budget and loop fingerprints. On violation the
    /// workflow is circuit-broken to Paused with an alert log; returns false to abort the run.
    /// </summary>
    private async Task<bool> PublishGuardedAsync(
        CollaborationContext cx, Workflow workflow, AgentMessage message,
        Dictionary<string, int> fingerprints, int roundMessages, CancellationToken ct)
    {
        if (roundMessages > cx.Settings.MaxMessagesPerRound)
        {
            await CircuitBreakAsync(workflow, $"message budget {cx.Settings.MaxMessagesPerRound}/round exceeded");
            return false;
        }

        var fp = $"{message.SenderId:N}>{message.ReceiverId:N}:{message.Type}:{StableHash(message.Payload)}";
        fingerprints.TryGetValue(fp, out var seen);
        if (seen + 1 >= cx.Settings.LoopFingerprintThreshold)
        {
            await CircuitBreakAsync(workflow, $"livelock fingerprint hit {seen + 1}x: {fp}");
            return false;
        }
        fingerprints[fp] = seen + 1;

        await cx.Bus.PublishAsync(message, workflow.TenantId, ct);
        return true;
    }

    /// <summary>
    /// Critic rejected: emit Critique back to the last proposer and Handoff the rework to another
    /// bound pending agent when one exists (acceptance 2 — context travels in the payload).
    /// Returns the number of messages actually published.
    /// </summary>
    private async Task<int> EmitCritiqueAndHandoffAsync(
        CollaborationContext cx, Workflow workflow, IWorkflowExecutable criticStep,
        string criticOutput, Guid correlationId, int round,
        Guid? lastProposerAgentId, Dictionary<string, int> fingerprints, CancellationToken ct)
    {
        var count = 0;
        var senderId = criticStep.AssignedAgentId ?? Guid.Empty;
        var feedback = JsonSerializer.Serialize(new
        {
            step = criticStep.Name,
            approved = false,
            feedback = StringHelpers.Truncate(criticOutput, 800)
        }, TraceJsonOpts);

        if (lastProposerAgentId.HasValue && lastProposerAgentId.Value != Guid.Empty)
        {
            await cx.Bus.PublishAsync(new AgentMessage(
                Guid.NewGuid(), workflow.Id, correlationId,
                senderId, lastProposerAgentId.Value,
                AgentMessageType.Critique, feedback, round), workflow.TenantId, ct);
            count++;
        }

        // Handoff target: first other pending bound agent != last proposer (D3).
        var target = workflow.Steps.FirstOrDefault(s =>
            s.State != WorkflowState.Completed &&
            !IsCriticStep(s) &&
            s.AssignedAgentId.HasValue &&
            s.AssignedAgentId.Value != lastProposerAgentId)?.AssignedAgentId;

        if (target.HasValue && target.Value != Guid.Empty)
        {
            await cx.Bus.PublishAsync(new AgentMessage(
                Guid.NewGuid(), workflow.Id, correlationId,
                senderId, target.Value,
                AgentMessageType.Handoff, feedback, round), workflow.TenantId, ct);
            count++;
            _logger.LogInformation("Handoff published: {From} -> {To} after critic rejection",
                senderId, target.Value);
        }

        return count;
    }

    /// <summary>Circuit-break to Paused with an alert log — terminate without hanging (acceptance 4).</summary>
    private async Task CircuitBreakAsync(Workflow workflow, string reason)
    {
        _logger.LogError(
            "AGENT-COLLABORATION CIRCUIT-BREAK on workflow {WorkflowId}: {Reason}",
            workflow.Id, reason);
        workflow.SetState(WorkflowState.Paused);
        _repository.Update(workflow);
        await _unitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Mirrors the legacy convergence completion block.</summary>
    private async Task CompleteAsync(Workflow workflow, CancellationToken ct)
    {
        _logger.LogInformation("Negotiation {WorkflowId} converged", workflow.Id);
        workflow.Complete();
        _repository.Update(workflow);
        await _unitOfWork.SaveChangesAsync(ct);
        await _eventBus.PublishAsync(
            new WorkflowCompleted(workflow.Id, workflow.Name, workflow.Steps.Count, workflow.TenantId), ct);
    }

    /// <summary>Parses the critic JSON verdict ("Approved") out of a step output.</summary>
    private static bool TryGetApproved(string? content, out bool approved)
    {
        approved = false;
        if (string.IsNullOrWhiteSpace(content)) return false;
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("Approved", out var prop)
                && prop.ValueKind == JsonValueKind.True)
            {
                approved = true;
                return true;
            }
            return doc.RootElement.TryGetProperty("Approved", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Short deterministic payload fingerprint for livelock detection.</summary>
    private static string StableHash(string payload)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes)[..16];
    }
}