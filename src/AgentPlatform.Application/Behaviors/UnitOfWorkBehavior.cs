using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Abstractions;
using MediatR;

namespace AgentPlatform.Application.Behaviors;

internal sealed class UnitOfWorkBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDomainEventBus _eventBus;

    public UnitOfWorkBehavior(IUnitOfWork unitOfWork, IDomainEventBus eventBus)
    {
        _unitOfWork = unitOfWork;
        _eventBus = eventBus;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next();

        var aggregates = _unitOfWork.GetTrackedAggregates();
        var events = aggregates.SelectMany(a => a.DomainEvents).ToList();

        // Commit the transaction FIRST so event handlers read committed data
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var domainEvent in events)
        {
            await _eventBus.PublishAsync(domainEvent, cancellationToken);
        }

        foreach (var aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }

        return response;
    }
}
