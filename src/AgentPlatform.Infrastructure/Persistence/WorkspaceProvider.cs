using AgentPlatform.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace AgentPlatform.Infrastructure.Persistence;

/// <summary>
/// Provides the current workspace identifier resolved from the request context (F35).
/// Resolution order: <see cref="IWorkspaceContext.OverrideWorkspaceId"/> (background /
/// anonymous scope injection) → JWT "workspace_id" claim → "X-Workspace-Id" header →
/// tenant default workspace via <see cref="IWorkspaceDirectory"/> → <see cref="Guid.Empty"/>
/// (no resolvable context; the workspace query filter then matches nothing — honest isolation).
/// </summary>
internal sealed class WorkspaceProvider : IWorkspaceProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IWorkspaceContext _workspaceContext;
    private readonly IWorkspaceDirectory _directory;
    private readonly ITenantProvider _tenantProvider;

    public WorkspaceProvider(
        IHttpContextAccessor httpContextAccessor,
        IWorkspaceContext workspaceContext,
        IWorkspaceDirectory directory,
        ITenantProvider tenantProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _workspaceContext = workspaceContext;
        _directory = directory;
        _tenantProvider = tenantProvider;
    }

    /// <inheritdoc />
    public Guid GetWorkspaceId()
    {
        // Priority 0: ambient override set by background scheduler / anonymous webhook scope.
        if (_workspaceContext.OverrideWorkspaceId is { } overridden)
            return overridden;

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            // Priority 1: JWT "workspace_id" claim（登录时若默认工作空间尚未供应，可能写入
            // Guid.Empty —— 视为缺省并继续沿解析链回退，避免把空过滤器当成合法上下文）。
            var claim = httpContext.User.FindFirst("workspace_id")?.Value;
            if (claim is not null && Guid.TryParse(claim, out var workspaceFromClaim) &&
                workspaceFromClaim != Guid.Empty)
            {
                return workspaceFromClaim;
            }

            // Priority 2: "X-Workspace-Id" header
            if (httpContext.Request.Headers.TryGetValue("X-Workspace-Id", out var headerValues) &&
                headerValues.FirstOrDefault() is { } headerValue &&
                Guid.TryParse(headerValue, out var workspaceFromHeader) &&
                workspaceFromHeader != Guid.Empty)
            {
                return workspaceFromHeader;
            }
        }

        // Priority 3: tenant default workspace (preloaded by DatabaseInitializer / WorkspaceProvisioner).
        var defaultWorkspaceId = _directory.GetDefaultWorkspaceId(_tenantProvider.GetTenantId());
        if (defaultWorkspaceId is { } id)
        {
            return id;
        }

        // Priority 4: nothing resolvable — empty id; query filter matches nothing (fail closed).
        return Guid.Empty;
    }
}
