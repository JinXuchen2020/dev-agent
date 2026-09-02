using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workflows.Commands.UpdateWorkflow;
using AgentPlatform.Application.Workflows.Queries.GetWorkflow;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Application.Workflows.Commands.RunExistingWorkflow;

/// <summary>run-existing 统一结果（直跑 Detail / 队列态标记，F37 D2=B）。</summary>
/// <param name="Detail">完成态的工作流详情；队列超时/拒投为 null。</param>
/// <param name="Dispatch">队列投递状态（NotQueued = 既有直跑语义）。</param>
/// <param name="WorkflowId">工作流 Id。</param>
/// <param name="State">最后一次可观测状态。</param>
public sealed record ExistingWorkflowRunResult(
    WorkflowDetailResponse? Detail,
    QueueDispatchStatus Dispatch,
    Guid WorkflowId,
    WorkflowState? State);

internal sealed class RunExistingWorkflowCommandHandler
    : IRequestHandler<RunExistingWorkflowCommand, ExistingWorkflowRunResult?>
{
    private readonly IWorkflowRepository _repo;
    private readonly IOrchestrationPrimitive _primitive;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IExecutionQueue _queue;
    private readonly IWorkspaceProvider _workspaceProvider;
    private readonly DurableExecutionSettings _settings;
    private readonly ILogger<RunExistingWorkflowCommandHandler> _logger;

    public RunExistingWorkflowCommandHandler(
        IWorkflowRepository repo,
        IOrchestrationPrimitive primitive,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        IExecutionQueue queue,
        IWorkspaceProvider workspaceProvider,
        IOptions<DurableExecutionSettings> settings,
        ILogger<RunExistingWorkflowCommandHandler> logger)
    {
        _repo = repo;
        _primitive = primitive;
        _auditLogRepository = auditLogRepository;
        _unitOfWork = unitOfWork;
        _queue = queue;
        _workspaceProvider = workspaceProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ExistingWorkflowRunResult?> Handle(RunExistingWorkflowCommand request, CancellationToken ct)
    {
        var wf = await _repo.GetByIdAsync(request.Id, ct);
        if (wf is null || wf.TenantId != request.TenantId)
            return null; // 404, existence not disclosed

        if (wf.CurrentState is WorkflowState.Running)
            throw new WorkflowConflictException($"Workflow '{wf.Id}' is already running.");

        // Re-run semantics: if the workflow already finished (Completed/Failed/RolledBack)
        // or was paused, restart it from a clean state. A still-Pending workflow (e.g. a
        // freshly edited draft) needs no reset, and a Running one is rejected above.
        // RunAsync only accepts Pending/Running, so reset any terminal/paused state first.
        if (wf.CurrentState is not (WorkflowState.Pending or WorkflowState.Running))
        {
            wf.Reset();
            _repo.Update(wf);
        }

        // F37 决策 D2=B：队列模式 = 入队 + 等待终态（worker 在独立 scope 重放执行）。
        if (QueuedRunSupport.IsQueueMode(_settings))
        {
            await _unitOfWork.SaveChangesAsync(ct);
            var job = QueuedRunSupport.BuildJob(
                wf.Id, request.TenantId, _workspaceProvider.GetWorkspaceId(), request.Preset,
                requestingUserId: request.RequestingUserId);
            var queued = await QueuedRunSupport.EnqueueAndWaitAsync(
                _queue, _repo, _settings, job, _logger, ct);

            return new ExistingWorkflowRunResult(
                queued.Workflow is null ? null : GetWorkflowQuery.ToDetailResponse(queued.Workflow),
                queued.Dispatch,
                wf.Id,
                queued.State);
        }

        // The orchestration primitive handles per-step persistence internally.
        var result = await _primitive.RunAsync(wf, request.Preset, ct);

        var auditLog = AuditLog.Record(
            tenantId: result.TenantId,
            action: AgentPlatform.Domain.Aggregates.AuditLogs.AuditActionType.RunWorkflow,
            entity: "Workflow",
            entityId: result.Id,
            details: $"Re-ran workflow '{result.Name}'");
        _auditLogRepository.Add(auditLog);

        return new ExistingWorkflowRunResult(
            GetWorkflowQuery.ToDetailResponse(result), QueueDispatchStatus.NotQueued, result.Id, result.CurrentState);
    }
}
