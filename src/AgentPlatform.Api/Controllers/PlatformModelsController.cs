using AgentPlatform.Api.Models;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentPlatform.Api.Controllers;

/// <summary>
/// 平台模型目录端点（运营方配置的 platform-* 模型 + 当前租户自配 BYO 模型并列）。
/// 仅暴露模型标识，不含任何密钥；所有认证用户可访问。
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/models")]
public sealed class PlatformModelsController : ControllerBase
{
    private readonly IPlatformModelProvider _platformModels;
    private readonly ITenantModelClientResolver _tenantModelResolver;
    private readonly ITenantProvider _tenant;

    /// <summary>初始化 <see cref="PlatformModelsController"/> 的新实例。</summary>
    public PlatformModelsController(
        IPlatformModelProvider platformModels,
        ITenantModelClientResolver tenantModelResolver,
        ITenantProvider tenant)
    {
        _platformModels = platformModels;
        _tenantModelResolver = tenantModelResolver;
        _tenant = tenant;
    }

    /// <summary>返回当前租户可用的模型列表（平台内置 ∪ 租户自配）。</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var list = _platformModels.GetCandidates()
            .Select(c => new PlatformModelDto(c.ModelId, c.Provider, c.ModelId, false))
            .ToList();

        var tenantResolution = await _tenantModelResolver.ResolveAsync(_tenant.GetTenantId(), ct);
        if (tenantResolution is not null)
        {
            foreach (var c in tenantResolution.Candidates)
            {
                list.Add(new PlatformModelDto(c.ModelId, c.Provider,
                    $"我的 · {c.Provider} ({c.ModelId})", true));
            }
        }

        return Ok(list);
    }
}
