using Nsp.Automation.Core;
using Xunit;

namespace Nsp.Mcp.Tools.Tests;

public class NativeIoCaptionsTests
{
    [Theory]
    [InlineData("SPI4", "[GY/Y], C8")]
    [InlineData("HBO3", "[BR/G], A21")]
    [InlineData("25A-HCO2", "[R/Y], E2")]
    [InlineData("A/C Request Switch I (GenCond1 Out)", null)]
    public void ReadsObservedPhysicalAndConditionalAssignments(string port, string? wire)
    {
        var result = NativeIoCaptions.Parse(new[] { "A/C Request Switch Input", port, wire ?? "", "----" });
        Assert.Equal(port, result!.Port);
        Assert.Equal(wire, result.Wire);
    }
    [Fact] public void UnassignedRequiresExplicitNativeEvidence()
    {
        Assert.Equal("Not Assigned", NativeIoCaptions.Parse(["Generic Condition 1 Input", "Not Assigned"])!.Port);
        Assert.Null(NativeIoCaptions.Parse(["Generic Condition 1 Input", ""]));
        Assert.Null(NativeIoCaptions.Parse(["SPI4 Sensor", "[GY/Y], C8"]));
    }
    [Fact] public void RefusesMultipleResourcesRatherThanPickingFirstConnection()
        => Assert.Throws<InvalidOperationException>(() => NativeIoCaptions.Parse(["SPI4", "HBO3", "[GY/Y], C8"]));
    [Fact] public void RefusesMultiplePinCaptionsInOneSupposedRow()
        => Assert.Throws<InvalidOperationException>(() => NativeIoCaptions.Parse(["SPI4", "[GY/Y], C8", "[BR/G], A21"]));
}
