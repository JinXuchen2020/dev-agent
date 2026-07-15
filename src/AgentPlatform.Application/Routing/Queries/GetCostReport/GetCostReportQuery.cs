using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.ValueObjects;
using MediatR;

namespace AgentPlatform.Application.Routing.Queries.GetCostReport;

/// <summary>
/// Represents a query to retrieve a cost report summarizing today's spending.
/// </summary>
public record GetCostReportQuery : IRequest<CostReportResponse>;

/// <summary>
/// Represents the response of a cost report query, containing the total spent today and the currency.
/// </summary>
/// <param name="TodaySpent">The total amount spent today.</param>
/// <param name="Currency">The currency code of the spent amount (e.g., "USD").</param>
public record CostReportResponse(decimal TodaySpent, string Currency);

internal sealed class GetCostReportQueryHandler(ICostController costController)
    : IRequestHandler<GetCostReportQuery, CostReportResponse>
{
    public Task<CostReportResponse> Handle(GetCostReportQuery request, CancellationToken cancellationToken)
    {
        var spent = costController.GetTodaySpent();
        return Task.FromResult(new CostReportResponse(spent.Amount, spent.Currency));
    }
}
