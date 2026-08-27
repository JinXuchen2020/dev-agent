namespace AgentPlatform.Application.Routing.Services;

/// <summary>
/// Thrown when the routing candidate list is empty — i.e. the tenant has no enabled BYO model
/// credentials AND the platform model catalog has no usable keys configured. This is a
/// configuration gap, not a model failure, so it carries an actionable message instead of the
/// generic "all models failed" wording (F31).
/// </summary>
public sealed class ModelNotConfiguredException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModelNotConfiguredException"/> class for the given tenant.
    /// </summary>
    /// <param name="tenantId">The tenant whose request could not be served.</param>
    public ModelNotConfiguredException(Guid tenantId)
        : base($"未配置任何可用模型：租户 {tenantId} 无启用的 BYO 模型凭据，平台模型目录（PlatformModels 表）也未配置可用 Key。" +
               "请在「我的凭据」添加模型凭据，或在管理后台为平台配置 LLM Key。")
    {
        TenantId = tenantId;
    }

    /// <summary>Gets the tenant that had no resolvable model configuration.</summary>
    public Guid TenantId { get; }
}