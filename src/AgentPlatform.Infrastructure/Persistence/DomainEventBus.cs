using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.EventHandlers;
using AgentPlatform.Domain.Abstractions;
using MediatR;

namespace AgentPlatform.Infrastructure.Persistence;

/// <summary>
/// 实现 <see cref="IDomainEventBus"/>，将领域事件包装为 <see cref="DomainEventNotification{T}"/>
/// 并通过 MediatR 通知管道进行分发。
/// </summary>
public sealed class DomainEventBus : IDomainEventBus
{
    private readonly IPublisher _publisher;

    /// <summary>
    /// 初始化 <see cref="DomainEventBus"/> 的新实例。
    /// </summary>
    /// <param name="publisher">用于分发领域事件通知的 MediatR 发布器。</param>
    public DomainEventBus(IPublisher publisher)
    {
        _publisher = publisher;
    }

    /// <summary>
    /// 将指定领域事件发布给所有已注册的 MediatR 通知处理程序。
    /// </summary>
    /// <typeparam name="T">领域事件的类型。</typeparam>
    /// <param name="domainEvent">要发布的领域事件实例。</param>
    /// <param name="ct">用于取消异步操作的取消令牌。</param>
    /// <returns>表示异步发布操作的任务。</returns>
    public async Task PublishAsync<T>(T domainEvent, CancellationToken ct = default) where T : IDomainEvent
    {
        var notification = new DomainEventNotification<T>(domainEvent);
        await _publisher.Publish(notification, ct);
    }
}
