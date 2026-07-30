using System.Reflection;
using AgentPlatform.Application.Analytics.Queries.GetDashboardSummary;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Domain.ValueObjects;
using AgentPlatform.Application.Abstractions;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Handlers;

public class GetDashboardSummaryQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime BaseDate = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly IExecutionLogRepository _executionLogRepository = Substitute.For<IExecutionLogRepository>();
    private readonly IConversationRepository _conversationRepository = Substitute.For<IConversationRepository>();
    private readonly IAgentRepository _agentRepository = Substitute.For<IAgentRepository>();
    private readonly IWorkflowRepository _workflowRepository = Substitute.For<IWorkflowRepository>();
    private readonly ITenantProvider _tenantProvider = Substitute.For<ITenantProvider>();
    private readonly GetDashboardSummaryQueryHandler _handler;

    public GetDashboardSummaryQueryHandlerTests()
    {
        _tenantProvider.GetTenantId().Returns(TenantId);
        _handler = new GetDashboardSummaryQueryHandler(
            _executionLogRepository, _conversationRepository, _agentRepository, _workflowRepository, _tenantProvider);
    }

    [Fact]
    public async Task Handle_Should_Compute_SuccessRate_And_TopWorkflows()
    {
        var logs = new List<ExecutionLog>
        {
            MakeLog("wf-alpha", WorkflowState.Completed, BaseDate, 0),
            MakeLog("wf-alpha", WorkflowState.Completed, BaseDate, 0),
            MakeLog("wf-alpha", WorkflowState.Failed, BaseDate, 0),
        };
        _executionLogRepository
            .GetByTenantAsync(TenantId, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(logs);

        var result = await _handler.Handle(new GetDashboardSummaryQuery(BaseDate, BaseDate.AddDays(2)), CancellationToken.None);

        Assert.Equal(3, result.Kpis.TotalExecutions);
        Assert.Equal(66.67, result.Kpis.SuccessRate, 2);
        var top = Assert.Single(result.TopWorkflows);
        Assert.Equal("wf-alpha", top.WorkflowName);
        Assert.Equal(3, top.Count);
    }

    [Fact]
    public async Task Handle_Should_Sum_Tokens_And_Count_Conversations_ByDay()
    {
        var conversations = new List<Conversation>
        {
            MakeConv(BaseDate, 100),
            MakeConv(BaseDate, 50),
            MakeConv(BaseDate.AddDays(1), 25),
        };
        _conversationRepository
            .GetByTenantAsync(TenantId, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(conversations);

        var result = await _handler.Handle(new GetDashboardSummaryQuery(BaseDate, BaseDate.AddDays(2)), CancellationToken.None);

        Assert.Equal(175, result.Kpis.TotalTokens);
        var day0 = result.ConversationsByDay.Single(b => b.Date == BaseDate);
        Assert.Equal(2, day0.Count);
        var day1 = result.ConversationsByDay.Single(b => b.Date == BaseDate.AddDays(1));
        Assert.Equal(1, day1.Count);
        var day0Tokens = result.TokenByDay.Single(b => b.Date == BaseDate);
        Assert.Equal(150, day0Tokens.TotalTokens);
    }

    [Fact]
    public async Task Handle_Should_Average_Latency_Across_Executions()
    {
        var logs = new List<ExecutionLog>
        {
            MakeLog("wf", WorkflowState.Completed, BaseDate, 1000),
            MakeLog("wf", WorkflowState.Completed, BaseDate, 2000),
        };
        _executionLogRepository
            .GetByTenantAsync(TenantId, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(logs);

        var result = await _handler.Handle(new GetDashboardSummaryQuery(BaseDate, BaseDate), CancellationToken.None);

        Assert.Equal(1500, result.Kpis.AvgLatencyMs, 2);
        var day = Assert.Single(result.LatencyByDay);
        Assert.Equal(1500, day.AvgMs, 2);
    }

    [Fact]
    public async Task Handle_Should_Produce_Day_Buckets_Even_When_Empty()
    {
        _executionLogRepository
            .GetByTenantAsync(TenantId, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ExecutionLog>());
        _conversationRepository
            .GetByTenantAsync(TenantId, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new List<Conversation>());

        var result = await _handler.Handle(new GetDashboardSummaryQuery(BaseDate, BaseDate.AddDays(2)), CancellationToken.None);

        Assert.Equal(3, result.ExecutionsByDay.Count);
        Assert.Equal(3, result.TokenByDay.Count);
        Assert.Equal(3, result.ConversationsByDay.Count);
        Assert.Equal(3, result.LatencyByDay.Count);
        Assert.Empty(result.TopWorkflows);
        Assert.Equal(0, result.Kpis.TotalExecutions);
        Assert.Equal(0, result.Kpis.SuccessRate);
        Assert.Equal(0, result.Kpis.TotalTokens);
    }

    [Fact]
    public async Task Handle_Should_Query_Repositories_With_Current_TenantId()
    {
        _executionLogRepository
            .GetByTenantAsync(Arg.Any<Guid>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ExecutionLog>());
        _conversationRepository
            .GetByTenantAsync(Arg.Any<Guid>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new List<Conversation>());

        await _handler.Handle(new GetDashboardSummaryQuery(BaseDate, BaseDate.AddDays(1)), CancellationToken.None);

        await _executionLogRepository.Received(1)
            .GetByTenantAsync(TenantId, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        await _agentRepository.Received(1).GetByTenantAsync(TenantId, Arg.Any<CancellationToken>());
        await _workflowRepository.Received(1).GetByTenantAsync(TenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_Report_Agent_And_Workflow_Counts()
    {
        var agents = new List<Agent>
        {
            new(Guid.NewGuid(), "a1", new AgentType("developer", "Developer", "dev"), new ModelEndpoint("openai", "gpt-4o", ""), "p", TenantId),
            new(Guid.NewGuid(), "a2", new AgentType("tester", "Tester", "test"), new ModelEndpoint("openai", "gpt-4o", ""), "p", TenantId),
        };
        var workflows = new List<Workflow>
        {
            new(Guid.NewGuid(), "w1", TenantId),
            new(Guid.NewGuid(), "w2", TenantId),
            new(Guid.NewGuid(), "w3", TenantId),
        };
        _agentRepository.GetByTenantAsync(TenantId, Arg.Any<CancellationToken>()).Returns(agents);
        _workflowRepository.GetByTenantAsync(TenantId, Arg.Any<CancellationToken>()).Returns(workflows);

        var result = await _handler.Handle(new GetDashboardSummaryQuery(BaseDate, BaseDate), CancellationToken.None);

        Assert.Equal(2, result.Kpis.ActiveAgents);
        Assert.Equal(3, result.Kpis.ActiveWorkflows);
    }

    private static ExecutionLog MakeLog(string workflowName, WorkflowState status, DateTime startedAt, double entryMs)
    {
        var log = new ExecutionLog(Guid.NewGuid(), Guid.NewGuid(), workflowName, TenantId, 1);
        switch (status)
        {
            case WorkflowState.Completed:
                log.Complete();
                break;
            case WorkflowState.Failed:
                log.Fail();
                break;
            case WorkflowState.RolledBack:
                log.Rollback();
                break;
        }

        SetPrivate(log, "StartedAt", startedAt);

        if (entryMs > 0)
        {
            log.AddEntry(new ExecutionLogEntry(
                Guid.NewGuid(), "step", 0, WorkflowState.Completed, TimeSpan.FromMilliseconds(entryMs), null, null));
        }

        return log;
    }

    private static Conversation MakeConv(DateTime createdAt, int tokens)
    {
        var conversation = new Conversation(Guid.NewGuid(), TenantId);
        SetPrivate(conversation, "CreatedAt", createdAt);
        SetPrivate(conversation, "TotalTokenUsage", new TokenUsage(tokens, 0));
        return conversation;
    }

    private static void SetPrivate<T>(T obj, string name, object value)
    {
        var property = typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property '{name}' not found on {typeof(T).Name}.");
        property.SetValue(obj, value);
    }
}
