using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.PublishedWorkflows;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.PublishedWorkflows;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Commands.PublishWorkflow;

internal sealed class PublishWorkflowCommandHandler
    : IRequestHandler<PublishWorkflowCommand, PublishStatusResponse>
{
    private readonly IWorkflowRepository _workflowRepo;
    private readonly IPublishedWorkflowRepository _publishedRepo;
    private readonly IAuditLogRepository _auditLogRepository;

    public PublishWorkflowCommandHandler(
        IWorkflowRepository workflowRepo,
        IPublishedWorkflowRepository publishedRepo,
        IAuditLogRepository auditLogRepository)
    {
        _workflowRepo = workflowRepo;
        _publishedRepo = publishedRepo;
        _auditLogRepository = auditLogRepository;
    }

    public async Task<PublishStatusResponse> Handle(PublishWorkflowCommand request, CancellationToken ct)
    {
        var wf = await _workflowRepo.GetByIdAsync(request.WorkflowId, ct);
        if (wf is null || wf.TenantId != request.TenantId)
            throw PublishedWorkflowException.NotFound(
                $"工作流 '{request.WorkflowId}' 不存在或不属于当前租户。");

        // 同一工作流仅允许一条发布记录：替换既有（删除旧 + 新增新，由 UnitOfWork 统一提交）。
        var existing = await _publishedRepo.GetByWorkflowIdAsync(request.TenantId, request.WorkflowId, ct);
        if (existing is not null)
            _publishedRepo.Delete(existing);

        // 生成租户内唯一的 slug（16 位 URL 安全随机串；极小概率碰撞则重试）。
        var slug = GenerateSlug();
        var attempt = 0;
        while (await _publishedRepo.GetBySlugAsync(slug, ct) is not null && attempt < 5)
        {
            slug = GenerateSlug();
            attempt++;
        }

        if (attempt >= 5)
            throw PublishedWorkflowException.Conflict("生成发布地址失败，请稍后重试。");

        var entity = new PublishedWorkflow(
            Guid.NewGuid(),
            request.TenantId,
            request.WorkflowId,
            slug,
            request.Mode,
            request.ApiKeyId,
            request.InputSchemaJson);
        _publishedRepo.Add(entity);

        _auditLogRepository.Add(AuditLog.Record(
            tenantId: request.TenantId,
            action: AuditActionType.PublishWorkflow,
            entity: "Workflow",
            entityId: request.WorkflowId,
            details: $"发布工作流为 {request.Mode}（slug={slug}）"));

        return ToStatusResponse(entity);
    }

    private static PublishStatusResponse ToStatusResponse(PublishedWorkflow e) =>
        new(e.Id, e.WorkflowId, e.Slug, e.Mode.ToString(), e.IsEnabled, e.ApiKeyId, e.InputSchemaJson, e.CreatedAt);

    private static string GenerateSlug()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var bytes = new byte[16];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        var chars = new char[16];
        for (var i = 0; i < 16; i++)
            chars[i] = alphabet[bytes[i] % alphabet.Length];
        return new string(chars);
    }
}
