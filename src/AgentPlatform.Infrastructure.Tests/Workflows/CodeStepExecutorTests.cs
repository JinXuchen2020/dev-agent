#nullable disable
using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Infrastructure.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Workflows;

public class CodeStepExecutorTests
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
        step.Name.Returns("代码节点");
        step.ConfigJson.Returns(configJson);
        return step;
    }

    private static CodeStepExecutor Executor(ICodeSandbox sandbox)
    {
        var settings = Options.Create(new SandboxSettings { TimeoutSeconds = 30, MaxOutputBytes = 65536 });
        return new CodeStepExecutor(Substitute.For<ILogger<CodeStepExecutor>>(), sandbox, settings);
    }

    [Fact]
    public async Task ExecuteAsync_RunsCode_Via_Sandbox_And_Maps_Success()
    {
        var sandbox = Substitute.For<ICodeSandbox>();
        sandbox.RunCodeAsync("print(1)", "python", 30, Arg.Any<CancellationToken>())
            .Returns(new SandboxResult(true, "1", string.Empty, 0, 10));

        var executor = Executor(sandbox);
        var result = await executor.ExecuteAsync(Node("{\"code\":\"print(1)\",\"language\":\"python\"}"), Ctx(), default);

        Assert.Equal(StepOutcome.Success, result.Outcome);
        Assert.Equal("1", result.Output);
        await sandbox.Received(1).RunCodeAsync("print(1)", "python", 30, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_SandboxFailure_Maps_To_RetryableFailure()
    {
        var sandbox = Substitute.For<ICodeSandbox>();
        sandbox.RunCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxResult(false, string.Empty, "boom", 1, 5));

        var executor = Executor(sandbox);
        var result = await executor.ExecuteAsync(Node("{\"code\":\"x\",\"language\":\"python\"}"), Ctx(), default);

        Assert.Equal(StepOutcome.FailedRetry, result.Outcome);
        Assert.Contains("boom", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_MissingCode_Returns_FatalFailure()
    {
        var sandbox = Substitute.For<ICodeSandbox>();
        var executor = Executor(sandbox);

        var result = await executor.ExecuteAsync(Node("{\"language\":\"python\"}"), Ctx(), default);

        Assert.Equal(StepOutcome.FailedRollback, result.Outcome);
        Assert.Contains("code", result.ErrorMessage);
    }
}
