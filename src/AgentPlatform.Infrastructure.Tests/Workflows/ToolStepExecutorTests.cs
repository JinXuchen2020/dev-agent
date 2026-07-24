#nullable disable
using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Tools;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Infrastructure.Workflows;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Workflows;

public class ToolStepExecutorTests
{
    private static WorkflowContext Ctx()
    {
        return new WorkflowContext
        {
            WorkflowId = Guid.NewGuid(),
            CurrentStepOrder = 0,
            Artifacts = new Dictionary<string, StepArtifact>(),
            Blackboard = Blackboard.Empty,
            Retrieval = RetrievalContext.Empty,
            Summary = StepHistory.Empty,
            TenantId = Guid.NewGuid(),
        };
    }

    private static IWorkflowExecutable Node(string configJson)
    {
        var step = Substitute.For<IWorkflowExecutable>();
        step.Name.Returns("工具节点");
        step.ConfigJson.Returns(configJson);
        return step;
    }

    private static ToolCallingDispatcher Dispatcher(IToolRegistry registry, IToolExecutor executor)
    {
        return new ToolCallingDispatcher(registry, new[] { executor }, Substitute.For<ILogger<ToolCallingDispatcher>>());
    }

    [Fact]
    public async Task ExecuteAsync_Dispatches_To_Executor_And_Maps_Success()
    {
        var tool = new ToolDefinition(Guid.NewGuid(), "web_search", "d", "{}", "h", Guid.NewGuid(), ToolSource.NativeTool, "http://x");
        var registry = Substitute.For<IToolRegistry>();
        registry.GetByNameAsync("web_search", Arg.Any<CancellationToken>()).Returns(tool);

        var executor = Substitute.For<IToolExecutor>();
        executor.Source.Returns(ToolSource.NativeTool);
        executor.ExecuteAsync(Arg.Any<ToolDefinition>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ToolExecutionResult(true, "ok-output"));

        var stepExecutor = new ToolStepExecutor(Substitute.For<ILogger<ToolStepExecutor>>(), Dispatcher(registry, executor));

        var result = await stepExecutor.ExecuteAsync(
            Node("{\"toolName\":\"web_search\",\"parameters\":\"{\\\"q\\\":1}\"}"), Ctx(), default);

        Assert.Equal(StepOutcome.Success, result.Outcome);
        Assert.Equal("ok-output", result.Output);
        await executor.Received(1).ExecuteAsync(Arg.Any<ToolDefinition>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ToolFailure_Maps_To_RetryableFailure()
    {
        var tool = new ToolDefinition(Guid.NewGuid(), "web_search", "d", "{}", "h", Guid.NewGuid(), ToolSource.NativeTool, "http://x");
        var registry = Substitute.For<IToolRegistry>();
        registry.GetByNameAsync("web_search", Arg.Any<CancellationToken>()).Returns(tool);

        var executor = Substitute.For<IToolExecutor>();
        executor.Source.Returns(ToolSource.NativeTool);
        executor.ExecuteAsync(Arg.Any<ToolDefinition>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ToolExecutionResult(false, string.Empty, null, "tool-error"));

        var stepExecutor = new ToolStepExecutor(Substitute.For<ILogger<ToolStepExecutor>>(), Dispatcher(registry, executor));

        var result = await stepExecutor.ExecuteAsync(
            Node("{\"toolName\":\"web_search\",\"parameters\":\"{}\"}"), Ctx(), default);

        Assert.Equal(StepOutcome.FailedRetry, result.Outcome);
        Assert.Contains("tool-error", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_MissingToolName_Returns_FatalFailure()
    {
        var registry = Substitute.For<IToolRegistry>();
        var executor = Substitute.For<IToolExecutor>();
        executor.Source.Returns(ToolSource.NativeTool);

        var stepExecutor = new ToolStepExecutor(Substitute.For<ILogger<ToolStepExecutor>>(), Dispatcher(registry, executor));

        var result = await stepExecutor.ExecuteAsync(Node("{}"), Ctx(), default);

        Assert.Equal(StepOutcome.FailedRollback, result.Outcome);
        Assert.Contains("toolName", result.ErrorMessage);
    }
}
