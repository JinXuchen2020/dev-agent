using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ExecutionLogs;
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
/// publishing, lifecycle operations, and durable execution with crash recovery (F30).
/// </summary>
internal sealed class OrchestrationPrimitive : IOrchestrationPrimitive
{
    private readonly IWorkflowRepository _repository;
    private readonly IRunningExecutionRepository _runningExecutionRepository;
    private readonly IExecutionLogRepository _executionLogRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDomainEventBus _eventBus;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrchestrationPrimitive> _logger;
    private readonly StateMachineSettings _settings;
    private readonly SequentialOrchestrator _sequential;
    private readonly NegotiationOrchestrator _negotiation;

    // ──────────── Instance Fields ────────────

    /// <summary>
    /// Unique identifier for this process instance (used for lease ownership).
    /// </summary>
    private readonly string _instanceId;

    /// <summary>
    /// Lease TTL for durable execution (default 5 minutes).
    /// </summary>
    private readonly TimeSpan _leaseTtl;

    // Tracks the preset chosen on first RunAsync so Resume/Retry reuse the SAME preset
    // instead of re-sniffing Context (which could flip the preset mid-lifecycle).
    // Cold-start fallback (e.g. after a process restart) still uses DetectPreset.
    private static readonly ConcurrentDictionary<Guid, OrchestrationPreset> s_resolvedPresets = new();

    // ──────────── Constructor ────────────

    public OrchestrationPrimitive(
        IWorkflowRepository repository,
        IRunningExecutionRepository runningExecutionRepository,
        IExecutionLogRepository executionLogRepository,
        IUnitOfWork unitOfWork,
        IDomainEventBus eventBus,
        IServiceProvider serviceProvider,
        IOptions<StateMachineSettings> settings,
        IOptions<DurableExecutionSettings> durableSettings,
        ILogger<OrchestrationPrimitive> logger,
        IVectorStore vectorStore,
        ITokenCounter tokenCounter)
    {
        _repository = repository;
        _runningExecutionRepository = runningExecutionRepository;
        _executionLogRepository = executionLogRepository;
        _unitOfWork = unitOfWork;
        _eventBus = eventBus;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _settings = settings.Value;
        _durableSettings = durableSettings.Value;

        // Generate unique instance ID for this process (used for lease ownership)
        _instanceId = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..8]}";
        _leaseTtl = TimeSpan.FromMinutes(_durableSettings.LeaseTtlMinutes);

        // Create internal orchestrators, delegating the heavy lifting.
        _sequential = new SequentialOrchestrator(
            repository, executionLogRepository, unitOfWork, eventBus, serviceProvider,
            logger, _settings, vectorStore, tokenCounter, _durableSettings);

        _negotiation = new NegotiationOrchestrator(
            repository, unitOfWork, eventBus, serviceProvider,
            logger, _settings, vectorStore, tokenCounter);
    }

    private readonly DurableExecutionSettings _durableSettings;

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
        workflow.SetState(WorkflowState.Running);
        var alreadyTracked = _unitOfWork.GetTrackedAggregates()
            .OfType<Workflow>()
            .Any(w => w.Id == workflow.Id);
        if (!alreadyTracked)
        {
            _repository.Add(workflow);
        }
        await _unitOfWork.SaveChangesAsync(ct);

        // Create or update RunningExecution record (acquire lease)
        var runningExec = await _runningExecutionRepository.GetByWorkflowIdAsync(workflow.Id, ct);
        if (runningExec == null)
        {
            runningExec = RunningExecution.Create(workflow.Id, workflow.TenantId, _instanceId, _leaseTtl);
            _runningExecutionRepository.Add(runningExec);
        }
        else
        {
            // Attempt to acquire lease (will succeed if expired or same instance)
            if (!runningExec.TryAcquireLease(_instanceId, _leaseTtl))
            {
                throw new InvalidOperationException(
                    $"Workflow {workflow.Id} is already running on another instance (lease held by {runningExec.InstanceId})");
            }
            runningExec.SetWorkflowState(WorkflowState.Running);
            _runningExecutionRepository.Update(runningExec);
        }
        await _unitOfWork.SaveChangesAsync(ct);

        // Publish WorkflowStarted
        await _eventBus.PublishAsync(
            new WorkflowStarted(workflow.Id, workflow.Name, workflow.TenantId), ct);

        // Register a cancellable source so PauseAsync can interrupt an in-flight run (Blueprint C.7).
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var linkedToken = linkedCts.Token;

        try
        {
            switch (preset)
            {
                case OrchestrationPreset.Sequential:
                    await _sequential.RunSequentialAsync(workflow, linkedToken, _runningExecutionRepository, _instanceId, _leaseTtl);
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
            await HandleInterruptionAsync(workflow, runningExec, ct);
            throw;
        }
        catch (OperationCanceledException)
        {
            // Explicit cancellation (outer ct was cancelled mid-execution)
            _logger.LogWarning("Workflow {WorkflowId} execution was cancelled by caller", workflow.Id);
            await HandleInterruptionAsync(workflow, runningExec, CancellationToken.None);
            throw;
        }
        finally
        {
            linkedCts.Dispose();
        }

        return workflow;
    }

    /// <summary>
    /// Resumes a workflow from its persisted checkpoint (called by WorkflowScheduler after crash recovery).
    /// </summary>
    internal async Task<Workflow> ResumeFromCheckpointAsync(Guid workflowId, CancellationToken ct = default)
    {
        var workflow = await LoadWorkflowAsync(workflowId, ct);
        if (workflow.CurrentState != WorkflowState.Running)
            throw new InvalidOperationException($"Workflow {workflowId} is not in Running state (state: {workflow.CurrentState})");

        // Reload preset — prefer the one chosen on first RunAsync (stable across resume).
        var preset = ResolvePreset(workflow, workflowId);

        // Acquire lease for this instance
        var runningExec = await _runningExecutionRepository.GetByWorkflowIdAsync(workflowId, ct);
        if (runningExec == null)
        {
            runningExec = RunningExecution.Create(workflow.Id, workflow.TenantId, _instanceId, _leaseTtl);
            _runningExecutionRepository.Add(runningExec);
        }
        else if (!runningExec.TryAcquireLease(_instanceId, _leaseTtl))
        {
            throw new InvalidOperationException(
                $"Workflow {workflowId} lease could not be acquired (held by {runningExec.InstanceId})");
        }
        await _unitOfWork.SaveChangesAsync(ct);

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var linkedToken = linkedCts.Token;

        try
        {
            switch (preset)
            {
                case OrchestrationPreset.Sequential:
                    await _sequential.RunSequentialAsync(workflow, linkedToken, _runningExecutionRepository, _instanceId, _leaseTtl, resumeFromCheckpoint: true);
                    break;
                case OrchestrationPreset.Negotiation:
                    // Negotiation orchestrator doesn't support checkpoint resume yet; fall back to normal run
                    await _negotiation.RunNegotiationAsync(workflow, linkedToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Workflow {WorkflowId} checkpoint resume interrupted", workflow.Id);
            await HandleInterruptionAsync(workflow, runningExec, ct);
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Workflow {WorkflowId} checkpoint resume cancelled by caller", workflow.Id);
            await HandleInterruptionAsync(workflow, runningExec, CancellationToken.None);
            throw;
        }
        finally
        {
            linkedCts.Dispose();
        }

        return workflow;
    }

    public async Task PauseAsync(Guid workflowId, CancellationToken ct = default)
    {
        var workflow = await LoadWorkflowAsync(workflowId, ct);
        if (workflow.CurrentState != WorkflowState.Running)
            throw new InvalidOperationException($"Workflow {workflowId} is not running (state: {workflow.CurrentState})");

        // Update RunningExecution to Paused (releases lease implicitly)
        var runningExec = await _runningExecutionRepository.GetByWorkflowIdAsync(workflowId, ct);
        if (runningExec != null)
        {
            runningExec.Pause();
            _runningExecutionRepository.Update(runningExec);
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

        workflow.SetState(WorkflowState.Running);
        await RunAsync(workflow, ResolvePreset(workflow, workflowId), ct);
    }

    public async Task RollbackToAsync(Guid workflowId, int targetStepOrder, CancellationToken ct = default)
    {
        var workflow = await LoadWorkflowAsync(workflowId, ct);

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

    public async Task<DebugStepResult> DebugStepAsync(Guid workflowId, Blackboard blackboard, CancellationToken ct = default)
    {
        var workflow = await LoadWorkflowAsync(workflowId, ct);
        var runningExec = await _runningExecutionRepository.GetByWorkflowIdAsync(workflowId, ct);
        if (runningExec != null && runningExec.WorkflowState == WorkflowState.Running)
            throw new InvalidOperationException($"Workflow {workflowId} is currently running; debug-step is not allowed.");

        return await _sequential.DebugStepAsync(workflow, blackboard, ct);
    }

    public async Task<WorkflowState> DebugResumeAsync(Guid workflowId, Blackboard blackboard, CancellationToken ct = default)
    {
        var workflow = await LoadWorkflowAsync(workflowId, ct);
        var runningExec = await _runningExecutionRepository.GetByWorkflowIdAsync(workflowId, ct);
        if (runningExec != null && runningExec.WorkflowState == WorkflowState.Running)
            throw new InvalidOperationException($"Workflow {workflowId} is currently running; debug-resume is not allowed.");

        return await _sequential.DebugResumeAsync(workflow, blackboard, ct);
    }

    public async Task<DebugStepResult> DebugRetryNodeAsync(Guid workflowId, Guid nodeId, Blackboard blackboard, CancellationToken ct = default)
    {
        var workflow = await LoadWorkflowAsync(workflowId, ct);
        var runningExec = await _runningExecutionRepository.GetByWorkflowIdAsync(workflowId, ct);
        if (runningExec != null && runningExec.WorkflowState == WorkflowState.Running)
            throw new InvalidOperationException($"Workflow {workflowId} is currently running; debug-retry is not allowed.");

        return await _sequential.DebugRetryNodeAsync(workflow, nodeId, blackboard, ct);
    }

    // ──────────── Internal Helpers ────────

    private async Task HandleInterruptionAsync(Workflow workflow, RunningExecution? runningExec, CancellationToken ct)
    {
        workflow.SetState(WorkflowState.Paused);
        _repository.Update(workflow);

        if (runningExec != null)
        {
            runningExec.Pause();
            _runningExecutionRepository.Update(runningExec);
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // If ct is cancelled, use CancellationToken.None to persist the Paused state
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
    }

    private OrchestrationPreset ResolvePreset(Workflow workflow, Guid workflowId)
    {
        return s_resolvedPresets.TryGetValue(workflowId, out var cached)
            ? cached
            : DetectPreset(workflow);
    }

    private static OrchestrationPreset DetectPreset(Workflow workflow)
    {
        return workflow.Context?.Contains("\"preset\":\"negotiation\"", StringComparison.OrdinalIgnoreCase) == true
            ? OrchestrationPreset.Negotiation
            : OrchestrationPreset.Sequential;
    }

    private async Task<Workflow> LoadWorkflowAsync(Guid workflowId, CancellationToken ct)
    {
        return await _repository.GetByIdAsync(workflowId, ct)
            ?? throw new InvalidOperationException($"Workflow {workflowId} not found");
    }
}