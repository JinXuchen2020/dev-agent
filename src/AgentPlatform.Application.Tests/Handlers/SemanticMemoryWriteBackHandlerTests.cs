using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.EventHandlers;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Aggregates.Workflows.Events;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Handlers;

/// <summary>
/// F33 ②：episodic 写回 handler——完成/回滚事件把「结局+步骤摘要」沉淀进语义记忆；
/// Enabled=false 时静默跳过；记忆服务异常不影响主流程。
/// </summary>
public sealed class SemanticMemoryWriteBackHandlerTests
{
    private readonly ISemanticMemoryService _memory = Substitute.For<ISemanticMemoryService>();
    private readonly IWorkflowRepository _repository = Substitute.For<IWorkflowRepository>();

    private SemanticMemoryWriteBackHandler CreateHandler(bool enabled = true) =>
        new(_memory, _repository,
            Options.Create(new SemanticMemorySettings { Enabled = enabled }),
            Substitute.For<ILogger<SemanticMemoryWriteBackHandler>>());

    private static Workflow CreateWorkflowWithSteps()
    {
        var wf = new Workflow(Guid.NewGuid(), "测试工作流", Guid.NewGuid());
        wf.AddStep(new WorkflowStep(Guid.NewGuid(), 0, "Architect"));
        wf.Steps[0].SetResult("架构设计产出内容");
        wf.Steps[0].SetState(WorkflowState.Completed);
        return wf;
    }

    [Fact]
    public async Task WorkflowCompleted_Writes_Episodic_Memory()
    {
        var wf = CreateWorkflowWithSteps();
        _repository.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);

        await CreateHandler().Handle(
            new DomainEventNotification<WorkflowCompleted>(
                new WorkflowCompleted(wf.Id, wf.Name, wf.Steps.Count, wf.TenantId)),
            CancellationToken.None);

        await _memory.Received(1).RememberRunAsync(
            wf.TenantId, wf.Id, "测试工作流", "completed",
            Arg.Is<string>(d => d.Contains("Architect") && d.Contains("架构设计产出内容")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RolledBack_Writes_Failure_Lesson_WithErrorDetail()
    {
        var wf = CreateWorkflowWithSteps();
        _repository.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);

        await CreateHandler().Handle(
            new DomainEventNotification<WorkflowRolledBack>(
                new WorkflowRolledBack(wf.Id, wf.Name, "Architect",
                    "Rolled back from step order 0: model timeout", wf.TenantId)),
            CancellationToken.None);

        await _memory.Received(1).RememberRunAsync(
            wf.TenantId, wf.Id, "测试工作流", "rolled_back",
            Arg.Is<string>(d => d.Contains("model timeout")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Disabled_Skips_WriteBack()
    {
        var wf = CreateWorkflowWithSteps();
        _repository.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);

        await CreateHandler(enabled: false).Handle(
            new DomainEventNotification<WorkflowCompleted>(
                new WorkflowCompleted(wf.Id, wf.Name, wf.Steps.Count, wf.TenantId)),
            CancellationToken.None);

        await _memory.DidNotReceive().RememberRunAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}