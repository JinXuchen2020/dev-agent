using AgentPlatform.Application.Workflows.Versioning;
using AgentPlatform.Application.WorkflowTemplates;
using AgentPlatform.Domain;
using AuditLogs = AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Aggregates.WorkflowTemplates;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.WorkflowTemplates;

/// <summary>
/// F23 单元测试：CloneWorkflowTemplateCommandHandler。
/// 验证「平台模板 → 租户工作流」克隆的关键不变量：
/// 副本命名、Agent 解绑（决策 S3）、审计动作、缺失模板的幂等空返回。
/// </summary>
public sealed class CloneWorkflowTemplateCommandHandlerTests
{
    private static string BuildValidSnapshotJson()
    {
        var start = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var end = Guid.NewGuid();
        var snapshot = new WorkflowGraphSnapshot(
            "Research template context",
            new List<WorkflowVersionNode>
            {
                // 模板可自带 Agent 引用（平台模板不绑定任何租户 Agent），克隆必须丢弃。
                new(start, StepType.Start, "Start", 0, 0, "{}", Guid.NewGuid()),
                new(mid, StepType.LLM, "Summarize", 0, 100, "{\"model\":\"gpt-4o\"}", Guid.NewGuid()),
                new(end, StepType.End, "End", 0, 200, "{}", Guid.NewGuid()),
            },
            new List<WorkflowVersionEdge>
            {
                // 链式连通：Start → Summarize → End，确保 ValidateGraph 通过。
                new(Guid.NewGuid(), start, mid, null),
                new(Guid.NewGuid(), mid, end, null),
            });
        return snapshot.ToJson();
    }

    [Fact]
    public async Task Clone_ExistingTemplate_CreatesAgentlessWorkflow_AndAudits()
    {
        var tenantId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var template = new WorkflowTemplate(
            templateId, "Research Template", WorkflowTemplateCategory.KnowledgeQa,
            "A knowledge QA starter", BuildValidSnapshotJson(), new[] { "research", "qa" });

        var templateRepo = Substitute.For<IWorkflowTemplateRepository>();
        templateRepo.GetByIdAsync(templateId, Arg.Any<CancellationToken>()).Returns(template);
        var workflowRepo = Substitute.For<IWorkflowRepository>();
        var auditRepo = Substitute.For<IAuditLogRepository>();

        var handler = new CloneWorkflowTemplateCommandHandler(templateRepo, workflowRepo, auditRepo);
        var result = await handler.Handle(
            new CloneWorkflowTemplateCommand(templateId, tenantId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal($"{template.Name} (副本)", result!.Name);
        Assert.Equal(3, result.Nodes.Count);
        // 决策 S3：所有节点 Agent 绑定必须被丢弃。
        Assert.All(result.Nodes, n => Assert.Null(n.AssignedAgentId));
        // 新工作流归属于调用方租户。
        workflowRepo.Received(1).Add(Arg.Is<Workflow>(w => w.TenantId == tenantId));
        auditRepo.Received().Add(Arg.Is<AuditLogs.AuditLog>(a =>
            a.Action == AuditLogs.AuditActionType.CloneTemplate && a.TenantId == tenantId));
    }

    [Fact]
    public async Task Clone_MissingTemplate_ReturnsNull_AndNoSideEffects()
    {
        var templateRepo = Substitute.For<IWorkflowTemplateRepository>();
        templateRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((WorkflowTemplate?)null);
        var workflowRepo = Substitute.For<IWorkflowRepository>();
        var auditRepo = Substitute.For<IAuditLogRepository>();

        var handler = new CloneWorkflowTemplateCommandHandler(templateRepo, workflowRepo, auditRepo);
        var result = await handler.Handle(
            new CloneWorkflowTemplateCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Null(result);
        workflowRepo.DidNotReceive().Add(Arg.Any<Workflow>());
        auditRepo.DidNotReceive().Add(Arg.Any<AuditLogs.AuditLog>());
    }
}
