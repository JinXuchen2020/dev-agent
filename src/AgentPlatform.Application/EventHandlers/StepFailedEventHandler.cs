using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using AgentPlatform.Domain.Aggregates.Workflows.Events;
using AgentPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Application.EventHandlers;

/// <summary>
/// Records a failed step entry in the <see cref="ExecutionLog"/> with error details.
/// </summary>
public sealed class StepFailedEventHandler
    : INotificationHandler<DomainEventNotification<StepFailed>>
{
    private readonly IExecutionLogRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<StepFailedEventHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StepFailedEventHandler"/> class.
    /// </summary>
    /// <param name="repository">The execution log repository for persisting log entries.</param>
    /// <param name="unitOfWork">The unit of work for persisting changes.</param>
    /// <param name="logger">The logger used to capture step failure events.</param>
    public StepFailedEventHandler(
        IExecutionLogRepository repository,
        IUnitOfWork unitOfWork,
        ILogger<StepFailedEventHandler> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// Handles the StepFailed domain event by recording the failed step entry in the execution log.
    /// </summary>
    /// <param name="notification">The domain event notification containing the step failed event.</param>
    /// <param name="ct">A cancellation token to cancel the asynchronous operation.</param>
    public async Task Handle(DomainEventNotification<StepFailed> notification, CancellationToken ct)
    {
        var evt = notification.DomainEvent;
        var logs = await _repository.GetByWorkflowIdAsync(evt.WorkflowId, ct);
        var log = logs.FirstOrDefault();

        if (log is null)
        {
            _logger.LogWarning(
                "No execution log found for workflow {WorkflowId} when step {StepName} failed",
                evt.WorkflowId, evt.StepName);
            return;
        }

        var entry = new ExecutionLogEntry(
            Guid.NewGuid(),
            evt.StepName,
            evt.StepOrder,
            Domain.Enums.WorkflowState.Failed,
            duration: evt.Duration,
            result: null,
            errorDetail: evt.ErrorDetail);

        log.AddEntry(entry);
        _repository.Update(log);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Step {StepName} ({StepOrder}) failed for workflow {WorkflowId}: {Error}",
            evt.StepName, evt.StepOrder, evt.WorkflowId, evt.ErrorDetail);
    }
}
