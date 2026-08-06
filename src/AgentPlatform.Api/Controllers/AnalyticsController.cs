using AgentPlatform.Application.Analytics.Queries.GetDashboardSummary;
using AgentPlatform.Application.Analytics.Queries.GetWorkflowUsage;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentPlatform.Api.Controllers;

/// <summary>
/// Tenant-scoped analytics for the operations dashboard. All data is filtered by the
/// current request's tenant (via <see cref="AgentPlatform.Application.Abstractions.ITenantProvider"/>).
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/analytics")]
public sealed class AnalyticsController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>Maximum allowed date-range span for the summary query (≈ 1 year).</summary>
    private const int MaxRangeDays = 366;


    /// <summary>
    /// Initializes a new instance of the <see cref="AnalyticsController"/> class.
    /// </summary>
    /// <param name="mediator">The MediatR mediator used to dispatch the dashboard summary query.</param>
    public AnalyticsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns a consolidated dashboard summary (KPIs + time-series chart data) for the
    /// current tenant within an optional date range. Any authenticated tenant user may read it.
    /// </summary>
    /// <param name="from">Optional inclusive start date (UTC, ISO-8601). Defaults to 14 days before <paramref name="to"/>.</param>
    /// <param name="to">Optional inclusive end date (UTC, ISO-8601). Defaults to now (UTC).</param>
    /// <param name="ct">A cancellation token.</param>
    [HttpGet("summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct = default)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
            return BadRequest("from must be earlier than or equal to to.");

        if (from.HasValue && to.HasValue && (to.Value - from.Value).TotalDays > MaxRangeDays)
            return BadRequest($"date range cannot exceed {MaxRangeDays} days.");

        var result = await _mediator.Send(new GetDashboardSummaryQuery(from, to), ct);
        return Ok(result);
    }

    /// <summary>
    /// Returns per-workflow usage metrics (executions, success rate, average latency, tokens)
    /// for the current tenant within an optional date range. Any authenticated tenant user
    /// may read it.
    /// </summary>
    /// <param name="from">Optional inclusive start date (UTC, ISO-8601). Defaults to 14 days before <paramref name="to"/>.</param>
    /// <param name="to">Optional inclusive end date (UTC, ISO-8601). Defaults to now (UTC).</param>
    /// <param name="ct">A cancellation token.</param>
    [HttpGet("workflows")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetWorkflowUsage(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct = default)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
            return BadRequest("from must be earlier than or equal to to.");

        if (from.HasValue && to.HasValue && (to.Value - from.Value).TotalDays > MaxRangeDays)
            return BadRequest($"date range cannot exceed {MaxRangeDays} days.");

        var result = await _mediator.Send(new GetWorkflowUsageQuery(from, to), ct);
        return Ok(result);
    }
}
