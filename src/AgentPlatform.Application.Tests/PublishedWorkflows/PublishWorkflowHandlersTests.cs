using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.PublishedWorkflows;
using AgentPlatform.Application.PublishedWorkflows.Queries.ListMcpTools;
using AgentPlatform.Application.Workflows.Commands.PublishWorkflow;
using AgentPlatform.Application.Workflows.Commands.UnpublishWorkflow;
using AgentPlatform.Application.Workflows.Queries.GetPublishStatus;
using AgentPlatform.Domain;
using AuditLogs = AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.PublishedWorkflows;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.PublishedWorkflows;

/// <summary>
/// F22 单元测试：发布 / 取消发布 / 查询发布状态 / 列举 MCP 工具 的命令与查询处理器。
/// 覆盖租户隔离、重复发布替换、幂等取消、MCP 工具列表筛选等关键不变量。
/// </summary>
public sealed class PublishWorkflowHandlersTests
{
    private static Workflow BuildWorkflow(Guid tenantId, WorkflowState state = WorkflowState.Pending)
    {
        var start = Guid.NewGuid();
        var end = Guid.NewGuid();
        var wf = new Workflow(Guid.NewGuid(), "Test WF", tenantId);
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

    // ---- Publish ----

    [Fact]
    public async Task Publish_CreatesRecord_AndAudits_AndReturnsSlug()
    {
        var tenantId = Guid.NewGuid();
        var wf = BuildWorkflow(tenantId);
        var workflowRepo = Substitute.For<IWorkflowRepository>();
        workflowRepo.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);
        var publishedRepo = Substitute.For<IPublishedWorkflowRepository>();
        publishedRepo.GetBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((PublishedWorkflow?)null);
        var auditRepo = Substitute.For<IAuditLogRepository>();

        var handler = new PublishWorkflowCommandHandler(workflowRepo, publishedRepo, auditRepo);
        var result = await handler.Handle(
            new PublishWorkflowCommand(wf.Id, PublishMode.Api, null, null, tenantId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(16, result!.Slug.Length);
        Assert.Equal("Api", result.Mode);
        publishedRepo.Received(1).Add(Arg.Any<PublishedWorkflow>());
        auditRepo.Received().Add(Arg.Is<AuditLogs.AuditLog>(a => a.Action == AuditLogs.AuditActionType.PublishWorkflow));
    }

    [Fact]
    public async Task Publish_CrossTenantWorkflow_ThrowsNotFound()
    {
        var wf = BuildWorkflow(Guid.NewGuid());
        var workflowRepo = Substitute.For<IWorkflowRepository>();
        workflowRepo.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);
        var handler = new PublishWorkflowCommandHandler(
            workflowRepo, Substitute.For<IPublishedWorkflowRepository>(), Substitute.For<IAuditLogRepository>());

        await Assert.ThrowsAsync<PublishedWorkflowException>(() => handler.Handle(
            new PublishWorkflowCommand(wf.Id, PublishMode.Api, null, null, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Publish_ReplacesExistingRecord()
    {
        var tenantId = Guid.NewGuid();
        var wf = BuildWorkflow(tenantId);
        var existing = new PublishedWorkflow(Guid.NewGuid(), tenantId, wf.Id, "oldSlug", PublishMode.Api);
        var workflowRepo = Substitute.For<IWorkflowRepository>();
        workflowRepo.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);
        var publishedRepo = Substitute.For<IPublishedWorkflowRepository>();
        publishedRepo.GetByWorkflowIdAsync(tenantId, wf.Id, Arg.Any<CancellationToken>()).Returns(existing);
        publishedRepo.GetBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((PublishedWorkflow?)null);

        var handler = new PublishWorkflowCommandHandler(workflowRepo, publishedRepo, Substitute.For<IAuditLogRepository>());
        await handler.Handle(
            new PublishWorkflowCommand(wf.Id, PublishMode.Mcp, null, null, tenantId), CancellationToken.None);

        publishedRepo.Received(1).Delete(existing);
        publishedRepo.Received(1).Add(Arg.Is<PublishedWorkflow>(p => p.Mode == PublishMode.Mcp));
    }

    // ---- Unpublish ----

    [Fact]
    public async Task Unpublish_Existing_DeletesAndAudits()
    {
        var tenantId = Guid.NewGuid();
        var existing = new PublishedWorkflow(Guid.NewGuid(), tenantId, Guid.NewGuid(), "slug", PublishMode.Api);
        var publishedRepo = Substitute.For<IPublishedWorkflowRepository>();
        publishedRepo.GetByWorkflowIdAsync(tenantId, existing.WorkflowId, Arg.Any<CancellationToken>()).Returns(existing);
        var auditRepo = Substitute.For<IAuditLogRepository>();

        var handler = new UnpublishWorkflowCommandHandler(publishedRepo, auditRepo);
        await handler.Handle(new UnpublishWorkflowCommand(existing.WorkflowId, tenantId), CancellationToken.None);

        publishedRepo.Received(1).Delete(existing);
        auditRepo.Received().Add(Arg.Is<AuditLogs.AuditLog>(a => a.Action == AuditLogs.AuditActionType.UnpublishWorkflow));
    }

    [Fact]
    public async Task Unpublish_NotPublished_IsIdempotentNoOp()
    {
        var tenantId = Guid.NewGuid();
        var publishedRepo = Substitute.For<IPublishedWorkflowRepository>();
        publishedRepo.GetByWorkflowIdAsync(tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PublishedWorkflow?)null);

        var handler = new UnpublishWorkflowCommandHandler(publishedRepo, Substitute.For<IAuditLogRepository>());
        await handler.Handle(new UnpublishWorkflowCommand(Guid.NewGuid(), tenantId), CancellationToken.None);

        publishedRepo.DidNotReceive().Delete(Arg.Any<PublishedWorkflow>());
    }

    // ---- GetPublishStatus ----

    [Fact]
    public async Task GetPublishStatus_Existing_ReturnsResponse()
    {
        var tenantId = Guid.NewGuid();
        var existing = new PublishedWorkflow(Guid.NewGuid(), tenantId, Guid.NewGuid(), "slug", PublishMode.Api);
        var publishedRepo = Substitute.For<IPublishedWorkflowRepository>();
        publishedRepo.GetByWorkflowIdAsync(tenantId, existing.WorkflowId, Arg.Any<CancellationToken>()).Returns(existing);

        var handler = new GetPublishStatusQueryHandler(publishedRepo);
        var result = await handler.Handle(new GetPublishStatusQuery(existing.WorkflowId, tenantId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("slug", result!.Slug);
    }

    [Fact]
    public async Task GetPublishStatus_NotPublished_ReturnsNull()
    {
        var tenantId = Guid.NewGuid();
        var publishedRepo = Substitute.For<IPublishedWorkflowRepository>();
        publishedRepo.GetByWorkflowIdAsync(tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PublishedWorkflow?)null);

        var handler = new GetPublishStatusQueryHandler(publishedRepo);
        var result = await handler.Handle(new GetPublishStatusQuery(Guid.NewGuid(), tenantId), CancellationToken.None);

        Assert.Null(result);
    }

    // ---- ListMcpTools ----

    [Fact]
    public async Task ListMcpTools_ReturnsOnlyEnabledMcpMode()
    {
        var tenantId = Guid.NewGuid();
        // 模拟仓储在 enabledOnly:true 时已过滤——仅返回 Enabled && Mcp 的记录。
        var enabledMcp = new PublishedWorkflow(Guid.NewGuid(), tenantId, Guid.NewGuid(), "mcpA", PublishMode.Mcp);

        var publishedRepo = Substitute.For<IPublishedWorkflowRepository>();
        publishedRepo.GetByTenantAndModeAsync(tenantId, PublishMode.Mcp, true, Arg.Any<CancellationToken>())
            .Returns(new List<PublishedWorkflow> { enabledMcp });

        var handler = new ListMcpToolsQueryHandler(publishedRepo);
        var tools = await handler.Handle(new ListMcpToolsQuery(tenantId), CancellationToken.None);

        Assert.Single(tools);
        Assert.Equal("mcpA", tools[0].Name);
        Assert.Equal("mcpA", tools[0].Description);
    }
}
