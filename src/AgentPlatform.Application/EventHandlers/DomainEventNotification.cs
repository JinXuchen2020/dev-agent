using AgentPlatform.Domain.Abstractions;
using MediatR;

namespace AgentPlatform.Application.EventHandlers;

/// <summary>
/// Wraps a domain event as a MediatR notification so it can be dispatched through the in-process notification pipeline.
/// </summary>
/// <typeparam name="T">The type of the domain event being wrapped.</typeparam>
/// <param name="DomainEvent">The domain event instance to deliver to handlers.</param>
public record DomainEventNotification<T>(T DomainEvent) : INotification where T : IDomainEvent;
