using Nsp.Automation.Core;
using Xunit;

namespace Nsp.Mcp.Tools.Tests;

public class NavigationBudgetTests
{
    [Fact] public void ColdSetupConsumesTheSameBudgetAsTraversal()
    {
        var elapsed = TimeSpan.Zero;
        var budget = new NavigationBudget(60, () => elapsed);
        elapsed = TimeSpan.FromSeconds(50); // Region/search resolution was slow.
        Assert.False(budget.CanStep);
        Assert.Throws<InvalidOperationException>(budget.TakeStep);
        Assert.Equal(0, budget.Steps);
    }
    [Fact] public void FallbackQueriesAndReverseSweepCannotResetTheStepLimit()
    {
        var budget = new NavigationBudget(3, () => TimeSpan.FromSeconds(1));
        budget.TakeStep(); // First query, forward.
        budget.TakeStep(); // Same query, reverse.
        budget.TakeStep(); // Fallback query.
        Assert.False(budget.CanStep);
        Assert.Throws<InvalidOperationException>(budget.TakeStep);
        Assert.Equal(3, budget.Steps);
    }
    [Fact] public void ExpiredBudgetStopsAnOtherwiseAvailableStep()
    {
        var elapsed = TimeSpan.FromSeconds(49);
        var budget = new NavigationBudget(60, () => elapsed);
        budget.TakeStep();
        elapsed = TimeSpan.FromSeconds(51);
        Assert.False(budget.CanStep);
    }
}
