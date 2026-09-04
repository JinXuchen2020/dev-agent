using System.Security.Claims;
using AgentPlatform.Domain.Repositories;

namespace AgentPlatform.Api.Middleware;

/// <summary>
/// F35（决策 D3=B）X-Workspace-Id 头授权守卫：非 Admin 调用者仅允许把请求定位到
/// 租户默认工作空间或自己已加入的工作空间（或与其 JWT workspace_id claim 一致的工作空间）；
/// Admin 允许本租户内任意工作空间（但同样拦截跨租户/已删除 id）。不可见时剥离该头，
/// 使 <c>WorkspaceProvider</c> 的解析链回退到 JWT workspace_id claim
/// （由切换端点重签发，恒合法）。没有此守卫，任何已认证用户都可以通过伪造该头
/// 读取同租户内任意工作空间的数据，绕过成员可见性约束。
/// </summary>
internal sealed class WorkspaceHeaderGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<WorkspaceHeaderGuardMiddleware> _logger;

    /// <summary>Initializes a new instance of the <see cref="WorkspaceHeaderGuardMiddleware"/> class.</summary>
    public WorkspaceHeaderGuardMiddleware(RequestDelegate next, ILogger<WorkspaceHeaderGuardMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// 校验已认证请求携带的 X-Workspace-Id 头；对不可见（非默认 / 非成员 / 非本租户）的头值
    /// 执行剥离后再进入后续管线。Admin 同样校验「id 属于本租户」（全租户可见但不跨租户），
    /// 拦截浏览器 localStorage 陈旧 id（换租户重登录后）与伪造头导致的空集查询。
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true &&
            context.Request.Headers.TryGetValue("X-Workspace-Id", out var values) &&
            Guid.TryParse(values.FirstOrDefault(), out var requestedWorkspaceId))
        {
            var isAdmin = string.Equals(
                context.User.FindFirstValue(ClaimTypes.Role), "Admin", StringComparison.OrdinalIgnoreCase);

            // 快路径：头与 JWT workspace_id claim 一致（SPA 稳态）——switch 端点已校验过可见性。
            var claimWorkspaceId = context.User.FindFirstValue("workspace_id");
            var matchesClaim = Guid.TryParse(claimWorkspaceId, out var claimWs) && claimWs == requestedWorkspaceId;

            if (!matchesClaim && !await IsAllowedAsync(context, requestedWorkspaceId, isAdmin))
            {
                context.Request.Headers.Remove("X-Workspace-Id");
                _logger.LogWarning(
                    "已剥离未授权的 X-Workspace-Id 头（workspace {WorkspaceId}，principal {UserId}）。",
                    requestedWorkspaceId,
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier));
            }
        }

        await _next(context);
    }

    private static async Task<bool> IsAllowedAsync(HttpContext context, Guid workspaceId, bool isAdmin)
    {
        // 使用嵌套 scope 查询成员可见性：先完成剥离，再让请求自身 scope 的 AppDbContext
        // 构造（控制器首次解析时）读到已净化的头，保证查询过滤器不会落在越权工作空间上。
        using var scope = context.RequestServices.CreateScope();
        var workspaceRepo = scope.ServiceProvider.GetRequiredService<IWorkspaceRepository>();

        // 租户过滤器由嵌套 scope 的 claim 租户上下文保证：跨租户 id 查询返回 null → 剥离（不泄漏存在性）。
        var workspace = await workspaceRepo.GetByIdAsync(workspaceId, context.RequestAborted);
        if (workspace is null)
            return false;

        // Admin 全租户可见：id 属于本租户即合法（跨租户/已删除 id 已在上面拦截）。
        if (isAdmin)
            return true;

        if (workspace.IsDefault)
            return true;

        var memberRepo = scope.ServiceProvider.GetRequiredService<IWorkspaceMemberRepository>();
        var userIdRaw = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdRaw, out var userId) &&
               await memberRepo.IsMemberAsync(workspaceId, userId, context.RequestAborted);
    }
}
