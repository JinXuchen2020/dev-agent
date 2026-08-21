using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Domain.ValueObjects;
using MediatR;

namespace AgentPlatform.Application.Agents.Commands.UpdateAgent;

internal sealed class UpdateAgentCommandHandler : IRequestHandler<UpdateAgentCommand, Agent?>
{
    private readonly IAgentRepository _repository;
    private readonly IAuditLogRepository _auditLogRepository;

    public UpdateAgentCommandHandler(
        IAgentRepository repository,
        IAuditLogRepository auditLogRepository)
    {
        _repository = repository;
        _auditLogRepository = auditLogRepository;
    }

    public async Task<Agent?> Handle(UpdateAgentCommand request, CancellationToken ct)
    {
        var agent = await _repository.GetByIdAsync(request.Id, ct);
        if (agent is null)
            return null;

        if (!string.IsNullOrWhiteSpace(request.Name))
            agent.UpdateName(request.Name);

        if (!string.IsNullOrWhiteSpace(request.RoleCode))
        {
            var role = AgentType.FromCode(request.RoleCode)
                       ?? new AgentType(request.RoleCode, request.RoleCode, request.RoleCode);
            agent.UpdateRole(role);
        }

        // Rebuild the model endpoint only when at least one model field is supplied,
        // preserving the other existing fields as fallbacks.
        if (request.ModelProvider is not null || request.ModelName is not null || request.ModelApiUrl is not null)
        {
            var provider = request.ModelProvider ?? agent.ModelEndpoint.Provider;
            var modelName = request.ModelName ?? agent.ModelEndpoint.ModelName;
            var apiUrl = request.ModelApiUrl ?? agent.ModelEndpoint.ApiUrl;
            agent.UpdateModelEndpoint(new ModelEndpoint(provider, modelName, apiUrl));
        }

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            agent.UpdateSystemPrompt(request.SystemPrompt);

        if (!string.IsNullOrWhiteSpace(request.Status)
            && Enum.TryParse<AgentStatus>(request.Status, ignoreCase: true, out var status))
        {
            agent.SetStatus(status);
        }

        if (request.AllowedToolNames is not null)
            agent.UpdateAllowedToolNames(request.AllowedToolNames);
        if (request.MaxIterations is not null)
            agent.UpdateMaxIterations(request.MaxIterations.Value);
        if (request.StopCriteria is not null)
            agent.UpdateStopCriteria(request.StopCriteria);

        _repository.Update(agent);

        var auditLog = AuditLog.Record(
            tenantId: agent.TenantId,
            action: AgentPlatform.Domain.Aggregates.AuditLogs.AuditActionType.UpdateAgent,
            entity: "Agent",
            userId: null,
            entityId: agent.Id,
            details: $"Updated agent '{agent.Name}' (role {agent.Role.RoleCode}).");
        _auditLogRepository.Add(auditLog);

        return agent;
    }
}
