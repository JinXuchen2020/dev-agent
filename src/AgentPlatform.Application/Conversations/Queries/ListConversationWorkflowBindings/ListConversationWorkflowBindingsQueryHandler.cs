using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Conversations.Queries.ListConversationWorkflowBindings;

internal sealed class ListConversationWorkflowBindingsQueryHandler
    : IRequestHandler<ListConversationWorkflowBindingsQuery, IReadOnlyList<WorkflowBindingDto>>
{
    private readonly IConversationRepository _conversationRepo;
    private readonly IWorkflowRepository _workflowRepo;
    private readonly IConversationWorkflowBindingRepository _bindingRepo;

    public ListConversationWorkflowBindingsQueryHandler(
        IConversationRepository conversationRepo,
        IWorkflowRepository workflowRepo,
        IConversationWorkflowBindingRepository bindingRepo)
    {
        _conversationRepo = conversationRepo;
        _workflowRepo = workflowRepo;
        _bindingRepo = bindingRepo;
    }

    public async Task<IReadOnlyList<WorkflowBindingDto>> Handle(
        ListConversationWorkflowBindingsQuery request, CancellationToken ct)
    {
        var conversation = await _conversationRepo.GetByIdAsync(request.ConversationId, ct);
        if (conversation is null || conversation.TenantId != request.TenantId)
            return Array.Empty<WorkflowBindingDto>();

        var bindings = await _bindingRepo.GetByConversationAsync(request.ConversationId, ct);
        var dtos = new List<WorkflowBindingDto>(bindings.Count);
        foreach (var binding in bindings)
        {
            var wf = await _workflowRepo.GetByIdAsync(binding.WorkflowId, ct);
            if (wf is null || wf.TenantId != request.TenantId)
                continue; // 租户校验：跳过越界绑定
            dtos.Add(new WorkflowBindingDto(binding.WorkflowId, wf.Name));
        }

        return dtos;
    }
}
