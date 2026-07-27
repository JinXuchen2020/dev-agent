using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Agents.Commands.DeleteAgent;

internal sealed class DeleteAgentCommandHandler : IRequestHandler<DeleteAgentCommand, bool>
{
    private readonly IAgentRepository _repository;
    private readonly IAuditLogRepository _auditLogRepository;

    public DeleteAgentCommandHandler(
        IAgentRepository repository,
        IAuditLogRepository auditLogRepository)
    {
        _repository = repository;
        _auditLogRepository = auditLogRepository;
    }

    public async Task<bool> Handle(DeleteAgentCommand request, CancellationToken ct)
    {
        var agent = await _repository.GetByIdAsync(request.Id, ct);
        if (agent is null)
            return false;

        _repository.Remove(agent);

        var auditLog = AuditLog.Record(
            tenantId: agent.TenantId,
            action: AgentPlatform.Domain.Aggregates.AuditLogs.AuditActionType.DeleteAgent,
            entity: "Agent",
            userId: null,
            entityId: agent.Id,
            details: $"Deleted agent '{agent.Name}' (role {agent.Role.RoleCode}).");
        _auditLogRepository.Add(auditLog);

        return true;
    }
}
