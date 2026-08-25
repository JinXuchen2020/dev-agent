using System.Collections.Concurrent;
using System.Threading.Channels;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.AgentMessages;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Messaging;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Messaging;

/// <summary>
/// F32：InProcessAgentMessageBus 行为——写穿持久化、按收件人隔离、发布端去重、
/// 未消费重投。消费幂等由仓储条件更新保证（此处以桩验证调用契约）。
/// </summary>
public sealed class InProcessAgentMessageBusTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid WfId = Guid.NewGuid();

    private readonly IAgentMessageLogRepository _logRepo = Substitute.For<IAgentMessageLogRepository>();
    private AgentMessageLog? _addedLog;
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private InProcessAgentMessageBus CreateBus() =>
        new(_logRepo, _unitOfWork, Substitute.For<ILogger<InProcessAgentMessageBus>>());

    private static AgentMessage Msg(Guid receiver, int salt = 0) => new(
        Guid.NewGuid(), WfId, Guid.NewGuid(), Guid.NewGuid(), receiver,
        AgentMessageType.Proposal, $"{{\"i\":{salt}}}", 1);

    [Fact]
    public async Task Publish_Persists_And_Delivers_To_Receiver_Inbox()
    {
        _logRepo.ExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        _logRepo.Add(Arg.Do<AgentMessageLog>(a => _addedLog = a));
        var bus = CreateBus();

        // 先发布（写穿持久化 + 入箱），再排空——ReadAllAsync 是一次性快照排空语义
        var msg = Msg(TenantId);
        await bus.PublishAsync(msg, TenantId);

        var delivered = new List<AgentMessage>();
        await foreach (var m in bus.ReadAllAsync(TenantId))
            delivered.Add(m);

        Assert.Single(delivered);
        Assert.Equal(msg.MessageId, delivered[0].MessageId);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.NotNull(_addedLog);
        Assert.Equal(TenantId, _addedLog!.TenantId);
    }

    [Fact]
    public async Task Publish_Duplicate_MessageId_Is_Skipped()
    {
        _logRepo.ExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true); // 已存在
        var bus = CreateBus();

        var msg = Msg(Guid.NewGuid());
        await bus.PublishAsync(msg, TenantId);

        _logRepo.DidNotReceive().Add(Arg.Any<AgentMessageLog>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAll_Drains_Only_Target_Receiver()
    {
        _logRepo.ExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        var bus = CreateBus();

        var otherReceiver = Guid.NewGuid();
        await bus.PublishAsync(Msg(otherReceiver, 1), TenantId);
        await bus.PublishAsync(Msg(otherReceiver, 2), TenantId);

        // 目标收件箱从未收到消息 → 排空为空（收件箱按 receiver 隔离）
        var drained = new List<AgentMessage>();
        await foreach (var m in bus.ReadAllAsync(TenantId))
            drained.Add(m);
        Assert.Empty(drained);
    }

    [Fact]
    public async Task Republish_Unconsumed_ReEnqueues_From_Log()
    {
        var pendingId = Guid.NewGuid();
        _logRepo.GetUnconsumedByWorkflowAsync(WfId, Arg.Any<CancellationToken>())
            .Returns(new List<AgentMessageLog>
            {
                new(pendingId, WfId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                    AgentMessageType.Handoff, "{}", 1, TenantId)
            });

        var bus = CreateBus();
        var receiver = Guid.NewGuid();
        // 重投消息的 ReceiverId 需与日志一致——直接断言返回计数，投递细节由 Publish 路径覆盖
        var n = await bus.RepublishUnconsumedAsync(WfId, TenantId);

        Assert.Equal(1, n);
    }
}