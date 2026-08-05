using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using Reqnroll;
using Xunit;

namespace AgentPlatform.SpecFlowTests.Steps;

/// <summary>
/// 驱动生产代码 <see cref="IOrchestrationPrimitive"/>（真实顺序 / 协商编排器），仅经
/// <see cref="ConfigurableStepExecutor"/> 隔离外部 LLM 步骤行为。断言真实重试 / 回滚语义，
/// 并验证工作流聚合状态已持久化到真实文件 SQLite。
/// </summary>
[Binding]
public sealed class WorkflowEngineSteps
{
    private Workflow _workflow = null!;
    private readonly ConfigurableStepExecutor _executor;

    public WorkflowEngineSteps()
    {
        _executor = IntegrationHost.Factory.Services.GetRequiredService<ConfigurableStepExecutor>();
    }

    [BeforeScenario]
    public void BeforeScenario() => _executor.Reset();

    [Given("a (\\d+)-step workflow is defined")]
    public void GivenWorkflowWithSteps(int stepCount)
    {
        var names = new List<string>();
        for (var i = 1; i <= stepCount; i++)
            names.Add($"Step{i}");

        _workflow = new Workflow(Guid.NewGuid(), "Engine Test Workflow", IntegrationConstants.Tenant1Id);
        _workflow.ReplaceSteps(names);
    }

    [Given("step (\\d+) is configured to fail with retryable error")]
    public void GivenStepRetryableFailure(int stepNumber)
        => _executor.ConfigureFailure($"Step{stepNumber}", StepOutcome.FailedRetry, $"Step{stepNumber} transient failure");

    [Given("step (\\d+) is configured to fail permanently")]
    public void GivenStepPermanentFailure(int stepNumber)
        => _executor.ConfigureFailure($"Step{stepNumber}", StepOutcome.FailedRollback, $"Step{stepNumber} fatal failure");

    [When("the workflow is executed sequentially")]
    public Task WhenExecutedSequentially() => ExecuteAsync(OrchestrationPreset.Sequential);

    [When("the workflow is executed with the negotiation preset")]
    public Task WhenExecutedNegotiation() => ExecuteAsync(OrchestrationPreset.Negotiation);

    private async Task ExecuteAsync(OrchestrationPreset preset)
    {
        // IOrchestrationPrimitive 为 Scoped，需在 scope 内解析；RunAsync 自行管理分步持久化。
        using var scope = IntegrationHost.Factory.Services.CreateScope();
        var primitive = scope.ServiceProvider.GetRequiredService<IOrchestrationPrimitive>();
        await primitive.RunAsync(_workflow, preset, CancellationToken.None);
    }

    [Then("step (\\d+) should be in state (.*)")]
    public void ThenStepInState(int stepNumber, string stateName)
        => Assert.Equal(ParseState(stateName), _workflow.Steps[stepNumber - 1].State);

    [Then("the workflow should be in state (.*)")]
    public void ThenWorkflowInState(string stateName)
        => Assert.Equal(ParseState(stateName), _workflow.CurrentState);

    [Then("step (\\d+) should have been attempted (\\d+) times")]
    public void ThenStepAttempted(int stepNumber, int times)
        => Assert.Equal(times, _executor.GetCallCount($"Step{stepNumber}"));

    [Then("the workflow state should be persisted to the database")]
    public async Task ThenPersistedToDatabase()
    {
        using var scope = IntegrationHost.Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IWorkflowRepository>();
        var reloaded = await repo.GetByIdAsync(_workflow.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal(_workflow.CurrentState, reloaded!.CurrentState);
    }

    private static WorkflowState ParseState(string name) => name switch
    {
        "Pending" => WorkflowState.Pending,
        "Running" => WorkflowState.Running,
        "Paused" => WorkflowState.Paused,
        "Completed" => WorkflowState.Completed,
        "Failed" => WorkflowState.Failed,
        "RolledBack" => WorkflowState.RolledBack,
        _ => throw new ArgumentException($"Unknown state: {name}")
    };
}
