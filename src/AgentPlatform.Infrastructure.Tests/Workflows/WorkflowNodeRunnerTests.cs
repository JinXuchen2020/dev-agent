using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Workflows;

/// <summary>
/// 覆盖设计文档 §9 后端验收：<see cref="WorkflowNodeRunner"/> 的 ResolveExecutor 路由
/// （按 StepType 命中；未知类型落 * 兜底；glob 按名匹配）。
/// </summary>
public sealed class WorkflowNodeRunnerTests
{
    private readonly IWorkflowRepository _repository = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();
    private readonly ILogger<WorkflowNodeRunner> _logger = Substitute.For<ILogger<WorkflowNodeRunner>>();

    private WorkflowNodeRunner CreateRunner() => new(_repository, _unitOfWork, _serviceProvider, _logger);

    private static IStepExecutor Executor(StepType? handlesType = null, string stepType = "*")
    {
        var ex = Substitute.For<IStepExecutor>();
        ex.HandlesType.Returns(handlesType);
        ex.StepType.Returns(stepType);
        ex.ExecuteAsync(default!, default!, default)
            .ReturnsForAnyArgs(StepExecutionResult.Success($"ran-{stepType}"));
        return ex;
    }

    private static void ProvideExecutors(IServiceProvider sp, params IStepExecutor[] executors) =>
        sp.GetService(typeof(IEnumerable<IStepExecutor>)).Returns(executors);

    private static Workflow BuildValidChain(params (string Name, StepType Type)[] nodes)
    {
        var wf = new Workflow(Guid.NewGuid(), "wf", Guid.NewGuid());
        var tempIds = nodes.Select(_ => Guid.NewGuid()).ToArray();
        var nodeTuples = nodes
            .Select((n, i) => (tempIds[i], n.Type, n.Name, (double)(i * 100), 0d, (string?)null, (Guid?)null))
            .ToList();
        var edgeTuples = new List<(Guid, Guid, Guid, string?)>();
        for (var i = 0; i < tempIds.Length - 1; i++)
            edgeTuples.Add((Guid.NewGuid(), tempIds[i], tempIds[i + 1], null));
        wf.ReplaceGraph(nodeTuples, edgeTuples);
        return wf;
    }

    [Fact]
    public async Task RunNodeAsync_RoutesByStepType_ToMatchingExecutor()
    {
        var llm = Executor(StepType.LLM);
        var agent = Executor(StepType.Agent);
        ProvideExecutors(_serviceProvider, llm, agent);

        var wf = BuildValidChain(("Start", StepType.Start), ("A", StepType.LLM), ("End", StepType.End));
        var llmNode = wf.Nodes.Single(n => n.Type == StepType.LLM);

        var result = await CreateRunner().RunNodeAsync(wf, llmNode.Id, default);

        Assert.Equal(WorkflowState.Completed, result.State);
        await llm.Received(1).ExecuteAsync(Arg.Any<IWorkflowExecutable>(), Arg.Any<WorkflowContext>(), Arg.Any<CancellationToken>());
        await agent.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, default);
    }

    [Fact]
    public async Task RunNodeAsync_UnknownType_FallsBackToWildcard()
    {
        // 没有任何 executor 的 HandlesType 命中 Critic，仅存在 "*" 兜底。
        var wildcard = Executor(null, "*");
        ProvideExecutors(_serviceProvider, wildcard);

        var wf = BuildValidChain(("Start", StepType.Start), ("C", StepType.Critic), ("End", StepType.End));
        var criticNode = wf.Nodes.Single(n => n.Type == StepType.Critic);

        var result = await CreateRunner().RunNodeAsync(wf, criticNode.Id, default);

        Assert.Equal(WorkflowState.Completed, result.State);
        await wildcard.Received(1).ExecuteAsync(Arg.Any<IWorkflowExecutable>(), Arg.Any<WorkflowContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunNodeAsync_GlobPattern_MatchesByName()
    {
        var criticGlob = Executor(null, "*critic*");
        var wildcard = Executor(null, "*");
        ProvideExecutors(_serviceProvider, criticGlob, wildcard);

        var wf = BuildValidChain(("Start", StepType.Start), ("CriticStep", StepType.Critic), ("End", StepType.End));
        var node = wf.Nodes.Single(n => n.Type == StepType.Critic);

        var result = await CreateRunner().RunNodeAsync(wf, node.Id, default);

        // 断言实际命中的 executor 输出，以区分 glob 命中 vs "*" 兜底。
        Assert.Equal("ran-*critic*", result.Result);
        await criticGlob.Received(1).ExecuteAsync(Arg.Any<IWorkflowExecutable>(), Arg.Any<WorkflowContext>(), Arg.Any<CancellationToken>());
        await wildcard.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, default);
    }

    [Fact]
    public async Task RunNodeAsync_StartNode_SkipsExecution()
    {
        ProvideExecutors(_serviceProvider, Executor(StepType.LLM));

        var wf = BuildValidChain(("Start", StepType.Start), ("A", StepType.LLM), ("End", StepType.End));
        var start = wf.Nodes.Single(n => n.Type == StepType.Start);

        var result = await CreateRunner().RunNodeAsync(wf, start.Id, default);

        // Start 节点直接返回，不执行任何 executor，状态保持 Pending。
        Assert.Equal(WorkflowState.Pending, result.State);
    }
}
