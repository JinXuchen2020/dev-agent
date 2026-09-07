using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workflows.Commands.AddWorkflowNode;
using AgentPlatform.Application.Workflows.Commands.AddWorkflowEdge;
using AgentPlatform.Application.Workflows.Commands.RemoveWorkflowNode;
using AgentPlatform.Application.Workflows.Commands.RemoveWorkflowEdge;
using AgentPlatform.Application.Workflows.Queries.GetWorkflow;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Handlers;

/// <summary>
/// 覆盖 #2 增量节点/连线端点的 handler：租户隔离（404）、运行态锁（409）、图校验（422）、成功返回服务端 Id。
/// </summary>
public sealed class WorkflowNodeEdgeHandlersTests
{
    private static Workflow NewWorkflow(Guid? tenant = null) =>
        new(Guid.NewGuid(), "wf", tenant ?? Guid.NewGuid());

    private static IWorkflowRepository RepoReturning(Workflow? wf)
    {
        var repo = Substitute.For<IWorkflowRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(wf);
        return repo;
    }

    // ── AddWorkflowNode ──
    [Fact]
    public async Task AddWorkflowNode_ReturnsNull_WhenWorkflowMissing()
    {
        var repo = RepoReturning(null);
        var handler = new AddWorkflowNodeCommandHandler(repo);

        var result = await handler.Handle(
            new AddWorkflowNodeCommand(Guid.NewGuid(),
                new AddWorkflowNodeRequest(StepType.LLM, "N1", 0, 0, null, null), Guid.NewGuid()),
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task AddWorkflowNode_ReturnsNull_OnTenantMismatch()
    {
        var wf = NewWorkflow(Guid.NewGuid());
        var repo = RepoReturning(wf);
        var handler = new AddWorkflowNodeCommandHandler(repo);

        var result = await handler.Handle(
            new AddWorkflowNodeCommand(wf.Id,
                new AddWorkflowNodeRequest(StepType.LLM, "N1", 0, 0, null, null), Guid.NewGuid()),
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task AddWorkflowNode_ThrowsConflict_WhenRunning()
    {
        var wf = NewWorkflow(Guid.NewGuid());
        wf.SetState(WorkflowState.Running);
        var repo = RepoReturning(wf);
        var handler = new AddWorkflowNodeCommandHandler(repo);

        await Assert.ThrowsAsync<WorkflowConflictException>(() => handler.Handle(
            new AddWorkflowNodeCommand(wf.Id,
                new AddWorkflowNodeRequest(StepType.LLM, "N1", 0, 0, null, null), wf.TenantId),
            CancellationToken.None));
    }

    [Fact]
    public async Task AddWorkflowNode_ReturnsCreatedNode_WithServerId()
    {
        var wf = NewWorkflow(Guid.NewGuid());
        var repo = RepoReturning(wf);
        var handler = new AddWorkflowNodeCommandHandler(repo);

        var result = await handler.Handle(
            new AddWorkflowNodeCommand(wf.Id,
                new AddWorkflowNodeRequest(StepType.Agentic, "ReAct", 10, 20, "{\"goal\":\"x\"}", null), wf.TenantId),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result!.Id);
        Assert.Equal("ReAct", result.Name);
        Assert.Equal(StepType.Agentic, result.Type);
        Assert.Equal(10, result.PositionX);
        Assert.Equal("{\"goal\":\"x\"}", result.ConfigJson);
        repo.Received(1).Update(Arg.Any<Workflow>());
        Assert.Contains(wf.Nodes, n => n.Id == result.Id);
    }

    // ── RemoveWorkflowNode ──
    [Fact]
    public async Task RemoveWorkflowNode_ReturnsFalse_WhenMissingOrMismatch()
    {
        var repo = RepoReturning(null);
        var handler = new RemoveWorkflowNodeCommandHandler(repo);
        Assert.False(await handler.Handle(
            new RemoveWorkflowNodeCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task RemoveWorkflowNode_ReturnsFalse_WhenNodeAbsent()
    {
        var wf = NewWorkflow(Guid.NewGuid());
        var repo = RepoReturning(wf);
        var handler = new RemoveWorkflowNodeCommandHandler(repo);

        var removed = await handler.Handle(
            new RemoveWorkflowNodeCommand(wf.Id, Guid.NewGuid(), wf.TenantId), CancellationToken.None);

        Assert.False(removed);
        repo.DidNotReceive().Update(Arg.Any<Workflow>());
    }

    [Fact]
    public async Task RemoveWorkflowNode_ReturnsTrue_AndRemovesNode()
    {
        var wf = NewWorkflow(Guid.NewGuid());
        var node = wf.AddNode(StepType.LLM, "N1", 0, 0, null, null);
        var repo = RepoReturning(wf);
        var handler = new RemoveWorkflowNodeCommandHandler(repo);

        var removed = await handler.Handle(
            new RemoveWorkflowNodeCommand(wf.Id, node.Id, wf.TenantId), CancellationToken.None);

        Assert.True(removed);
        Assert.DoesNotContain(wf.Nodes, n => n.Id == node.Id);
        repo.Received(1).Update(Arg.Any<Workflow>());
    }

    // ── AddWorkflowEdge ──
    [Fact]
    public async Task AddWorkflowEdge_ReturnsCreatedEdge_WithServerId()
    {
        var wf = NewWorkflow(Guid.NewGuid());
        var a = wf.AddNode(StepType.Start, "S", 0, 0, null, null);
        var b = wf.AddNode(StepType.End, "E", 0, 0, null, null);
        var repo = RepoReturning(wf);
        var handler = new AddWorkflowEdgeCommandHandler(repo);

        var result = await handler.Handle(
            new AddWorkflowEdgeCommand(wf.Id,
                new AddWorkflowEdgeRequest(a.Id, b.Id, "ok"), wf.TenantId),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result!.Id);
        Assert.Equal(a.Id, result.SourceNodeId);
        Assert.Equal(b.Id, result.TargetNodeId);
        Assert.Equal("ok", result.Label);
    }

    [Fact]
    public async Task AddWorkflowEdge_ThrowsGraphException_WhenSourceMissing()
    {
        var wf = NewWorkflow(Guid.NewGuid());
        var repo = RepoReturning(wf);
        var handler = new AddWorkflowEdgeCommandHandler(repo);

        await Assert.ThrowsAsync<WorkflowGraphException>(() => handler.Handle(
            new AddWorkflowEdgeCommand(wf.Id,
                new AddWorkflowEdgeRequest(Guid.NewGuid(), Guid.NewGuid(), null), wf.TenantId),
            CancellationToken.None));
    }

    // ── RemoveWorkflowEdge ──
    [Fact]
    public async Task RemoveWorkflowEdge_ReturnsTrue_AndRemovesEdge()
    {
        var wf = NewWorkflow(Guid.NewGuid());
        var a = wf.AddNode(StepType.Start, "S", 0, 0, null, null);
        var b = wf.AddNode(StepType.End, "E", 0, 0, null, null);
        var edge = wf.AddEdge(a.Id, b.Id, null);
        var repo = RepoReturning(wf);
        var handler = new RemoveWorkflowEdgeCommandHandler(repo);

        var removed = await handler.Handle(
            new RemoveWorkflowEdgeCommand(wf.Id, edge.Id, wf.TenantId), CancellationToken.None);

        Assert.True(removed);
        Assert.DoesNotContain(wf.Edges, e => e.Id == edge.Id);
    }

    [Fact]
    public async Task RemoveWorkflowEdge_ReturnsFalse_WhenEdgeAbsent()
    {
        var wf = NewWorkflow(Guid.NewGuid());
        var repo = RepoReturning(wf);
        var handler = new RemoveWorkflowEdgeCommandHandler(repo);

        Assert.False(await handler.Handle(
            new RemoveWorkflowEdgeCommand(wf.Id, Guid.NewGuid(), wf.TenantId), CancellationToken.None));
    }

    /// <summary>
    /// 回归锁：增量图命令必须实现 <see cref="ICommand{T}"/>，否则 UnitOfWorkBehavior
    /// （约束 where TRequest : ICommand&lt;TResponse&gt;）不会 SaveChanges —— 曾因此出现
    /// 「端点返回 200 + 服务端 Id，但变更从不落库」的假成功（本地 curl 联调实证）。
    /// </summary>
    [Fact]
    public void IncrementalGraphCommands_ImplementICommand_SoUnitOfWorkCommits()
    {
        Assert.IsAssignableFrom<ICommand<WorkflowNodeResponse?>>(new AddWorkflowNodeCommand(
            Guid.Empty, new AddWorkflowNodeRequest(StepType.LLM, "n", 0, 0, null, null), Guid.Empty));
        Assert.IsAssignableFrom<ICommand<bool>>(
            new RemoveWorkflowNodeCommand(Guid.Empty, Guid.Empty, Guid.Empty));
        Assert.IsAssignableFrom<ICommand<WorkflowEdgeResponse?>>(new AddWorkflowEdgeCommand(
            Guid.Empty, new AddWorkflowEdgeRequest(Guid.Empty, Guid.Empty, null), Guid.Empty));
        Assert.IsAssignableFrom<ICommand<bool>>(
            new RemoveWorkflowEdgeCommand(Guid.Empty, Guid.Empty, Guid.Empty));
    }
}
