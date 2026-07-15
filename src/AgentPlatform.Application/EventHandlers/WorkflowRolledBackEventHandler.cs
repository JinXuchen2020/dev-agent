using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows.Events;
using AgentPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Application.EventHandlers;

/// <summary>
/// Marks the <see cref="Domain.Aggregates.ExecutionLogs.ExecutionLog"/> as rolled back when a workflow is rolled back.
/// </summary>
public sealed class WorkflowRolledBackEventHandler
    : INotificationHandler<DomainEventNotification<WorkflowRolledBack>>
{
    private readonly IExecutionLogRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<WorkflowRolledBackEventHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowRolledBackEventHandler"/> class.
    /// </summary>
    /// <param name="repository">The execution log repository for persisting log entries.</param>
    /// <param name="unitOfWork">The unit of work for persisting changes.</param>
    /// <param name="logger">The logger used to capture workflow rollback events.</param>
    public WorkflowRolledBackEventHandler(
        IExecutionLogRepository repository,
        IUnitOfWork unitOfWork,
        ILogger<WorkflowRolledBackEventHandler> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// Handles the WorkflowRolledBack domain event by marking the execution log as rolled back.
    /// </summary>
    /// <param name="notification">The domain event notification containing the workflow rolled back event.</param>
    /// <param name="ct">A cancellation token to cancel the asynchronous operation.</param>
    public async Task Handle(DomainEventNotification<WorkflowRolledBack> notification, CancellationToken ct)
    {
        var evt = notification.DomainEvent;
        var logs = await _repository.GetByWorkflowIdAsync(evt.WorkflowId, ct);
        var log = logs.FirstOrDefault();

        if (log is null)
        {
            _logger.LogWarning(
                "No execution log found for workflow {WorkflowId} on rollback",
                evt.WorkflowId);
            return;
        }

        log.Rollback();
        _repository.Update(log);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Execution log for workflow {WorkflowId} marked as rolled back (failed step: {FailedStep})",
            evt.WorkflowId, evt.FailedStepName);
    }
}
