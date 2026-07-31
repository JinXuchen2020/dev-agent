using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Aggregates.Workflows.Events;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// The single orchestration primitive for the platform (Blueprint C.2).
/// Facade that routes to the appropriate preset orchestrator (SequentialOrchestrator /
/// NegotiationOrchestrator) and handles per-step persistence, domain event
/// publishing, lifecycle operations, and TTL-based eviction of stale CTS entries.
/// </summary>
internal sealed class OrchestrationPrimitive : IOrchestrationPrimitive
{
    private readonly IWorkflowRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDomainEventBus _eventBus;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrchestrationPrimitive> _logger;
    private readonly StateMachineSettings _settings;
    private readonly SequentialOrchestrator _sequential;
    private readonly NegotiationOrchestrator _negotiation;

    // ──────────── Static Fields ────────────

    /// <summary>
    /// Wraps a CancellationTokenSource with a last-access timestamp for TTL eviction.
    /// </summary>
    private sealed class RunningCtsEntry
    {
        public CancellationTokenSource Cts { get; }
        public DateTime LastAccessUtc { get; set; }

        public RunningCtsEntry(CancellationTokenSource cts)
        {
            Cts = cts;
            LastAccessUtc = DateTime.UtcNow;
        }
    }

    // Tracks in-flight runs so PauseAsync can interrupt them (Blueprint C.7: mid-execution pause).
    // Uses a wrapper with last-access timestamps for TTL-based stale-entry eviction.
    private static readonly ConcurrentDictionary<Guid, RunningCtsEntry> s_runningCts = new();

    // Tracks the preset chosen on first RunAsync so Resume/Retry reuse the SAME preset
    // instead of re-sniffing Context (which could flip the preset mid-lifecycle).
    // Cold-start fallback (e.g. after a process restart) still uses DetectPreset.
    private static readonly ConcurrentDictionary<Guid, OrchestrationPreset> s_resolvedPresets = new();

    // Timer for periodic TTL eviction of stale s_runningCts entries (every 30 min).
    // Entries idle for more than 1 hour or already cancelled are removed.
    private static readonly Timer s_evictionTimer = new(
        EvictStaleCtsEntries, null, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));

    /// <summary>
    /// Evicts CTS entries that are either already cancelled or have been idle
    /// (last access) for more than 1 hour. Runs on a background timer every 30 min.
    /// </summary>
    private static void EvictStaleCtsEntries(object? state)
    {
        var threshold = DateTime.UtcNow.AddHours(-1);
        foreach (var kvp in s_runningCts)
        {
            if (kvp.Value.Cts.IsCancellationRequested || kvp.Value.LastAccessUtc < threshold)
            {
                if (s_runningCts.TryRemove(kvp.Key, out var entry))
                {
                    entry.Cts.Dispose();
                }
            }
        }
    }

    // ──────────── Constructor ────────────

    public OrchestrationPrimitive(
        IWorkflowRepository repository,
        IUnitOfWork unitOfWork,
        IDomainEventBus eventBus,
        IServiceProvider serviceProvider,
        IOptions<StateMachineSettings> settings,
        ILogger<OrchestrationPrimitive> logger,
        IVectorStore vectorStore,
        ITokenCounter tokenCounter)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _eventBus = eventBus;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _settings = settings.Value;

        // Create internal orchestrators, delegating the heavy lifting.
        _sequential = new SequentialOrchestrator(
            repository, unitOfWork, eventBus, serviceProvider,
            logger, _settings, vectorStore, tokenCounter);

        _negotiation = new NegotiationOrchestrator(
            repository, unitOfWork, eventBus, serviceProvider,
            logger, _settings, vectorStore, tokenCounter);
    }

    // ──────────── Public API (IOrchestrationPrimitive) ────────────

    public async Task<Workflow> RunAsync(Workflow workflow, OrchestrationPreset preset, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        // Remember the chosen preset for this workflow so Resume/Retry stay stable.
        s_resolvedPresets[workflow.Id] = preset;

        if (workflow.CurrentState != WorkflowState.Pending && workflow.CurrentState != WorkflowState.Running)
            throw new InvalidOperationException(
                $"Workflow {workflow.Id} cannot be started (state: {workflow.CurrentState})");

        // Ensure the workflow is persisted before starting.
        // Only INSERT when this is a brand-new, untracked aggregate (the create-and-run
        // path). When the workflow was loaded from the repository (re-run / resume / retry)
        // it is already tracked, and calling Add would re-insert the row and violate the
        // primary-key unique constraint (DbUpdateException: UNIQUE constraint failed: Workflows.Id).
        workflow.SetState(WorkflowState.Running);
        var alreadyTracked = _unitOfWork.GetTrackedAggregates()
            .OfType<Workflow>()
            .Any(w => w.Id == workflow.Id);
        if (!alreadyTracked)
        {
            _repository.Add(workflow);
        }
        await _unitOfWork.SaveChangesAsync(ct);

        // Publish WorkflowStarted
        await _eventBus.PublishAsync(
            new WorkflowStarted(workflow.Id, workflow.Name, workflow.TenantId), ct);

        // Register a cancellable source so PauseAsync can interrupt an in-flight run (Blueprint C.7).
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var entry = new RunningCtsEntry(linkedCts);
        s_runningCts[workflow.Id] = entry;
        var linkedToken = linkedCts.Token;
        try
        {
            switch (preset)
            {
                case OrchestrationPreset.Sequential:
                    await _sequential.RunSequentialAsync(workflow, linkedToken);
                    break;
                case OrchestrationPreset.Negotiation:
                    await _negotiation.RunNegotiationAsync(workflow, linkedToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout (inner CTS fired) OR external Pause (linked CTS cancelled) —
            // both leave the workflow resumable.
            _logger.LogWarning("Workflow {WorkflowId} execution interrupted (timeout or pause)", workflow.Id);
            workflow.SetState(WorkflowState.Paused);
            _repository.Update(workflow);
            await _unitOfWork.SaveChangesAsync(ct);
            throw;
        }
        catch (OperationCanceledException)
        {
            // Explicit cancellation (outer ct was cancelled mid-execution)
            // Set Paused so the workflow is resumable — otherwise it stays Running permanently.
            _logger.LogWarning("Workflow {WorkflowId} execution was cancelled by caller", workflow.Id);
            workflow.SetState(WorkflowState.Paused);
            _repository.Update(workflow);
            // Use CancellationToken.None because ct is already cancelled
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            s_runningCts.TryRemove(workflow.Id, out _);
            linkedCts.Dispose();
        }

        return workflow;
    }

    public async Task PauseAsync(Guid workflowId, CancellationToken ct = default)
    {
        var workflow = await LoadWorkflowAsync(workflowId, ct);
        if (workflow.CurrentState != WorkflowState.Running)
            throw new InvalidOperationException($"Workflow {workflowId} is not running (state: {workflow.CurrentState})");

        // Interrupt the in-flight run, if one is registered, so an executing step aborts
        // immediately instead of finishing (Blueprint C.7: mid-execution pause).
        if (s_runningCts.TryGetValue(workflowId, out var entry))
        {
            entry.LastAccessUtc = DateTime.UtcNow;
            entry.Cts.Cancel();
        }

        workflow.SetState(WorkflowState.Paused);
        _repository.Update(workflow);
        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("Workflow {WorkflowId} paused", workflowId);
    }

    public async Task<Workflow> ResumeAsync(Guid workflowId, CancellationToken ct = default)
    {
        var workflow = await LoadWorkflowAsync(workflowId, ct);
        if (workflow.CurrentState != WorkflowState.Paused)
            throw new InvalidOperationException($"Workflow {workflowId} is not paused (state: {workflow.CurrentState})");

        // Reload preset — prefer the one chosen on first RunAsync (stable across resume).
        var preset = ResolvePreset(workflow, workflowId);
        workflow.SetState(WorkflowState.Running);
        _repository.Update(workflow);
        await _unitOfWork.SaveChangesAsync(ct);

        await RunAsync(workflow, preset, ct);
        return workflow;
    }

    public async Task RetryStepAsync(Guid workflowId, int stepOrder, CancellationToken ct = default)
    {
        var workflow = await LoadWorkflowAsync(workflowId, ct);
        var failedStep = workflow.Steps.FirstOrDefault(s => s.Order == stepOrder)
            ?? throw new InvalidOperationException($"Step order {stepOrder} not found in workflow {workflowId}");

        // Reset the step to Pending (clear prior error state)
        failedStep.SetState(WorkflowState.Pending);
        _repository.Update(workflow);
        await _unitOfWork.SaveChangesAsync(ct);

        // Re-run from the reset step, reusing the stable preset.
        workflow.SetState(WorkflowState.Running);
        await RunAsync(workflow, ResolvePreset(workflow, workflowId), ct);
    }

    public async Task RollbackToAsync(Guid workflowId, int targetStepOrder, CancellationToken ct = default)
    {
        var workflow = await LoadWorkflowAsync(workflowId, ct);

        // Precise rollback: reset target and all subsequent steps to Pending (Blueprint C.6)
        foreach (var step in workflow.Steps.Where(s => s.Order >= targetStepOrder))
        {
            step.SetState(WorkflowState.Pending);
        }

        workflow.SetState(WorkflowState.RolledBack);
        _repository.Update(workflow);
        await _unitOfWork.SaveChangesAsync(ct);

        await _eventBus.PublishAsync(
            new WorkflowRolledBack(workflow.Id, workflow.Name,
                $"Rolled back to step order {targetStepOrder}",
                $"Precise rollback to step order {targetStepOrder}", workflow.TenantId), ct);

        _logger.LogInformation("Workflow {WorkflowId} rolled back to step order {TargetStepOrder} (Blueprint C.6 precision rollback)",
            workflowId, targetStepOrder);
    }

    public async Task<WorkflowStateSnapshot> GetStateAsync(Guid workflowId, CancellationToken ct = default)
    {
        var workflow = await LoadWorkflowAsync(workflowId, ct);
        var lastCompletedStep = workflow.Steps
            .Where(s => s.State == WorkflowState.Completed)
            .OrderByDescending(s => s.Order)
            .FirstOrDefault();

        var stepSnapshots = workflow.Steps.Select(s => new StepSnapshot(
            s.Id, s.Order, s.StepName, s.State, s.Result, s.ErrorDetail)).ToArray();

        return new WorkflowStateSnapshot(
            workflowId,
            workflow.CurrentState,
            lastCompletedStep?.Order ?? 0,
            stepSnapshots);
    }

    // ──────────── Preset Resolution ────────────

    private OrchestrationPreset ResolvePreset(Workflow workflow, Guid workflowId)
    {
        // Prefer the preset recorded when the workflow first ran; fall back to
        // heuristic sniffing only for cold starts (in-memory cache lost on restart).
        return s_resolvedPresets.TryGetValue(workflowId, out var cached)
            ? cached
            : DetectPreset(workflow);
    }

    private static OrchestrationPreset DetectPreset(Workflow workflow)
    {
        // Cold-start fallback ONLY (ResolvePreset prefers the cached choice).
        // Heuristic: a structured marker in Context selects negotiation; otherwise
        // default to sequential (the fast path). Anchored to the exact JSON key so a
        // step name/result containing the word "negotiation" cannot trigger it.
        return workflow.Context?.Contains("\"preset\":\"negotiation\"", StringComparison.OrdinalIgnoreCase) == true
            ? OrchestrationPreset.Negotiation
            : OrchestrationPreset.Sequential;
    }

    // ──────────── Helpers ────────────

    private async Task<Workflow> LoadWorkflowAsync(Guid workflowId, CancellationToken ct)
    {
        return await _repository.GetByIdAsync(workflowId, ct)
            ?? throw new InvalidOperationException($"Workflow {workflowId} not found");
    }
}
