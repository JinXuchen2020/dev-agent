using System.Collections.Generic;
using System.Reflection;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Analytics.Queries.GetWorkflowUsage;
using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Handlers;

/// <summary>Verifies the per-workflow usage query aggregates metrics grouped by workflow.</summary>
public sealed class GetWorkflowUsageQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime BaseDate = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid WfAlpha = Guid.NewGuid();
    private static readonly Guid WfBeta = Guid.NewGuid();

    private readonly IExecutionLogRepository _executionLogRepository = Substitute.For<IExecutionLogRepository>();
    private readonly ITenantProvider _tenantProvider = Substitute.For<ITenantProvider>();
    private readonly GetWorkflowUsageQueryHandler _handler;

    public GetWorkflowUsageQueryHandlerTests()
    {
        _tenantProvider.GetTenantId().Returns(TenantId);
        _handler = new GetWorkflowUsageQueryHandler(_executionLogRepository, _tenantProvider);
    }

    [Fact]
    public async Task Handle_Should_Aggregate_PerWorkflow_Executions_And_SuccessRate()
    {
        var logs = new List<ExecutionLog>
        {
            MakeLog(WfAlpha, "alpha", WorkflowState.Completed, 0, 0, 0),
            MakeLog(WfAlpha, "alpha", WorkflowState.Completed, 0, 0, 0),
            MakeLog(WfAlpha, "alpha", WorkflowState.Failed, 0, 0, 0),
            MakeLog(WfBeta, "beta", WorkflowState.Completed, 0, 0, 0),
        };
        _executionLogRepository
            .GetByTenantAsync(TenantId, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(logs);

        var result = await _handler.Handle(new GetWorkflowUsageQuery(BaseDate, BaseDate.AddDays(1)), CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        var alpha = Assert.Single(result.Items, d => d.WorkflowId == WfAlpha);
        Assert.Equal("alpha", alpha.WorkflowName);
        Assert.Equal(3, alpha.Executions);
        Assert.Equal(2, alpha.Completed);
        Assert.Equal(1, alpha.Failed);
        Assert.Equal(66.67, alpha.SuccessRate, 2);
        var beta = Assert.Single(result.Items, d => d.WorkflowId == WfBeta);
        Assert.Equal(1, beta.Executions);
        Assert.Equal(100, beta.SuccessRate, 2);
    }

    [Fact]
    public async Task Handle_Should_Sum_Tokens_And_Average_Latency_PerWorkflow()
    {
        var logs = new List<ExecutionLog>
        {
            MakeLog(WfAlpha, "alpha", WorkflowState.Completed, 1000, 30, 70),
            MakeLog(WfAlpha, "alpha", WorkflowState.Completed, 2000, 10, 40),
        };
        _executionLogRepository
            .GetByTenantAsync(TenantId, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(logs);

        var result = await _handler.Handle(new GetWorkflowUsageQuery(BaseDate, BaseDate), CancellationToken.None);

        var alpha = Assert.Single(result.Items);
        Assert.Equal(1500, alpha.AvgLatencyMs, 2);
        Assert.Equal(150, alpha.TotalTokens); // (30+70) + (10+40)
    }

    [Fact]
    public async Task Handle_Should_Query_With_Current_TenantId_And_Range()
    {
        _executionLogRepository
            .GetByTenantAsync(Arg.Any<Guid>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ExecutionLog>());

        await _handler.Handle(new GetWorkflowUsageQuery(BaseDate, BaseDate.AddDays(1)), CancellationToken.None);

        await _executionLogRepository.Received(1)
            .GetByTenantAsync(TenantId, BaseDate, BaseDate.AddDays(2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_Return_Empty_When_NoLogs()
    {
        _executionLogRepository
            .GetByTenantAsync(TenantId, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ExecutionLog>());

        var result = await _handler.Handle(new GetWorkflowUsageQuery(BaseDate, BaseDate), CancellationToken.None);

        Assert.Empty(result.Items);
    }

    private static ExecutionLog MakeLog(
        Guid workflowId, string workflowName, WorkflowState status, double entryMs, int tokensIn, int tokensOut)
    {
        var log = new ExecutionLog(Guid.NewGuid(), workflowId, workflowName, TenantId, 1);
        switch (status)
        {
            case WorkflowState.Completed:
                log.Complete();
                break;
            case WorkflowState.Failed:
                log.Fail();
                break;
        }

        SetPrivate(log, "StartedAt", BaseDate);

        if (entryMs > 0 || tokensIn > 0 || tokensOut > 0)
        {
            log.AddEntry(new ExecutionLogEntry(
                Guid.NewGuid(), "step", 0, WorkflowState.Completed,
                TimeSpan.FromMilliseconds(entryMs), null, null, tokensIn, tokensOut));
        }

        return log;
    }

    private static void SetPrivate<T>(T obj, string name, object value)
    {
        var property = typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property '{name}' not found on {typeof(T).Name}.");
        property.SetValue(obj, value);
    }
}
