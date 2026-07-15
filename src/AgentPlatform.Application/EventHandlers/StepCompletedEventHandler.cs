using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using AgentPlatform.Domain.Aggregates.Workflows.Events;
using AgentPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Application.EventHandlers;

/// <summary>
/// Records a successfully completed step entry in the <see cref="ExecutionLog"/>.
/// </summary>
public sealed class StepCompletedEventHandler
    : INotificationHandler<DomainEventNotification<StepCompleted>>
{
    private readonly IExecutionLogRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<StepCompletedEventHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StepCompletedEventHandler"/> class.
    /// </summary>
    /// <param name="repository">The execution log repository for persisting log entries.</param>
    /// <param name="unitOfWork">The unit of work for persisting changes.</param>
    /// <param name="logger">The logger used to capture step completion events.</param>
    public StepCompletedEventHandler(
        IExecutionLogRepository repository,
        IUnitOfWork unitOfWork,
        ILogger<StepCompletedEventHandler> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// Handles the StepCompleted domain event by recording the step entry in the execution log.
    /// </summary>
    /// <param name="notification">The domain event notification containing the step completed event.</param>
    /// <param name="ct">A cancellation token to cancel the asynchronous operation.</param>
    public async Task Handle(DomainEventNotification<StepCompleted> notification, CancellationToken ct)
    {
        var evt = notification.DomainEvent;
        var logs = await _repository.GetByWorkflowIdAsync(evt.WorkflowId, ct);
        var log = logs.FirstOrDefault();

        if (log is null)
        {
            _logger.LogWarning(
                "No execution log found for workflow {WorkflowId} when step {StepName} completed",
                evt.WorkflowId, evt.StepName);
            return;
        }

        var entry = new ExecutionLogEntry(
            Guid.NewGuid(),
            evt.StepName,
            evt.StepOrder,
            Domain.Enums.WorkflowState.Completed,
            duration: evt.Duration,
            result: evt.Result,
            errorDetail: null);

        log.AddEntry(entry);
        _repository.Update(log);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Step {StepName} ({StepOrder}) completed for workflow {WorkflowId}",
            evt.StepName, evt.StepOrder, evt.WorkflowId);
    }
}
