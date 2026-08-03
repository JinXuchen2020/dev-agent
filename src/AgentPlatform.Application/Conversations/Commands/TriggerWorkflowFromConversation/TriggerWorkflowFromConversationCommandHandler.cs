using AgentPlatform.Application.WorkflowTriggers;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Conversations.Commands.TriggerWorkflowFromConversation;

internal sealed class TriggerWorkflowFromConversationCommandHandler
    : IRequestHandler<TriggerWorkflowFromConversationCommand, TriggerRunResult?>
{
    private readonly IConversationRepository _conversationRepo;
    private readonly IWorkflowRepository _workflowRepo;
    private readonly IConversationWorkflowBindingRepository _bindingRepo;
    private readonly IMediator _mediator;

    public TriggerWorkflowFromConversationCommandHandler(
        IConversationRepository conversationRepo,
        IWorkflowRepository workflowRepo,
        IConversationWorkflowBindingRepository bindingRepo,
        IMediator mediator)
    {
        _conversationRepo = conversationRepo;
        _workflowRepo = workflowRepo;
        _bindingRepo = bindingRepo;
        _mediator = mediator;
    }

    public async Task<TriggerRunResult?> Handle(
        TriggerWorkflowFromConversationCommand request, CancellationToken ct)
    {
        var conversation = await _conversationRepo.GetByIdAsync(request.ConversationId, ct);
        if (conversation is null || conversation.TenantId != request.TenantId)
            return null;

        var workflow = await _workflowRepo.GetByIdAsync(request.WorkflowId, ct);
        if (workflow is null || workflow.TenantId != request.TenantId)
            return null;

        var binding = await _bindingRepo.GetAsync(request.ConversationId, request.WorkflowId, ct);
        if (binding is null)
            return null; // 未绑定 → 404

        return await _mediator.Send(new TriggerWorkflowCommand(
            request.WorkflowId, request.TenantId, TriggerType.Chat, null), ct);
    }
}
