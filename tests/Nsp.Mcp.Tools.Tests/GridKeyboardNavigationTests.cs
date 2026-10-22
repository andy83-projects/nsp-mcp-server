using Nsp.Automation.Core;
using Xunit;

namespace Nsp.Mcp.Tools.Tests;

public class GridKeyboardNavigationTests
{
    [Theory]
    [InlineData(0, 0, 0, 14, "{RIGHT 14}")]
    [InlineData(0, 0, 0, 15, "{RIGHT 15}")]
    [InlineData(0, 15, 0, 0, "{LEFT 15}")]
    [InlineData(2, 4, 1, 14, "{UP 1}{RIGHT 10}")]
    [InlineData(1, 14, 2, 4, "{DOWN 1}{LEFT 10}")]
    [InlineData(1, 14, 1, 14, "")]
    public void MovesFromTheExposedAnchorToTheRequestedLogicalCell(int ar, int ac, int r, int c, string keys)
        => Assert.Equal(keys, GridKeyboardNavigation.Keys(ar, ac, r, c, 3, 16));

    [Fact] public void InvalidAddressesRefuseBeforeAnyKeyboardInput()
    {
        Assert.Throws<ArgumentException>(() => GridKeyboardNavigation.Keys(0, 0, 0, 16, 1, 16));
        Assert.Throws<ArgumentException>(() => GridKeyboardNavigation.Keys(-1, 0, 0, 14, 1, 16));
        Assert.Throws<ArgumentException>(() => GridKeyboardNavigation.Keys(0, 0, 0, 1, 0, 16));
        Assert.Throws<InvalidOperationException>(() => GridKeyboardNavigation.Keys(0, 0, 0, 999, 1, 1000));
    }
}
