using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Conversations.Commands.BindConversationWorkflow;
using AgentPlatform.Application.Conversations.Commands.TriggerWorkflowFromConversation;
using AgentPlatform.Application.Conversations.Queries.ListConversationWorkflowBindings;
using AgentPlatform.Application.WorkflowTriggers;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Aggregates.WorkflowTriggers;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Handlers;

/// <summary>F21 触发器相关 handler 单元测试（NSubstitute + xunit）。</summary>
public sealed class WorkflowTriggersHandlersTests
{
    #region GenerateWebhookTokenCommandHandler

    [Fact]
    public async Task GenerateWebhookToken_Should_Create_When_Not_Exists()
    {
        var workflowId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var workflow = new Workflow(workflowId, "wf", tenantId);
        var workflowRepo = Substitute.For<IWorkflowRepository>();
        var triggerRepo = Substitute.For<IWorkflowTriggerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var audit = Substitute.For<IAuditLogRepository>();

        workflowRepo.GetByIdAsync(workflowId, Arg.Any<CancellationToken>()).Returns(workflow);
        triggerRepo.GetByWorkflowAndTypeAsync(workflowId, TriggerType.Webhook, Arg.Any<CancellationToken>())
            .Returns((WorkflowTrigger?)null);

        var handler = new GenerateWebhookTokenCommandHandler(workflowRepo, triggerRepo, unitOfWork, audit);
        var result = await handler.Handle(new GenerateWebhookTokenCommand(workflowId, tenantId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Created);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        triggerRepo.Received(1).Add(Arg.Any<WorkflowTrigger>());
    }

    [Fact]
    public async Task GenerateWebhookToken_Should_Reuse_Existing_Token_When_Exists()
    {
        var workflowId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var workflow = new Workflow(workflowId, "wf", tenantId);
        var existing = WorkflowTrigger.CreateWebhook(Guid.NewGuid(), workflowId, tenantId, "existing-token");
        var workflowRepo = Substitute.For<IWorkflowRepository>();
        var triggerRepo = Substitute.For<IWorkflowTriggerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var audit = Substitute.For<IAuditLogRepository>();

        workflowRepo.GetByIdAsync(workflowId, Arg.Any<CancellationToken>()).Returns(workflow);
        triggerRepo.GetByWorkflowAndTypeAsync(workflowId, TriggerType.Webhook, Arg.Any<CancellationToken>())
            .Returns(existing);

        var handler = new GenerateWebhookTokenCommandHandler(workflowRepo, triggerRepo, unitOfWork, audit);
        var result = await handler.Handle(new GenerateWebhookTokenCommand(workflowId, tenantId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result!.Created);
        Assert.Equal("existing-token", result.Token); // 不轮换
        triggerRepo.DidNotReceive().Add(Arg.Any<WorkflowTrigger>());
    }

    [Fact]
    public async Task GenerateWebhookToken_Should_Return_Null_When_Workflow_Not_Found()
    {
        var workflowId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var workflowRepo = Substitute.For<IWorkflowRepository>();
        var triggerRepo = Substitute.For<IWorkflowTriggerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var audit = Substitute.For<IAuditLogRepository>();

        workflowRepo.GetByIdAsync(workflowId, Arg.Any<CancellationToken>()).Returns((Workflow?)null);

        var handler = new GenerateWebhookTokenCommandHandler(workflowRepo, triggerRepo, unitOfWork, audit);
        var result = await handler.Handle(new GenerateWebhookTokenCommand(workflowId, tenantId), CancellationToken.None);

        Assert.Null(result);
    }

    #endregion

    #region DisableWebhookTriggerCommandHandler

    [Fact]
    public async Task DisableWebhookTrigger_Should_Be_Idempotent_When_No_Trigger()
    {
        var workflowId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var triggerRepo = Substitute.For<IWorkflowTriggerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var audit = Substitute.For<IAuditLogRepository>();

        triggerRepo.GetByWorkflowAndTypeAsync(workflowId, TriggerType.Webhook, Arg.Any<CancellationToken>())
            .Returns((WorkflowTrigger?)null);

        var handler = new DisableWebhookTriggerCommandHandler(triggerRepo, unitOfWork, audit);
        var ok = await handler.Handle(new DisableWebhookTriggerCommand(workflowId, tenantId), CancellationToken.None);

        Assert.True(ok);
    }

    [Fact]
    public async Task DisableWebhookTrigger_Should_Disable_Existing()
    {
        var workflowId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var existing = WorkflowTrigger.CreateWebhook(Guid.NewGuid(), workflowId, tenantId, "tok");
        var triggerRepo = Substitute.For<IWorkflowTriggerRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var audit = Substitute.For<IAuditLogRepository>();

        triggerRepo.GetByWorkflowAndTypeAsync(workflowId, TriggerType.Webhook, Arg.Any<CancellationToken>())
            .Returns(existing);

        var handler = new DisableWebhookTriggerCommandHandler(triggerRepo, unitOfWork, audit);
        await handler.Handle(new DisableWebhookTriggerCommand(workflowId, tenantId), CancellationToken.None);

        Assert.False(existing.Enabled);
        triggerRepo.Received(1).Update(existing);
    }

    #endregion

    #region PutScheduleTriggerCommandHandler

    [Fact]
    public async Task PutScheduleTrigger_Should_Compute_NextRun_When_Enabled()
    {
        var workflowId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var workflow = new Workflow(workflowId, "wf", tenantId);
        var workflowRepo = Substitute.For<IWorkflowRepository>();
        var triggerRepo = Substitute.For<IWorkflowTriggerRepository>();
        var calculator = Substitute.For<IScheduleCalculator>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var audit = Substitute.For<IAuditLogRepository>();
        var next = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        workflowRepo.GetByIdAsync(workflowId, Arg.Any<CancellationToken>()).Returns(workflow);
        calculator.ComputeNextRunUtc("0 0 * * *", "UTC", Arg.Any<DateTime>()).Returns(next);

        var handler = new PutScheduleTriggerCommandHandler(workflowRepo, triggerRepo, calculator, unitOfWork, audit);
        var result = await handler.Handle(
            new PutScheduleTriggerCommand(workflowId, tenantId, "0 0 * * *", "UTC", true), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Enabled);
        Assert.Equal(next, result.NextRunAt);
    }

    [Fact]
    public async Task PutScheduleTrigger_Should_Null_NextRun_When_Disabled()
    {
        var workflowId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var workflow = new Workflow(workflowId, "wf", tenantId);
        var workflowRepo = Substitute.For<IWorkflowRepository>();
        var triggerRepo = Substitute.For<IWorkflowTriggerRepository>();
        var calculator = Substitute.For<IScheduleCalculator>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var audit = Substitute.For<IAuditLogRepository>();

        workflowRepo.GetByIdAsync(workflowId, Arg.Any<CancellationToken>()).Returns(workflow);

        var handler = new PutScheduleTriggerCommandHandler(workflowRepo, triggerRepo, calculator, unitOfWork, audit);
        var result = await handler.Handle(
            new PutScheduleTriggerCommand(workflowId, tenantId, "0 0 * * *", "UTC", false), CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result!.Enabled);
        Assert.Null(result.NextRunAt);
        calculator.DidNotReceive().ComputeNextRunUtc(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>());
    }

    #endregion

    #region GetWorkflowTriggersQueryHandler

    [Fact]
    public async Task GetWorkflowTriggers_Should_Shape_Webhook_Schedule_And_ChatCount()
    {
        var workflowId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var webhook = WorkflowTrigger.CreateWebhook(Guid.NewGuid(), workflowId, tenantId, "tok");
        var schedule = WorkflowTrigger.CreateSchedule(
            Guid.NewGuid(), workflowId, tenantId, "0 0 * * *", "Asia/Shanghai", true,
            new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var bindings = new List<ConversationWorkflowBinding>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), workflowId, tenantId),
            new(Guid.NewGuid(), Guid.NewGuid(), workflowId, tenantId)
        };
        var triggerRepo = Substitute.For<IWorkflowTriggerRepository>();
        var bindingRepo = Substitute.For<IConversationWorkflowBindingRepository>();

        triggerRepo.ListByWorkflowAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(new List<WorkflowTrigger> { webhook, schedule });
        bindingRepo.GetByWorkflowAsync(workflowId, Arg.Any<CancellationToken>()).Returns(bindings);

        var handler = new GetWorkflowTriggersQueryHandler(triggerRepo, bindingRepo);
        var result = await handler.Handle(
            new GetWorkflowTriggersQuery(workflowId, tenantId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result!.Webhook);
        Assert.Equal("tok", result.Webhook!.TriggerToken);
        Assert.True(result.Webhook.Enabled);
        Assert.NotNull(result.Schedule);
        Assert.Equal("0 0 * * *", result.Schedule!.Cron);
        Assert.Equal("Asia/Shanghai", result.Schedule.Timezone);
        Assert.Equal(2, result.ChatBindingCount);
    }

    [Fact]
    public async Task GetWorkflowTriggers_Should_Return_Nulls_When_No_Triggers()
    {
        var workflowId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var triggerRepo = Substitute.For<IWorkflowTriggerRepository>();
        var bindingRepo = Substitute.For<IConversationWorkflowBindingRepository>();

        triggerRepo.ListByWorkflowAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(new List<WorkflowTrigger>());
        bindingRepo.GetByWorkflowAsync(workflowId, Arg.Any<CancellationToken>())
            .Returns(new List<ConversationWorkflowBinding>());

        var handler = new GetWorkflowTriggersQueryHandler(triggerRepo, bindingRepo);
        var result = await handler.Handle(
            new GetWorkflowTriggersQuery(workflowId, tenantId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result!.Webhook);
        Assert.Null(result.Schedule);
        Assert.Equal(0, result.ChatBindingCount);
    }

    #endregion

    #region BindConversationWorkflowCommandHandler

    [Fact]
    public async Task BindWorkflow_Should_Bind_When_Valid()
    {
        var conversationId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var conversation = new Conversation(conversationId, tenantId);
        var workflow = new Workflow(workflowId, "wf", tenantId);
        var conversationRepo = Substitute.For<IConversationRepository>();
        var workflowRepo = Substitute.For<IWorkflowRepository>();
        var bindingRepo = Substitute.For<IConversationWorkflowBindingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        conversationRepo.GetByIdAsync(conversationId, Arg.Any<CancellationToken>()).Returns(conversation);
        workflowRepo.GetByIdAsync(workflowId, Arg.Any<CancellationToken>()).Returns(workflow);
        bindingRepo.GetAsync(conversationId, workflowId, Arg.Any<CancellationToken>())
            .Returns((ConversationWorkflowBinding?)null);

        var handler = new BindConversationWorkflowCommandHandler(conversationRepo, workflowRepo, bindingRepo, unitOfWork);
        var ok = await handler.Handle(
            new BindConversationWorkflowCommand(conversationId, workflowId, tenantId), CancellationToken.None);

        Assert.True(ok);
        bindingRepo.Received(1).Add(Arg.Any<ConversationWorkflowBinding>());
    }

    [Fact]
    public async Task BindWorkflow_Should_Reject_CrossTenant_Workflow()
    {
        var conversationId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var conversation = new Conversation(conversationId, tenantId);
        var otherTenantWorkflow = new Workflow(workflowId, "wf", Guid.NewGuid());
        var conversationRepo = Substitute.For<IConversationRepository>();
        var workflowRepo = Substitute.For<IWorkflowRepository>();
        var bindingRepo = Substitute.For<IConversationWorkflowBindingRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        conversationRepo.GetByIdAsync(conversationId, Arg.Any<CancellationToken>()).Returns(conversation);
        workflowRepo.GetByIdAsync(workflowId, Arg.Any<CancellationToken>()).Returns(otherTenantWorkflow);

        var handler = new BindConversationWorkflowCommandHandler(conversationRepo, workflowRepo, bindingRepo, unitOfWork);
        var ok = await handler.Handle(
            new BindConversationWorkflowCommand(conversationId, workflowId, tenantId), CancellationToken.None);

        Assert.False(ok);
        bindingRepo.DidNotReceive().Add(Arg.Any<ConversationWorkflowBinding>());
    }

    #endregion

    #region TriggerWorkflowFromConversationCommandHandler

    [Fact]
    public async Task TriggerFromConversation_Should_Delegate_When_Bound()
    {
        var conversationId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var conversation = new Conversation(conversationId, tenantId);
        var workflow = new Workflow(workflowId, "wf", tenantId);
        var binding = new ConversationWorkflowBinding(Guid.NewGuid(), conversationId, workflowId, tenantId);
        var conversationRepo = Substitute.For<IConversationRepository>();
        var workflowRepo = Substitute.For<IWorkflowRepository>();
        var bindingRepo = Substitute.For<IConversationWorkflowBindingRepository>();
        var mediator = Substitute.For<IMediator>();
        var expected = new TriggerRunResult(workflowId, "wf", WorkflowState.Pending);

        conversationRepo.GetByIdAsync(conversationId, Arg.Any<CancellationToken>()).Returns(conversation);
        workflowRepo.GetByIdAsync(workflowId, Arg.Any<CancellationToken>()).Returns(workflow);
        bindingRepo.GetAsync(conversationId, workflowId, Arg.Any<CancellationToken>()).Returns(binding);
        mediator.Send(Arg.Any<TriggerWorkflowCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TriggerRunResult?>(expected));

        var handler = new TriggerWorkflowFromConversationCommandHandler(
            conversationRepo, workflowRepo, bindingRepo, mediator);
        var result = await handler.Handle(
            new TriggerWorkflowFromConversationCommand(conversationId, workflowId, tenantId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(workflowId, result!.WorkflowId);
        await mediator.Received(1).Send(Arg.Any<TriggerWorkflowCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TriggerFromConversation_Should_Return_Null_When_Not_Bound()
    {
        var conversationId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var conversation = new Conversation(conversationId, tenantId);
        var workflow = new Workflow(workflowId, "wf", tenantId);
        var conversationRepo = Substitute.For<IConversationRepository>();
        var workflowRepo = Substitute.For<IWorkflowRepository>();
        var bindingRepo = Substitute.For<IConversationWorkflowBindingRepository>();
        var mediator = Substitute.For<IMediator>();

        conversationRepo.GetByIdAsync(conversationId, Arg.Any<CancellationToken>()).Returns(conversation);
        workflowRepo.GetByIdAsync(workflowId, Arg.Any<CancellationToken>()).Returns(workflow);
        bindingRepo.GetAsync(conversationId, workflowId, Arg.Any<CancellationToken>())
            .Returns((ConversationWorkflowBinding?)null);

        var handler = new TriggerWorkflowFromConversationCommandHandler(
            conversationRepo, workflowRepo, bindingRepo, mediator);
        var result = await handler.Handle(
            new TriggerWorkflowFromConversationCommand(conversationId, workflowId, tenantId), CancellationToken.None);

        Assert.Null(result);
    }

    #endregion

    #region InvokeWebhookCommandHandler

    [Fact]
    public async Task InvokeWebhook_Should_Return_Null_When_Token_Not_Found()
    {
        var triggerRepo = Substitute.For<IWorkflowTriggerRepository>();
        var audit = Substitute.For<IAuditLogRepository>();
        var mediator = Substitute.For<IMediator>();

        triggerRepo.GetByTokenAsync("missing", Arg.Any<CancellationToken>()).Returns((WorkflowTrigger?)null);

        var handler = new InvokeWebhookCommandHandler(triggerRepo, audit, mediator);
        var result = await handler.Handle(new InvokeWebhookCommand("missing", "{}"), CancellationToken.None);

        Assert.Null(result);
        await mediator.DidNotReceive().Send(Arg.Any<TriggerWorkflowCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeWebhook_Should_Return_Null_When_Disabled()
    {
        var trigger = WorkflowTrigger.CreateWebhook(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "tok");
        // 通过反射关闭 Enabled（private set），覆盖禁用分支。
        trigger.GetType().GetProperty("Enabled")!.SetValue(trigger, false);
        var triggerRepo = Substitute.For<IWorkflowTriggerRepository>();
        var audit = Substitute.For<IAuditLogRepository>();
        var mediator = Substitute.For<IMediator>();

        triggerRepo.GetByTokenAsync("tok", Arg.Any<CancellationToken>()).Returns(trigger);

        var handler = new InvokeWebhookCommandHandler(triggerRepo, audit, mediator);
        var result = await handler.Handle(new InvokeWebhookCommand("tok", "{}"), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task InvokeWebhook_Should_Delegate_When_Enabled()
    {
        var workflowId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var trigger = WorkflowTrigger.CreateWebhook(Guid.NewGuid(), workflowId, tenantId, "tok");
        var triggerRepo = Substitute.For<IWorkflowTriggerRepository>();
        var audit = Substitute.For<IAuditLogRepository>();
        var mediator = Substitute.For<IMediator>();
        var expected = new TriggerRunResult(workflowId, "wf", WorkflowState.Pending);

        triggerRepo.GetByTokenAsync("tok", Arg.Any<CancellationToken>()).Returns(trigger);
        mediator.Send(Arg.Any<TriggerWorkflowCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TriggerRunResult?>(expected));

        var handler = new InvokeWebhookCommandHandler(triggerRepo, audit, mediator);
        var result = await handler.Handle(new InvokeWebhookCommand("tok", "{\"x\":1}"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(workflowId, result!.WorkflowId);
        audit.Received(1).Add(Arg.Any<AuditLog>());
    }

    #endregion

    #region TriggerWorkflowCommandHandler

    [Fact]
    public async Task TriggerWorkflow_Should_Inject_Tenant_And_Restore_Context()
    {
        var workflowId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var wf = new Workflow(workflowId, "wf", tenantId);
        wf.UpdateContext("{\"foo\":1}");
        var originalContext = wf.Context;

        var workflowRepo = Substitute.For<IWorkflowRepository>();
        var primitive = Substitute.For<IOrchestrationPrimitive>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var audit = Substitute.For<IAuditLogRepository>();
        var tenantContext = Substitute.For<ITenantContext>();
        var workspaceContext = Substitute.For<IWorkspaceContext>();
        var workspaceDirectory = Substitute.For<IWorkspaceDirectory>();

        workflowRepo.GetByIdForTriggerAsync(workflowId, tenantId, Arg.Any<CancellationToken>()).Returns(wf);
        primitive.RunAsync(Arg.Any<Workflow>(), Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(wf));

        var handler = new TriggerWorkflowCommandHandler(workflowRepo, primitive, unitOfWork, audit, tenantContext, workspaceContext, workspaceDirectory);
        var result = await handler.Handle(
            new TriggerWorkflowCommand(workflowId, tenantId, TriggerType.Webhook, "{\"a\":1}"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(workflowId, result!.WorkflowId);
        Assert.Equal(tenantId, tenantContext.OverrideTenantId);
        Assert.Equal(originalContext, wf.Context); // 运行后还原原始 Context
        await primitive.Received(1).RunAsync(Arg.Any<Workflow>(), Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>());
        audit.Received(1).Add(Arg.Any<AuditLog>());
    }

    [Fact]
    public async Task TriggerWorkflow_Should_Return_Null_When_NotFound()
    {
        var workflowId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var workflowRepo = Substitute.For<IWorkflowRepository>();
        var primitive = Substitute.For<IOrchestrationPrimitive>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var audit = Substitute.For<IAuditLogRepository>();
        var tenantContext = Substitute.For<ITenantContext>();
        var workspaceContext = Substitute.For<IWorkspaceContext>();
        var workspaceDirectory = Substitute.For<IWorkspaceDirectory>();

        workflowRepo.GetByIdForTriggerAsync(workflowId, tenantId, Arg.Any<CancellationToken>())
            .Returns((Workflow?)null);

        var handler = new TriggerWorkflowCommandHandler(workflowRepo, primitive, unitOfWork, audit, tenantContext, workspaceContext, workspaceDirectory);
        var result = await handler.Handle(
            new TriggerWorkflowCommand(workflowId, tenantId, TriggerType.Webhook, null), CancellationToken.None);

        Assert.Null(result);
        await primitive.DidNotReceive().RunAsync(Arg.Any<Workflow>(), Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>());
    }

    #endregion
}
