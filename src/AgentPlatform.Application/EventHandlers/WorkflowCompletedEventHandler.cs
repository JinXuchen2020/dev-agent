using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Diagnostics;
using AgentPlatform.Domain.Aggregates.Workflows.Events;
using AgentPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Application.EventHandlers;

/// <summary>
/// Marks the <see cref="Domain.Aggregates.ExecutionLogs.ExecutionLog"/> as completed when a workflow finishes successfully.
/// </summary>
public sealed class WorkflowCompletedEventHandler
    : INotificationHandler<DomainEventNotification<WorkflowCompleted>>
{
    private readonly IExecutionLogRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IExecutionProgressBroadcaster _broadcaster;
    private readonly ILogger<WorkflowCompletedEventHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowCompletedEventHandler"/> class.
    /// </summary>
    /// <param name="repository">The execution log repository for persisting log entries.</param>
    /// <param name="unitOfWork">The unit of work for persisting changes.</param>
    /// <param name="broadcaster">The progress broadcaster for SSE streaming.</param>
    /// <param name="logger">The logger used to capture workflow completion events.</param>
    public WorkflowCompletedEventHandler(
        IExecutionLogRepository repository,
        IUnitOfWork unitOfWork,
        IExecutionProgressBroadcaster broadcaster,
        ILogger<WorkflowCompletedEventHandler> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    /// <summary>
    /// Handles the WorkflowCompleted domain event by marking the execution log as completed.
    /// </summary>
    /// <param name="notification">The domain event notification containing the workflow completed event.</param>
    /// <param name="ct">A cancellation token to cancel the asynchronous operation.</param>
    public async Task Handle(DomainEventNotification<WorkflowCompleted> notification, CancellationToken ct)
    {
        var evt = notification.DomainEvent;
        var logs = await _repository.GetByWorkflowIdAsync(evt.WorkflowId, ct);
        var log = logs.FirstOrDefault();

        if (log is null)
        {
            _logger.LogWarning(
                "No execution log found for workflow {WorkflowId} on completion",
                evt.WorkflowId);
            return;
        }

        log.Complete();
        _repository.Update(log);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Execution log for workflow {WorkflowId} marked as completed", evt.WorkflowId);

        await _broadcaster.PublishAsync(evt.WorkflowId, new ExecutionProgressEvent(
            Type: "workflow_completed",
            WorkflowId: evt.WorkflowId,
            ExecutionLogId: log.Id,
            StepName: null,
            StepOrder: null,
            Status: "completed",
            Result: null,
            ErrorDetail: null,
            Timestamp: DateTime.UtcNow), ct);

        // Record workflow success metric
        WorkflowMetrics.WorkflowCompletedCounter.Add(1,
            new KeyValuePair<string, object?>("result", "success"),
            new KeyValuePair<string, object?>("workflow_id", evt.WorkflowId));
    }
}
