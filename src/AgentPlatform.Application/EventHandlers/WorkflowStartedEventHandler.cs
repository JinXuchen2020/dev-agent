using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using AgentPlatform.Domain.Aggregates.Workflows.Events;
using AgentPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Application.EventHandlers;

/// <summary>
/// Creates an <see cref="ExecutionLog"/> when a workflow starts, recording the initial execution state.
/// </summary>
public sealed class WorkflowStartedEventHandler
    : INotificationHandler<DomainEventNotification<WorkflowStarted>>
{
    private readonly IExecutionLogRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IExecutionProgressBroadcaster _broadcaster;
    private readonly ILogger<WorkflowStartedEventHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowStartedEventHandler"/> class.
    /// </summary>
    /// <param name="repository">The execution log repository for persisting log entries.</param>
    /// <param name="unitOfWork">The unit of work for persisting changes.</param>
    /// <param name="broadcaster">The progress broadcaster for SSE streaming.</param>
    /// <param name="logger">The logger used to capture workflow start events.</param>
    public WorkflowStartedEventHandler(
        IExecutionLogRepository repository,
        IUnitOfWork unitOfWork,
        IExecutionProgressBroadcaster broadcaster,
        ILogger<WorkflowStartedEventHandler> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    /// <summary>
    /// Handles the WorkflowStarted domain event by creating an execution log entry.
    /// </summary>
    /// <param name="notification">The domain event notification containing the workflow started event.</param>
    /// <param name="ct">A cancellation token to cancel the asynchronous operation.</param>
    public async Task Handle(DomainEventNotification<WorkflowStarted> notification, CancellationToken ct)
    {
        var evt = notification.DomainEvent;
        _logger.LogInformation(
            "Creating execution log for workflow {WorkflowId} ({Name})", evt.WorkflowId, evt.Name);

        // TotalSteps unknown at workflow start — will be updated as steps are encountered
        var log = new ExecutionLog(
            Guid.NewGuid(),
            evt.WorkflowId,
            evt.Name,
            evt.TenantId,
            totalSteps: 0);

        _repository.Add(log);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Execution log {LogId} created for workflow {WorkflowId}", log.Id, evt.WorkflowId);

        // Broadcast progress for SSE subscribers
        await _broadcaster.PublishAsync(evt.WorkflowId, new ExecutionProgressEvent(
            Type: "workflow_started",
            WorkflowId: evt.WorkflowId,
            ExecutionLogId: log.Id,
            StepName: null,
            StepOrder: null,
            Status: "running",
            Result: null,
            ErrorDetail: null,
            Timestamp: DateTime.UtcNow), ct);
    }
}
