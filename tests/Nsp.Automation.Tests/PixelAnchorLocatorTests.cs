using System.Drawing;
using Nsp.Automation.Fallback;
using Xunit;

namespace Nsp.Automation.Tests;

/// <summary>Pure math, no real window needed — safe to run anywhere net10.0-windows builds,
/// including the VM's own CI if one ever exists.</summary>
public class PixelAnchorLocatorTests
{
    [Fact]
    public void LocateCell_InterpolatesBetweenTwoAnchors()
    {
        var locator = PixelAnchorLocator.FromTwoAnchors(
            anchorA: (Row: 0, Col: 0, Pixel: new Point(100, 200)),
            anchorB: (Row: 4, Col: 8, Pixel: new Point(180, 280)));

        var midpoint = locator.LocateCell(2, 4);

        Assert.Equal(140, midpoint.X);
        Assert.Equal(240, midpoint.Y);
    }
}

/// <summary>
/// Placeholder for real UIA-driven tests against the actual NSP app. These require the
/// Windows VM with NSP open and are intentionally skipped elsewhere — fill these in once
/// the Phase 0 probe has run. See ../../docs/RUNBOOK.md and ../../docs/PHASE0_FINDINGS.md.
/// </summary>
public class NspWindowLocatorLiveTests
{
    [Fact(Skip = "Requires the Windows VM with NSP open — see docs/RUNBOOK.md")]
    public void Locate_FindsTheExpectedWindow()
    {
    }
}
