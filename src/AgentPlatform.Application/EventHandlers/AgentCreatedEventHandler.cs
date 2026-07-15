using AgentPlatform.Domain.Aggregates.Agents.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Application.EventHandlers;

internal sealed class AgentCreatedEventHandler : INotificationHandler<DomainEventNotification<AgentCreated>>
{
    private readonly ILogger<AgentCreatedEventHandler> _logger;

    public AgentCreatedEventHandler(ILogger<AgentCreatedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(DomainEventNotification<AgentCreated> notification, CancellationToken ct)
    {
        var evt = notification.DomainEvent;
        _logger.LogInformation(
            "Agent created: {AgentId}, Name: {Name}, Role: {Role}, Tenant: {TenantId}",
            evt.AgentId, evt.Name,
            evt.RoleCode, evt.TenantId);

        return Task.CompletedTask;
    }
}
