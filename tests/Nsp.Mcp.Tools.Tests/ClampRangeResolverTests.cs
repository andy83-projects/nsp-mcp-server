using Nsp.TuningTables;
using Xunit;

namespace Nsp.Mcp.Tools.Tests;

public class ClampRangeResolverTests
{
    [Fact]
    public async Task ResolveAsync_ReturnsNoRangeKnown_WhenTableHasNoLocalConfigEntry()
    {
        var resolver = new ClampRangeResolver(ClampRangeConfig.Empty());

        var resolution = await resolver.ResolveAsync("unregistered_table");

        Assert.Equal(ClampOutcome.NoRangeKnown, resolution.Outcome);
        Assert.Null(resolution.Range);
    }

    [Fact]
    public void Clamp_LeavesInRangeValueUnchanged()
    {
        var range = new ClampRange(0, 100, "test");

        var (applied, wasClamped) = ClampRangeResolver.Clamp(50, range);

        Assert.Equal(50, applied);
        Assert.False(wasClamped);
    }

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-10, 0)]
    public void Clamp_ClampsOutOfRangeValue(double requested, double expectedApplied)
    {
        var range = new ClampRange(0, 100, "test");

        var (applied, wasClamped) = ClampRangeResolver.Clamp(requested, range);

        Assert.Equal(expectedApplied, applied);
        Assert.True(wasClamped);
    }

    [Fact]
    public async Task ResolveAsync_LoadsFromLocalConfigFile()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, """
            {
              "tables": {
                "fuel_ve_table": { "min": 0, "max": 100, "source": "test-fixture" }
              }
            }
            """);

            var config = await ClampRangeConfig.LoadAsync(tempFile);
            var resolver = new ClampRangeResolver(config);

            var resolution = await resolver.ResolveAsync("fuel_ve_table");

            Assert.Equal(ClampOutcome.Resolved, resolution.Outcome);
            Assert.Equal(0, resolution.Range!.Min);
            Assert.Equal(100, resolution.Range.Max);
            Assert.Equal("test-fixture", resolution.Range.Provenance);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
