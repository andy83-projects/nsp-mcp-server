namespace Nsp.Automation.Core;

/// <summary>One budget for setup, every search fallback and both traversal directions.</summary>
public sealed class NavigationBudget(int maxSteps, Func<TimeSpan> elapsed)
{
    public int Steps { get; private set; }
    public bool CanStep => Steps < Math.Clamp(maxSteps, 1, 120) && elapsed() < TimeSpan.FromSeconds(50);
    public void TakeStep()
    {
        if (!CanStep) throw new InvalidOperationException("Native navigation reached its overall time/step budget; no further arrow sent.");
        Steps++;
    }
}
