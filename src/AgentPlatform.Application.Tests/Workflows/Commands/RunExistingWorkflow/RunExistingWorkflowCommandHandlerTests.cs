using System.Linq;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workflows.Commands.RunExistingWorkflow;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Workflows.Commands.RunExistingWorkflow;

/// <summary>
/// 覆盖「保存并运行」重跑场景：工作流处于终态（Completed/Failed/RolledBack）或暂停态时，
/// RunExistingWorkflowCommandHandler 必须先把聚合重置为 Pending，否则 RunAsync 会拒绝执行。
/// </summary>
public sealed class RunExistingWorkflowCommandHandlerTests
{
    private static Workflow BuildGraphWithState(WorkflowState state)
    {
        var wf = new Workflow(Guid.NewGuid(), "wf", Guid.NewGuid());
        var start = Guid.NewGuid();
        var a = Guid.NewGuid();
        var end = Guid.NewGuid();
        var nodes = new List<(Guid, StepType, string, double, double, string?, Guid?)>
        {
            (start, StepType.Start, "Start", 0, 0, "{}", null),
            (a, StepType.LLM, "Step A", 0, 100, "{}", null),
            (end, StepType.End, "End", 0, 200, "{}", null),
        };
        var edges = new List<(Guid, Guid, Guid, string?)>
        {
            (Guid.NewGuid(), start, a, null),
            (Guid.NewGuid(), a, end, null),
        };
        wf.ReplaceGraph(nodes, edges);
        // Simulate a prior completed run so nodes carry results.
        wf.Nodes.ElementAt(1).SetResult("prior output");
        wf.SetState(state);
        return wf;
    }

    private static RunExistingWorkflowCommandHandler BuildHandler(
        Workflow wf,
        out IOrchestrationPrimitive primitive,
        out IWorkflowRepository repo)
    {
        repo = Substitute.For<IWorkflowRepository>();
        repo.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);

        primitive = Substitute.For<IOrchestrationPrimitive>();
        primitive.RunAsync(wf, Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>())
            .Returns(wf); // mock returns same instance; does not mutate state

        var audit = Substitute.For<IAuditLogRepository>();

        return new RunExistingWorkflowCommandHandler(repo, primitive, audit);
    }

    [Theory]
    [InlineData(WorkflowState.Completed)]
    [InlineData(WorkflowState.Failed)]
    [InlineData(WorkflowState.RolledBack)]
    [InlineData(WorkflowState.Paused)]
    public async Task ReRun_FromTerminalOrPausedState_ResetsToPendingAndRuns(WorkflowState prior)
    {
        var wf = BuildGraphWithState(prior);
        var handler = BuildHandler(wf, out var primitive, out _);

        var result = await handler.Handle(
            new RunExistingWorkflowCommand(wf.Id, TenantId: wf.TenantId), CancellationToken.None);

        Assert.NotNull(result);
        // Reset() cleared the prior result and returned the aggregate to Pending.
        Assert.Equal(WorkflowState.Pending, wf.CurrentState);
        Assert.Equal(WorkflowState.Pending, wf.Nodes.ElementAt(1).State);
        Assert.Null(wf.Nodes.ElementAt(1).Result);
        await primitive.Received(1).RunAsync(wf, Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReRun_FromPending_DoesNotThrowAndRuns()
    {
        var wf = BuildGraphWithState(WorkflowState.Pending);
        var handler = BuildHandler(wf, out var primitive, out _);

        var result = await handler.Handle(
            new RunExistingWorkflowCommand(wf.Id, TenantId: wf.TenantId), CancellationToken.None);

        Assert.NotNull(result);
        await primitive.Received(1).RunAsync(wf, Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReRun_WhileRunning_ThrowsConflict()
    {
        var wf = BuildGraphWithState(WorkflowState.Running);
        var handler = BuildHandler(wf, out var primitive, out _);

        await Assert.ThrowsAsync<WorkflowConflictException>(
            () => handler.Handle(
                new RunExistingWorkflowCommand(wf.Id, TenantId: wf.TenantId), CancellationToken.None));

        await primitive.DidNotReceive().RunAsync(Arg.Any<Workflow>(), Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>());
    }
}
