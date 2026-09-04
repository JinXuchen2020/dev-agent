using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.ExecutionLogs.Queries.GetExecutionLogDetail;
using AgentPlatform.Application.ExecutionLogs.Queries.GetExecutionLogSteps;
using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.ExecutionLogs;

/// <summary>
/// F40 越权面收口的 handler 级回归锁：既有 详情 / steps 两个查询必须经租户作用域仓储方法
/// 读取（GetByIdForTenantAsync / IsOwnedByTenantAsync），不得回退到无过滤的 GetByIdAsync /
/// 裸 QueryStepsAsync —— 后者一旦绕开归属判定即是跨租户 GUID 猜测漏洞复发。
/// </summary>
public sealed class ExecutionLogTenantQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static (T Substitute, ITenantProvider Provider) Harness<T>()
        where T : class
    {
        var repo = Substitute.For<T>();
        var provider = Substitute.For<ITenantProvider>();
        provider.GetTenantId().Returns(TenantId);
        return (repo, provider);
    }

    [Fact]
    public async Task Detail_Uses_TenantScoped_Read_And_Blocks_CrossTenant()
    {
        var (repo, provider) = Harness<IExecutionLogRepository>();

        // 归属匹配：走 GetByIdForTenantAsync（带当前租户），返回完整详情。
        var log = new ExecutionLog(Guid.NewGuid(), Guid.NewGuid(), "wf", TenantId, 1);
        log.AddEntry(new ExecutionLogEntry(
            Guid.NewGuid(), "Step 1", 0, WorkflowState.Completed,
            TimeSpan.FromMilliseconds(10), "ok", null));
        log.Complete();
        repo.GetByIdForTenantAsync(log.Id, TenantId, Arg.Any<CancellationToken>()).Returns(log);

        var detail = await new GetExecutionLogDetailQueryHandler(repo, provider)
            .Handle(new GetExecutionLogDetailQuery(log.Id), CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Single(detail!.Entries);
        await repo.Received(1).GetByIdForTenantAsync(log.Id, TenantId, Arg.Any<CancellationToken>());

        // 跨租户 → null（controller 映射 404），且绝不触碰无过滤读取。
        var foreign = await new GetExecutionLogDetailQueryHandler(repo, provider)
            .Handle(new GetExecutionLogDetailQuery(Guid.NewGuid()), CancellationToken.None);
        Assert.Null(foreign);
        await repo.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task Steps_Requires_Ownership_Before_Queried()
    {
        var (repo, provider) = Harness<IExecutionLogRepository>();
        var handler = new GetExecutionLogStepsQueryHandler(repo, provider);
        var logId = Guid.NewGuid();

        // 非本租户：IsOwnedByTenantAsync=false → null → 404，且不进入无租户的 QueryStepsAsync。
        repo.IsOwnedByTenantAsync(logId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        Assert.Null(await handler.Handle(new GetExecutionLogStepsQuery(logId), CancellationToken.None));
        await repo.DidNotReceiveWithAnyArgs().QueryStepsAsync(default, default, default, default, default);

        // 本租户：判定通过后按原分页查询。
        repo.IsOwnedByTenantAsync(logId, TenantId, Arg.Any<CancellationToken>()).Returns(true);
        repo.QueryStepsAsync(logId, null, 0, 50, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ExecutionLogEntry>)[], 0));
        var ok = await handler.Handle(new GetExecutionLogStepsQuery(logId), CancellationToken.None);
        Assert.NotNull(ok);
    }
}
