using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Conversations.Commands.BindConversationWorkflow;

internal sealed class BindConversationWorkflowCommandHandler
    : IRequestHandler<BindConversationWorkflowCommand, bool>
{
    private readonly IConversationRepository _conversationRepo;
    private readonly IWorkflowRepository _workflowRepo;
    private readonly IConversationWorkflowBindingRepository _bindingRepo;
    private readonly IUnitOfWork _unitOfWork;

    public BindConversationWorkflowCommandHandler(
        IConversationRepository conversationRepo,
        IWorkflowRepository workflowRepo,
        IConversationWorkflowBindingRepository bindingRepo,
        IUnitOfWork unitOfWork)
    {
        _conversationRepo = conversationRepo;
        _workflowRepo = workflowRepo;
        _bindingRepo = bindingRepo;
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> Handle(BindConversationWorkflowCommand request, CancellationToken ct)
    {
        var conversation = await _conversationRepo.GetByIdAsync(request.ConversationId, ct);
        if (conversation is null || conversation.TenantId != request.TenantId)
            return false;

        var workflow = await _workflowRepo.GetByIdAsync(request.WorkflowId, ct);
        if (workflow is null || workflow.TenantId != request.TenantId)
            return false;

        var existing = await _bindingRepo.GetAsync(request.ConversationId, request.WorkflowId, ct);
        if (existing is not null)
            return true; // 已绑定，幂等

        _bindingRepo.Add(new ConversationWorkflowBinding(
            Guid.NewGuid(), request.ConversationId, request.WorkflowId, request.TenantId));
        await _unitOfWork.SaveChangesAsync(ct);
        return true;
    }
}
