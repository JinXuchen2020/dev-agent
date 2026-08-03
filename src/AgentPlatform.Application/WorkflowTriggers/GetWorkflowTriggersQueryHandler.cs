using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.WorkflowTriggers;

internal sealed class GetWorkflowTriggersQueryHandler
    : IRequestHandler<GetWorkflowTriggersQuery, WorkflowTriggersResponse?>
{
    private readonly IWorkflowTriggerRepository _triggerRepo;
    private readonly IConversationWorkflowBindingRepository _bindingRepo;

    public GetWorkflowTriggersQueryHandler(
        IWorkflowTriggerRepository triggerRepo,
        IConversationWorkflowBindingRepository bindingRepo)
    {
        _triggerRepo = triggerRepo;
        _bindingRepo = bindingRepo;
    }

    public async Task<WorkflowTriggersResponse?> Handle(GetWorkflowTriggersQuery request, CancellationToken ct)
    {
        var triggers = await _triggerRepo.ListByWorkflowAsync(request.WorkflowId, ct);
        var webhook = triggers.FirstOrDefault(t => t.Type == TriggerType.Webhook);
        var schedule = triggers.FirstOrDefault(t => t.Type == TriggerType.Schedule);

        var bindings = await _bindingRepo.GetByWorkflowAsync(request.WorkflowId, ct);

        return new WorkflowTriggersResponse(
            webhook is null ? null : new WebhookTriggerView(webhook.TriggerToken, webhook.Enabled),
            schedule is null
                ? null
                : new ScheduleTriggerView(schedule.Cron, schedule.Timezone, schedule.Enabled, schedule.NextRunAt),
            bindings.Count);
    }
}
