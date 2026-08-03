using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Conversations.Commands.UnbindConversationWorkflow;

internal sealed class UnbindConversationWorkflowCommandHandler
    : IRequestHandler<UnbindConversationWorkflowCommand, bool>
{
    private readonly IConversationWorkflowBindingRepository _bindingRepo;
    private readonly IUnitOfWork _unitOfWork;

    public UnbindConversationWorkflowCommandHandler(
        IConversationWorkflowBindingRepository bindingRepo,
        IUnitOfWork unitOfWork)
    {
        _bindingRepo = bindingRepo;
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> Handle(UnbindConversationWorkflowCommand request, CancellationToken ct)
    {
        var binding = await _bindingRepo.GetAsync(request.ConversationId, request.WorkflowId, ct);
        if (binding is null)
            return true; // 未绑定，幂等

        _bindingRepo.Remove(binding);
        await _unitOfWork.SaveChangesAsync(ct);
        return true;
    }
}
