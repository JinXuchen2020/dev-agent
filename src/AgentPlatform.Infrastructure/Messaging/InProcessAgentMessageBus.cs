using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.AgentMessages;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Messaging;

/// <summary>
/// In-process <see cref="IAgentMessageBus"/> built on bounded per-receiver channels (F32).
/// Publish is write-through: durable log append (dedup by MessageId) first, then inbox enqueue.
/// Consumption idempotency is delegated to <see cref="IAgentMessageLogRepository.TryMarkConsumedAsync"/>.
/// Scoped: each workflow run gets an isolated bus — no cross-tenant/cross-run message bleed.
/// </summary>
internal sealed class InProcessAgentMessageBus : IAgentMessageBus
{
    private const int InboxCapacity = 256;

    private readonly IAgentMessageLogRepository _logRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<InProcessAgentMessageBus> _logger;

    private readonly ConcurrentDictionary<Guid, Channel<AgentMessage>> _inboxes = new();

    public InProcessAgentMessageBus(
        IAgentMessageLogRepository logRepository,
        IUnitOfWork unitOfWork,
        ILogger<InProcessAgentMessageBus> logger)
    {
        _logRepository = logRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PublishAsync(AgentMessage message, Guid tenantId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // 1) Write-through durable log with publish-side dedup: a redelivered/replayed MessageId
        //    must not double-append nor double-enqueue.
        if (await _logRepository.ExistsAsync(message.MessageId, ct))
        {
            _logger.LogDebug("Message {MessageId} already logged; skipping duplicate publish", message.MessageId);
            return;
        }

        _logRepository.Add(new AgentMessageLog(
            messageId: message.MessageId,
            workflowId: message.WorkflowId,
            correlationId: message.CorrelationId,
            senderId: message.SenderId,
            receiverId: message.ReceiverId,
            messageType: message.Type,
            payload: message.Payload,
            round: message.Round,
            tenantId: tenantId));
        await _unitOfWork.SaveChangesAsync(ct);

        // 2) In-memory fan-out to the receiver inbox (broadcast fans out to all registered).
        if (message.ReceiverId == Guid.Empty)
        {
            foreach (var inbox in _inboxes.Values)
                await EnqueueAsync(inbox, message, ct);
        }
        else
        {
            var inbox = _inboxes.GetOrAdd(message.ReceiverId, _ => CreateInbox());
            await EnqueueAsync(inbox, message, ct);
        }

        _logger.LogInformation(
            "Agent message {MessageId} ({Type}) published: {Sender} → {Receiver} (round {Round}, wf {WorkflowId})",
            message.MessageId, message.Type, message.SenderId, message.ReceiverId, message.Round, message.WorkflowId);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentMessage> ReadAllAsync(
        Guid receiverId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var inbox = _inboxes.GetOrAdd(receiverId, _ => CreateInbox());

        // Drain the current backlog only — the negotiation loop pulls per round.
        while (inbox.Reader.TryRead(out var message))
        {
            yield return message;
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<int> RepublishUnconsumedAsync(Guid workflowId, Guid tenantId, CancellationToken ct = default)
    {
        var pending = await _logRepository.GetUnconsumedByWorkflowAsync(workflowId, ct);
        foreach (var log in pending)
        {
            var message = new AgentMessage(
                log.Id, log.WorkflowId, log.CorrelationId,
                log.SenderId, log.ReceiverId,
                log.MessageType, log.Payload, log.Round);

            var inbox = _inboxes.GetOrAdd(message.ReceiverId == Guid.Empty ? Guid.Empty : message.ReceiverId, _ => CreateInbox());
            await EnqueueAsync(inbox, message, ct);
        }

        if (pending.Count > 0)
            _logger.LogInformation("Republished {Count} unconsumed messages for workflow {WorkflowId}", pending.Count, workflowId);
        return pending.Count;
    }

    private static Channel<AgentMessage> CreateInbox() =>
        Channel.CreateBounded<AgentMessage>(new BoundedChannelOptions(InboxCapacity)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait // backpressure instead of silent drop
        });

    private async Task EnqueueAsync(Channel<AgentMessage> inbox, AgentMessage message, CancellationToken ct)
    {
        // Backpressure wait keeps at-least-once semantics; the stall guard in the orchestrator
        // bounds how long a full inbox can stall the round.
        while (!inbox.Writer.TryWrite(message))
        {
            _logger.LogWarning("Inbox {Receiver} full; waiting for capacity (backpressure)", message.ReceiverId);
            await Task.Delay(50, ct);
        }
    }
}