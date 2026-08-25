using System;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using Xunit;

namespace AgentPlatform.Application.Tests.Workflows;

/// <summary>
/// F30/F31：RunningExecution 租约生命周期领域行为。
/// 重点锁定 F31 回归修复——从终态（Completed）与暂停态（Paused）重新获取租约必须成功，
/// 否则「重跑工作流 / 暂停后恢复」在真实 DB 路径上全部失败。
/// </summary>
public sealed class RunningExecutionTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    [Fact]
    public void Create_InitializesRunningState_WithFreshLease()
    {
        var exec = RunningExecution.Create(Guid.NewGuid(), Guid.NewGuid(), "instance-a", Ttl);

        Assert.Equal(WorkflowState.Running, exec.WorkflowState);
        Assert.Equal("instance-a", exec.InstanceId);
        Assert.False(exec.IsLeaseExpired);
    }

    [Fact]
    public void TryAcquireLease_FromTerminalCompleted_Succeeds_ForRerun()
    {
        var exec = RunningExecution.Create(Guid.NewGuid(), Guid.NewGuid(), "instance-a", Ttl);
        exec.Complete();
        Assert.Equal(string.Empty, exec.InstanceId); // 终态清空持有者

        // 重跑场景（F31 回归修复）：必须允许从 Completed 重新获取租约
        Assert.True(exec.TryAcquireLease("instance-b", Ttl));
        Assert.Equal("instance-b", exec.InstanceId);
    }

    [Fact]
    public void TryAcquireLease_FromPaused_Succeeds_ForResume()
    {
        var exec = RunningExecution.Create(Guid.NewGuid(), Guid.NewGuid(), "instance-a", Ttl);
        exec.Pause();

        // 恢复场景（F31 回归修复）：Paused 后 Resume 必须能重新获取租约
        Assert.True(exec.TryAcquireLease("instance-a", Ttl));
    }

    [Fact]
    public void TryAcquireLease_HeldByAnotherLiveInstance_IsRefused()
    {
        var exec = RunningExecution.Create(Guid.NewGuid(), Guid.NewGuid(), "instance-a", Ttl);

        // 另一实例、租约未过期 → 拒绝（多实例幂等的核心）
        Assert.False(exec.TryAcquireLease("instance-b", Ttl));
        Assert.Equal("instance-a", exec.InstanceId); // 持有者不变
    }

    [Fact]
    public void TryAcquireLease_AfterExpiry_Succeeds_Takeover()
    {
        var wfId = Guid.NewGuid();
        // 以重水化工厂构造「租约已过期」的持久化形态（崩溃恢复调度的真实输入）
        var exec = RunningExecution.Rehydrate(
            wfId, Guid.NewGuid(), WorkflowState.Running,
            "instance-a",
            heartbeatAt: DateTime.UtcNow.AddMinutes(-10),
            leaseExpiresAt: DateTime.UtcNow.AddMinutes(-5));

        Assert.True(exec.IsLeaseExpired);
        Assert.True(exec.TryAcquireLease("scheduler-1", Ttl)); // 崩溃恢复接管
        Assert.Equal("scheduler-1", exec.InstanceId);
    }

    [Fact]
    public void ReleaseLease_WrongInstance_IsRefused()
    {
        var exec = RunningExecution.Create(Guid.NewGuid(), Guid.NewGuid(), "instance-a", Ttl);

        Assert.False(exec.ReleaseLease("instance-b"));
        Assert.Equal("instance-a", exec.InstanceId);
    }

    [Fact]
    public void UpdateHeartbeat_AdvancesVersion_AndSnapshot()
    {
        var exec = RunningExecution.Create(Guid.NewGuid(), Guid.NewGuid(), "instance-a", Ttl);

        exec.UpdateHeartbeat(7, "{\"k\":\"v\"}");

        Assert.Equal(7, exec.CheckpointVersion);
        Assert.Equal("{\"k\":\"v\"}", exec.BlackboardSnapshot);
    }

    [Fact]
    public void Create_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentException>(() => RunningExecution.Create(Guid.NewGuid(), Guid.NewGuid(), "", Ttl));
        Assert.Throws<ArgumentOutOfRangeException>(() => RunningExecution.Create(Guid.NewGuid(), Guid.NewGuid(), "a", TimeSpan.Zero));
    }
}