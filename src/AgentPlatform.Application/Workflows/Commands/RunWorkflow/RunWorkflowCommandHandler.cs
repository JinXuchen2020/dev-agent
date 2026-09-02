using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Application.Workflows.Commands.RunWorkflow;

internal sealed class RunWorkflowCommandHandler : IRequestHandler<RunWorkflowCommand, WorkflowRunResult>
{
    private readonly IOrchestrationPrimitive _primitive;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IWorkflowRepository _workflowRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IExecutionQueue _queue;
    private readonly IWorkspaceProvider _workspaceProvider;
    private readonly DurableExecutionSettings _settings;
    private readonly ILogger<RunWorkflowCommandHandler> _logger;

    public RunWorkflowCommandHandler(
        IOrchestrationPrimitive primitive,
        IAuditLogRepository auditLogRepository,
        IWorkflowRepository workflowRepository,
        IUnitOfWork unitOfWork,
        IExecutionQueue queue,
        IWorkspaceProvider workspaceProvider,
        IOptions<DurableExecutionSettings> settings,
        ILogger<RunWorkflowCommandHandler> logger)
    {
        _primitive = primitive;
        _auditLogRepository = auditLogRepository;
        _workflowRepository = workflowRepository;
        _unitOfWork = unitOfWork;
        _queue = queue;
        _workspaceProvider = workspaceProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<WorkflowRunResult> Handle(RunWorkflowCommand request, CancellationToken ct)
    {
        var workflow = new Workflow(Guid.NewGuid(), request.Name, request.TenantId);

        if (!string.IsNullOrWhiteSpace(request.InitialContext))
        {
            workflow.UpdateContext(request.InitialContext);
        }

        // Create steps from the request if provided (Blueprint C.2: sequential preset)
        if (request.Steps is { Count: > 0 })
        {
            for (var i = 0; i < request.Steps.Count; i++)
            {
                workflow.AddStep(new WorkflowStep(Guid.NewGuid(), i, request.Steps[i]));
            }
        }

        // F37 决策 D2=B：队列模式 = 先落库（worker 在独立 scope 按 id 加载），再入队等待。
        if (QueuedRunSupport.IsQueueMode(_settings))
        {
            _workflowRepository.Add(workflow);
            await _unitOfWork.SaveChangesAsync(ct);

            var job = QueuedRunSupport.BuildJob(
                workflow.Id, request.TenantId, _workspaceProvider.GetWorkspaceId(), request.Preset,
                requestingUserId: request.RequestingUserId);
            return await QueuedRunSupport.EnqueueAndWaitAsync(
                _queue, _workflowRepository, _settings, job, _logger, ct);
        }

        // The orchestration primitive handles per-step persistence internally
        var result = await _primitive.RunAsync(workflow, request.Preset, ct);

        var auditLog = AuditLog.Record(
            tenantId: result.TenantId,
            action: AuditActionType.RunWorkflow,
            entity: "Workflow",
            entityId: result.Id,
            details: $"Started workflow '{result.Name}'");
        _auditLogRepository.Add(auditLog);

        return new WorkflowRunResult(result, QueueDispatchStatus.NotQueued, result.Id, result.CurrentState);
    }
}
