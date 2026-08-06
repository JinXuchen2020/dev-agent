using System.Collections.Generic;
using AgentPlatform.Application.Workflows.Versioning;
using AgentPlatform.Application.Workflows.Versioning.DiffWorkflow;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Workflows.Versioning;

/// <summary>Verifies the workflow-definition diff query resolves snapshots and computes deltas.</summary>
public sealed class DiffWorkflowQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenant = Guid.NewGuid();
    private static readonly Guid StartId = Guid.NewGuid();
    private static readonly Guid EndId = Guid.NewGuid();

    private readonly IWorkflowRepository _workflowRepository = Substitute.For<IWorkflowRepository>();
    private readonly IWorkflowVersionRepository _versionRepository = Substitute.For<IWorkflowVersionRepository>();
    private readonly DiffWorkflowQueryHandler _handler;

    public DiffWorkflowQueryHandlerTests()
    {
        _handler = new DiffWorkflowQueryHandler(_workflowRepository, _versionRepository);
    }

    private static Workflow BuildWorkflow(Guid tenantId, params (Guid Id, StepType Type, string Name, string? Config)[] nodes)
    {
        var wf = new Workflow(Guid.NewGuid(), "WF", tenantId);
        var nodeArgs = new List<(Guid, StepType, string, double, double, string?, Guid?)>();
        // Ensure exactly one Start node (ValidateGraph requires it).
        if (!nodes.Any(n => n.Type == StepType.Start))
            nodeArgs.Add((StartId, StepType.Start, "Start", 0, 0, "{}", null));
        foreach (var n in nodes)
            nodeArgs.Add((n.Id, n.Type, n.Name, 0, 0, n.Config, null));
        // A real workflow always has an End node; ValidateGraph enforces this.
        nodeArgs.Add((EndId, StepType.End, "End", 0, 0, "{}", null));

        // Wire a linear Start -> ... -> End chain so each node is reachable.
        var edgeArgs = new List<(Guid, Guid, Guid, string?)>();
        for (var i = 0; i < nodeArgs.Count - 1; i++)
            edgeArgs.Add((Guid.NewGuid(), nodeArgs[i].Item1, nodeArgs[i + 1].Item1, null));

        wf.ReplaceGraph(nodeArgs, edgeArgs);
        return wf;
    }

    private static WorkflowVersion MakeVersion(Guid workflowId, int number, Workflow source)
    {
        var snapshot = WorkflowGraphSnapshot.FromWorkflow(source);
        return WorkflowVersion.Create(
            Guid.NewGuid(), workflowId, TenantId, number, $"v{number}", snapshot.ToJson(), null, null);
    }

    [Fact]
    public async Task Handle_VersionPair_Should_Report_Added_Node()
    {
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();
        var wf = BuildWorkflow(TenantId, (aId, StepType.Start, "A", "{}"), (bId, StepType.LLM, "B", "{}"));
        var v1 = MakeVersion(wf.Id, 1, BuildWorkflow(TenantId, (aId, StepType.Start, "A", "{}")));
        var v2 = MakeVersion(wf.Id, 2, wf);

        _workflowRepository.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);
        _versionRepository.GetByIdAsync(v1.Id, Arg.Any<CancellationToken>()).Returns(v1);
        _versionRepository.GetByIdAsync(v2.Id, Arg.Any<CancellationToken>()).Returns(v2);

        var result = await _handler.Handle(
            new DiffWorkflowQuery(wf.Id, v1.Id, v2.Id, null, TenantId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("v1", result!.FromLabel);
        Assert.Equal("v2", result.ToLabel);
        Assert.Single(result.AddedNodes);
        Assert.Equal("B", result.AddedNodes[0].Name);
        Assert.Empty(result.RemovedNodes);
        Assert.Empty(result.ChangedNodes);
        Assert.False(result.ContextChanged);
    }

    [Fact]
    public async Task Handle_Should_Report_Changed_Node_Config()
    {
        var aId = Guid.NewGuid();
        var wf = BuildWorkflow(TenantId, (aId, StepType.LLM, "A", "{\"x\":1}"));
        var v1 = MakeVersion(wf.Id, 1, BuildWorkflow(TenantId, (aId, StepType.LLM, "A", "{}")));
        var v2 = MakeVersion(wf.Id, 2, wf);

        _workflowRepository.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);
        _versionRepository.GetByIdAsync(v1.Id, Arg.Any<CancellationToken>()).Returns(v1);
        _versionRepository.GetByIdAsync(v2.Id, Arg.Any<CancellationToken>()).Returns(v2);

        var result = await _handler.Handle(
            new DiffWorkflowQuery(wf.Id, v1.Id, v2.Id, null, TenantId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result!.ChangedNodes);
        Assert.Equal("A", result.ChangedNodes[0].Before.Name);
        Assert.Equal("{}", result.ChangedNodes[0].Before.ConfigJson);
        Assert.Equal("{\"x\":1}", result.ChangedNodes[0].After.ConfigJson);
    }

    [Fact]
    public async Task Handle_CrossTenant_Workflow_Should_ReturnNull()
    {
        var wf = BuildWorkflow(OtherTenant, (Guid.NewGuid(), StepType.Start, "A", "{}"));
        _workflowRepository.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);

        var result = await _handler.Handle(
            new DiffWorkflowQuery(wf.Id, null, null, null, TenantId), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_OtherWorkflow_CrossTenant_Should_ReturnNull()
    {
        var wf = BuildWorkflow(TenantId, (Guid.NewGuid(), StepType.Start, "A", "{}"));
        var other = BuildWorkflow(OtherTenant, (Guid.NewGuid(), StepType.Start, "A", "{}"));
        _workflowRepository.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);
        _workflowRepository.GetByIdAsync(other.Id, Arg.Any<CancellationToken>()).Returns(other);

        var result = await _handler.Handle(
            new DiffWorkflowQuery(wf.Id, null, null, other.Id, TenantId), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_Default_NoVersions_Should_Treat_Current_As_Added()
    {
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();
        var wf = BuildWorkflow(TenantId, (aId, StepType.Start, "A", "{}"), (bId, StepType.LLM, "B", "{}"));
        _workflowRepository.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);
        _versionRepository.ListByWorkflowAsync(wf.Id, 0, 1, Arg.Any<CancellationToken>())
            .Returns(new List<WorkflowVersion>());

        var result = await _handler.Handle(
            new DiffWorkflowQuery(wf.Id, null, null, null, TenantId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("(no version)", result!.FromLabel);
        Assert.Equal("current", result.ToLabel);
        Assert.Equal(3, result.AddedNodes.Count);
        Assert.Empty(result.RemovedNodes);
    }

    [Fact]
    public async Task Handle_Default_WithLatestVersion_Should_Diff_Against_It()
    {
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();
        var wf = BuildWorkflow(TenantId, (aId, StepType.Start, "A", "{}"), (bId, StepType.LLM, "B", "{}"));
        var v1 = MakeVersion(wf.Id, 1, BuildWorkflow(TenantId, (aId, StepType.Start, "A", "{}")));
        _workflowRepository.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);
        _versionRepository.ListByWorkflowAsync(wf.Id, 0, 1, Arg.Any<CancellationToken>())
            .Returns(new List<WorkflowVersion> { v1 });

        var result = await _handler.Handle(
            new DiffWorkflowQuery(wf.Id, null, null, null, TenantId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("v1", result!.FromLabel);
        Assert.Single(result.AddedNodes);
        Assert.Equal("B", result.AddedNodes[0].Name);
    }
}
