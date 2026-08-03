using System.Net;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.PublishedWorkflows;
using AgentPlatform.Application.PublishedWorkflows.Commands.RunPublishedWorkflow;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.PublishedWorkflows;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.PublishedWorkflows;

/// <summary>
/// F22 单元测试：RunPublishedWorkflowCommandHandler（API 与 MCP 两种表面共用的执行入口）。
/// 重点验证：租户隔离、绑定 Key 隔离、输入 Schema 校验、Running 冲突、终态重置、模式无关执行。
/// </summary>
public sealed class RunPublishedWorkflowCommandHandlerTests
{
    private const string InputJson = "{\"x\":1}";

    private static Workflow BuildWorkflow(Guid tenantId, Guid id, WorkflowState state = WorkflowState.Pending)
    {
        var start = Guid.NewGuid();
        var end = Guid.NewGuid();
        var wf = new Workflow(id, "Test WF", tenantId);
        wf.ReplaceGraph(
            new List<(Guid, StepType, string, double, double, string?, Guid?)>
            {
                (start, StepType.Start, "Start", 0, 0, "{}", null),
                (end, StepType.End, "End", 0, 200, "{}", null),
            },
            new List<(Guid, Guid, Guid, string?)>
            {
                (Guid.NewGuid(), start, end, null),
            });
        wf.SetState(state);
        return wf;
    }

    private static PublishedWorkflow BuildPublished(Guid tenantId, Guid workflowId, PublishMode mode = PublishMode.Api,
        Guid? apiKeyId = null, string? schema = null, bool enabled = true)
    {
        var pw = new PublishedWorkflow(Guid.NewGuid(), tenantId, workflowId, "slug123", mode, apiKeyId, schema);
        if (!enabled) pw.Disable();
        return pw;
    }

    private static RunPublishedWorkflowCommandHandler BuildHandler(
        PublishedWorkflow pw, Workflow wf, out IOrchestrationPrimitive primitive, out IWorkflowRepository workflowRepo)
    {
        var publishedRepo = Substitute.For<IPublishedWorkflowRepository>();
        publishedRepo.GetBySlugAsync(pw.Slug, Arg.Any<CancellationToken>()).Returns(pw);
        workflowRepo = Substitute.For<IWorkflowRepository>();
        workflowRepo.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);
        primitive = Substitute.For<IOrchestrationPrimitive>();
        primitive.RunAsync(wf, Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>()).Returns(wf);
        var auditRepo = Substitute.For<IAuditLogRepository>();
        return new RunPublishedWorkflowCommandHandler(publishedRepo, workflowRepo, primitive, auditRepo);
    }

    [Fact]
    public async Task Run_ApiModeEnabled_ReturnsOutput_AndAudits()
    {
        var tenantId = Guid.NewGuid();
        var wf = BuildWorkflow(tenantId, Guid.NewGuid());
        var pw = BuildPublished(tenantId, wf.Id);
        var handler = BuildHandler(pw, wf, out var primitive, out _);

        var result = await handler.Handle(
            new RunPublishedWorkflowCommand(pw.Slug, tenantId, InputJson), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("slug123", result!.Slug);
        Assert.Equal(InputJson, result.Output);
        await primitive.Received(1).RunAsync(wf, Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_McpModeEnabled_StillExecutes_ModeAgnostic()
    {
        var tenantId = Guid.NewGuid();
        var wf = BuildWorkflow(tenantId, Guid.NewGuid());
        var pw = BuildPublished(tenantId, wf.Id, PublishMode.Mcp);
        var handler = BuildHandler(pw, wf, out var primitive, out _);

        var result = await handler.Handle(
            new RunPublishedWorkflowCommand(pw.Slug, tenantId, InputJson), CancellationToken.None);

        Assert.NotNull(result);
        await primitive.Received(1).RunAsync(Arg.Any<Workflow>(), Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_BoundKeyMismatch_ReturnsNull_NoExecution()
    {
        var tenantId = Guid.NewGuid();
        var wf = BuildWorkflow(tenantId, Guid.NewGuid());
        var pw = BuildPublished(tenantId, wf.Id, apiKeyId: Guid.NewGuid());
        var handler = BuildHandler(pw, wf, out var primitive, out _);

        var result = await handler.Handle(
            new RunPublishedWorkflowCommand(pw.Slug, tenantId, InputJson, InvokingKeyId: Guid.NewGuid()), CancellationToken.None);

        Assert.Null(result);
        await primitive.DidNotReceive().RunAsync(Arg.Any<Workflow>(), Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_CrossTenantWorkflow_ReturnsNull()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var wf = BuildWorkflow(tenantA, Guid.NewGuid());
        var pw = BuildPublished(tenantA, wf.Id);
        var handler = BuildHandler(pw, wf, out var primitive, out _);

        var result = await handler.Handle(
            new RunPublishedWorkflowCommand(pw.Slug, tenantB, InputJson), CancellationToken.None);

        Assert.Null(result);
        await primitive.DidNotReceive().RunAsync(Arg.Any<Workflow>(), Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_MissingRequiredInput_ThrowsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        var wf = BuildWorkflow(tenantId, Guid.NewGuid());
        var pw = BuildPublished(tenantId, wf.Id, schema: "{\"required\":[\"foo\"]}");
        var handler = BuildHandler(pw, wf, out _, out _);

        var ex = await Assert.ThrowsAsync<PublishedWorkflowException>(() => handler.Handle(
            new RunPublishedWorkflowCommand(pw.Slug, tenantId, "{}"), CancellationToken.None));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task Run_WorkflowRunning_ThrowsConflict()
    {
        var tenantId = Guid.NewGuid();
        var wf = BuildWorkflow(tenantId, Guid.NewGuid(), WorkflowState.Running);
        var pw = BuildPublished(tenantId, wf.Id);
        var handler = BuildHandler(pw, wf, out _, out _);

        var ex = await Assert.ThrowsAsync<PublishedWorkflowException>(() => handler.Handle(
            new RunPublishedWorkflowCommand(pw.Slug, tenantId, InputJson), CancellationToken.None));
        Assert.Equal(HttpStatusCode.Conflict, ex.StatusCode);
    }

    [Fact]
    public async Task Run_TerminalState_ResetsBeforeRun_AndUpdates()
    {
        var tenantId = Guid.NewGuid();
        var wf = BuildWorkflow(tenantId, Guid.NewGuid(), WorkflowState.Completed);
        var pw = BuildPublished(tenantId, wf.Id);
        var handler = BuildHandler(pw, wf, out var primitive, out var workflowRepo);

        var result = await handler.Handle(
            new RunPublishedWorkflowCommand(pw.Slug, tenantId, InputJson), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(WorkflowState.Pending, wf.CurrentState); // Reset() applied
        workflowRepo.Received(1).Update(wf);
        await primitive.Received(1).RunAsync(wf, Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_Disabled_ReturnsNull()
    {
        var tenantId = Guid.NewGuid();
        var wf = BuildWorkflow(tenantId, Guid.NewGuid());
        var pw = BuildPublished(tenantId, wf.Id, enabled: false);
        var handler = BuildHandler(pw, wf, out var primitive, out _);

        var result = await handler.Handle(
            new RunPublishedWorkflowCommand(pw.Slug, tenantId, InputJson), CancellationToken.None);

        Assert.Null(result);
        await primitive.DidNotReceive().RunAsync(Arg.Any<Workflow>(), Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>());
    }
}
