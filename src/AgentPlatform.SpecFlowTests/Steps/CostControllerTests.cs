using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Queries.GetCostReport;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AgentPlatform.SpecFlowTests.Steps;

public class CostControllerTests
{
    private static CostController CreateCostController(decimal budget = 50m)
    {
        var pricingOptions = Substitute.For<IOptions<PricingSettings>>();
        pricingOptions.Value.Returns(new PricingSettings());
        var routerOptions = Substitute.For<IOptions<RouterSettings>>();
        routerOptions.Value.Returns(new RouterSettings { DailyBudget = budget });
        return new CostController(pricingOptions, routerOptions, Substitute.For<ILogger<CostController>>());
    }

    [Fact]
    public void TryReserve_ReturnsFalse_WhenBudgetExceeded()
    {
        // Arrange
        var controller = CreateCostController(budget: 0m);
        var candidate = new ModelCandidate("gpt-4o", "openai", 100);

        // Act
        var result = controller.TryReserve(candidate, 1000);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void TryReserve_ReturnsTrue_WhenWithinBudget()
    {
        var controller = CreateCostController(budget: 50m);
        var candidate = new ModelCandidate("gpt-4o", "openai", 100);

        var result = controller.TryReserve(candidate, 1000);

        Assert.True(result);
    }

    [Fact]
    public void SettleUsage_WithNullTokenUsage_KeepsReservation()
    {
        var controller = CreateCostController(budget: 50m);
        var candidate = new ModelCandidate("gpt-4o", "openai", 100);

        controller.TryReserve(candidate, 1000);
        var spentBefore = controller.GetTodaySpent();

        controller.SettleUsage(candidate, null, 1000);

        var spentAfter = controller.GetTodaySpent();
        Assert.Equal(spentBefore.Amount, spentAfter.Amount);
    }

    [Fact]
    public void SettleUsage_WithActualUsage_AdjustsToActual()
    {
        var controller = CreateCostController(budget: 50m);
        var candidate = new ModelCandidate("gpt-4o", "openai", 100);

        controller.TryReserve(candidate, 1000); // reserves 1000 tokens worth
        controller.SettleUsage(candidate, new TokenUsage(100, 200), 1000); // actual 300 tokens

        var spent = controller.GetTodaySpent();
        // openai cost: 2.50/million, so 300 tokens = 300 * 2.50/1000000 = 0.00075
        Assert.True(spent.Amount > 0);
        Assert.True(spent.Amount < 0.01m); // much less than 1000 tokens worth
    }

    [Fact]
    public void ReleaseReservation_ReducesSpent()
    {
        var controller = CreateCostController(budget: 50m);
        var candidate = new ModelCandidate("gpt-4o", "openai", 100);

        controller.TryReserve(candidate, 1000);
        var spentAfterReserve = controller.GetTodaySpent();
        Assert.True(spentAfterReserve.Amount > 0);

        controller.ReleaseReservation(candidate, 1000);
        var spentAfterRelease = controller.GetTodaySpent();
        Assert.Equal(0m, spentAfterRelease.Amount);
    }

    [Fact]
    public void ReleaseReservation_ClampsToZero_WhenNegative()
    {
        var controller = CreateCostController(budget: 50m);
        var candidate = new ModelCandidate("gpt-4o", "openai", 100);

        // Release without prior reserve - should clamp to zero, not go negative
        controller.ReleaseReservation(candidate, 1000);
        var spent = controller.GetTodaySpent();
        Assert.Equal(0m, spent.Amount);
    }

    [Fact]
    public async Task GetCostReportQuery_ReturnsCorrectData()
    {
        var controller = CreateCostController(budget: 50m);
        var candidate = new ModelCandidate("gpt-4o", "openai", 100);
        controller.TryReserve(candidate, 1000);

        var handler = new GetCostReportQueryHandler(controller);
        var result = await handler.Handle(new GetCostReportQuery(), CancellationToken.None);

        Assert.True(result.TodaySpent > 0);
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public void TryReserve_ThrowsOnNullCandidate()
    {
        var controller = CreateCostController();
        Assert.Throws<ArgumentNullException>(() => controller.TryReserve(null!, 1000));
    }

    [Fact]
    public void SettleUsage_ThrowsOnNullCandidate()
    {
        var controller = CreateCostController();
        Assert.Throws<ArgumentNullException>(() => controller.SettleUsage(null!, new TokenUsage(1, 1), 1000));
    }

    [Fact]
    public void ReleaseReservation_ThrowsOnNullCandidate()
    {
        var controller = CreateCostController();
        Assert.Throws<ArgumentNullException>(() => controller.ReleaseReservation(null!, 1000));
    }
}
