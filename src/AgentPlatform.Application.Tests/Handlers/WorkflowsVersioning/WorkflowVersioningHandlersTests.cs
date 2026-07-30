using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workflows.Commands.UpdateWorkflow;
using AgentPlatform.Application.Workflows.Versioning;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Handlers.WorkflowsVersioning;

/// <summary>Unit tests for workflow versioning + import/export command/query handlers (NSubstitute mocks).</summary>
public sealed class WorkflowVersioningHandlersTests
{
    private static Workflow BuildDag(Guid tenantId)
    {
        var start = Guid.NewGuid();
        var llm = Guid.NewGuid();
        var end = Guid.NewGuid();
        var wf = new Workflow(Guid.NewGuid(), "Test WF", tenantId);
        wf.ReplaceGraph(
            new List<(Guid, StepType, string, double, double, string?, Guid?)>
            {
                (start, StepType.Start, "Start", 0, 0, "{}", null),
                (llm, StepType.LLM, "Step1", 0, 120, "{}", null),
                (end, StepType.End, "End", 0, 240, "{}", null),
            },
            new List<(Guid, Guid, Guid, string?)>
            {
                (Guid.NewGuid(), start, llm, null),
                (Guid.NewGuid(), llm, end, null),
            });
        return wf;
    }

    [Fact]
    public async Task CreateWorkflowVersion_CreatesNextVersion_AndAudits()
    {
        var tenantId = Guid.NewGuid();
        var wf = BuildDag(tenantId);
        var workflowRepo = Substitute.For<IWorkflowRepository>();
        workflowRepo.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);
        var versionRepo = Substitute.For<IWorkflowVersionRepository>();
        versionRepo.GetLatestVersionNumberAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(2);
        var auditRepo = Substitute.For<IAuditLogRepository>();

        var handler = new CreateWorkflowVersionCommandHandler(workflowRepo, versionRepo, auditRepo);
        var result = await handler.Handle(new CreateWorkflowVersionCommand(wf.Id, tenantId, "note"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(3, result!.VersionNumber);
        Assert.Equal("Test WF", result.Name);
        Assert.Equal(3, result.Nodes.Count);
        versionRepo.Received().Add(Arg.Any<WorkflowVersion>());
        auditRepo.Received().Add(Arg.Is<AuditLog>(a => a.Action == AgentPlatform.Domain.Aggregates.AuditLogs.AuditActionType.CreateWorkflowVersion));
    }

    [Fact]
    public async Task CreateWorkflowVersion_ReturnsNull_ForCrossTenant()
    {
        var wf = BuildDag(Guid.NewGuid());
        var workflowRepo = Substitute.For<IWorkflowRepository>();
        workflowRepo.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);
        var handler = new CreateWorkflowVersionCommandHandler(
            workflowRepo, Substitute.For<IWorkflowVersionRepository>(), Substitute.For<IAuditLogRepository>());

        var result = await handler.Handle(new CreateWorkflowVersionCommand(wf.Id, Guid.NewGuid(), null), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task RestoreWorkflowVersion_RebuildsGraph_FromSnapshot()
    {
        var tenantId = Guid.NewGuid();
        var wf = BuildDag(tenantId);
        var snapshot = WorkflowGraphSnapshot.FromWorkflow(wf);
        var version = WorkflowVersion.Create(Guid.NewGuid(), wf.Id, tenantId, 1, wf.Name, snapshot.ToJson(), null, "v1");

        // Mutate the live workflow so restore has something to undo (still a valid graph).
        wf.Rename("Mutated");
        var mutStart = Guid.NewGuid();
        var mutEnd = Guid.NewGuid();
        wf.ReplaceGraph(
            new List<(Guid, StepType, string, double, double, string?, Guid?)>
            {
                (mutStart, StepType.Start, "Start", 0, 0, "{}", null),
                (mutEnd, StepType.End, "End", 0, 120, "{}", null),
            },
            new List<(Guid, Guid, Guid, string?)>
            {
                (Guid.NewGuid(), mutStart, mutEnd, null),
            });

        var workflowRepo = Substitute.For<IWorkflowRepository>();
        workflowRepo.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);
        var versionRepo = Substitute.For<IWorkflowVersionRepository>();
        versionRepo.GetByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
        var auditRepo = Substitute.For<IAuditLogRepository>();

        var handler = new RestoreWorkflowVersionCommandHandler(workflowRepo, versionRepo, auditRepo);
        var result = await handler.Handle(new RestoreWorkflowVersionCommand(wf.Id, version.Id, tenantId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Test WF", wf.Name); // restored name
        Assert.Equal(3, wf.Nodes.Count);  // restored graph
        auditRepo.Received().Add(Arg.Is<AuditLog>(a => a.Action == AgentPlatform.Domain.Aggregates.AuditLogs.AuditActionType.RestoreWorkflowVersion));
    }

    [Fact]
    public async Task RestoreWorkflowVersion_Throws_WhenRunning()
    {
        var tenantId = Guid.NewGuid();
        var wf = BuildDag(tenantId);
        wf.SetState(WorkflowState.Running);
        var version = WorkflowVersion.Create(
            Guid.NewGuid(), wf.Id, tenantId, 1, wf.Name, WorkflowGraphSnapshot.FromWorkflow(wf).ToJson(), null, null);

        var workflowRepo = Substitute.For<IWorkflowRepository>();
        workflowRepo.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);
        var versionRepo = Substitute.For<IWorkflowVersionRepository>();
        versionRepo.GetByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);

        var handler = new RestoreWorkflowVersionCommandHandler(workflowRepo, versionRepo, Substitute.For<IAuditLogRepository>());
        await Assert.ThrowsAsync<WorkflowConflictException>(() =>
            handler.Handle(new RestoreWorkflowVersionCommand(wf.Id, version.Id, tenantId), CancellationToken.None));
    }

    [Fact]
    public async Task ImportWorkflow_CreatesNewWorkflow_AndAudits()
    {
        var tenantId = Guid.NewGuid();
        var start = Guid.NewGuid();
        var llm = Guid.NewGuid();
        var end = Guid.NewGuid();
        var nodes = new List<WorkflowNodeRequest>
        {
            new(start, StepType.Start, "Start", new WorkflowNodePosition(0, 0), "{}", null),
            new(llm, StepType.LLM, "Step1", new WorkflowNodePosition(0, 120), "{}", null),
            new(end, StepType.End, "End", new WorkflowNodePosition(0, 240), "{}", null),
        };
        var edges = new List<WorkflowEdgeRequest>
        {
            new(Guid.NewGuid(), start, llm, null),
            new(Guid.NewGuid(), llm, end, null),
        };
        var workflowRepo = Substitute.For<IWorkflowRepository>();
        var auditRepo = Substitute.For<IAuditLogRepository>();
        var handler = new ImportWorkflowCommandHandler(workflowRepo, auditRepo);

        var result = await handler.Handle(new ImportWorkflowCommand("Imported", "{}", nodes, edges, tenantId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Imported", result!.Name);
        Assert.Equal(3, result.Nodes.Count);
        workflowRepo.Received().Add(Arg.Any<Workflow>());
        auditRepo.Received().Add(Arg.Is<AuditLog>(a => a.Action == AgentPlatform.Domain.Aggregates.AuditLogs.AuditActionType.ImportWorkflow));
    }

    [Fact]
    public async Task ImportWorkflow_Throws_OnInvalidGraph()
    {
        var tenantId = Guid.NewGuid();
        var nodes = new List<WorkflowNodeRequest>
        {
            new(Guid.NewGuid(), StepType.LLM, "Only", new WorkflowNodePosition(0, 0), "{}", null),
        };
        var handler = new ImportWorkflowCommandHandler(Substitute.For<IWorkflowRepository>(), Substitute.For<IAuditLogRepository>());
        await Assert.ThrowsAsync<WorkflowGraphException>(() =>
            handler.Handle(new ImportWorkflowCommand("Bad", "{}", nodes, null, tenantId), CancellationToken.None));
    }

    [Fact]
    public async Task ListWorkflowVersions_ReturnsPagedDescending()
    {
        var wfId = Guid.NewGuid();
        var versions = new List<WorkflowVersion>
        {
            WorkflowVersion.Create(Guid.NewGuid(), wfId, Guid.NewGuid(), 2, "n2", "{}", null, null),
            WorkflowVersion.Create(Guid.NewGuid(), wfId, Guid.NewGuid(), 1, "n1", "{}", null, null),
        };
        var versionRepo = Substitute.For<IWorkflowVersionRepository>();
        versionRepo.ListByWorkflowAsync(wfId, 0, 20, Arg.Any<CancellationToken>()).Returns(versions);
        versionRepo.CountByWorkflowAsync(wfId, Arg.Any<CancellationToken>()).Returns(2);

        var handler = new ListWorkflowVersionsQueryHandler(versionRepo);
        var result = await handler.Handle(new ListWorkflowVersionsQuery(wfId), CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.Items[0].VersionNumber); // descending order
    }

    [Fact]
    public async Task GetWorkflowVersion_MapsSnapshot()
    {
        var wfId = Guid.NewGuid();
        var snapshot = WorkflowGraphSnapshot.FromWorkflow(BuildDag(Guid.NewGuid()));
        var version = WorkflowVersion.Create(Guid.NewGuid(), wfId, Guid.NewGuid(), 1, "n", snapshot.ToJson(), null, null);
        var versionRepo = Substitute.For<IWorkflowVersionRepository>();
        versionRepo.GetByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);

        var handler = new GetWorkflowVersionQueryHandler(versionRepo);
        var result = await handler.Handle(new GetWorkflowVersionQuery(wfId, version.Id), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Nodes.Count);
    }

    [Fact]
    public async Task DeleteWorkflowVersion_Removes_AndAudits()
    {
        var tenantId = Guid.NewGuid();
        var wfId = Guid.NewGuid();
        var version = WorkflowVersion.Create(Guid.NewGuid(), wfId, tenantId, 1, "n", "{}", null, null);
        var versionRepo = Substitute.For<IWorkflowVersionRepository>();
        versionRepo.GetByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
        var auditRepo = Substitute.For<IAuditLogRepository>();

        var handler = new DeleteWorkflowVersionCommandHandler(versionRepo, auditRepo);
        await handler.Handle(new DeleteWorkflowVersionCommand(wfId, version.Id, tenantId), CancellationToken.None);

        versionRepo.Received().Remove(Arg.Any<WorkflowVersion>());
        auditRepo.Received().Add(Arg.Is<AuditLog>(a => a.Action == AgentPlatform.Domain.Aggregates.AuditLogs.AuditActionType.DeleteWorkflowVersion));
    }
}
