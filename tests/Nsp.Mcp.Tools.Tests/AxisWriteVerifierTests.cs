using Nsp.TuningTables;
using Xunit;

namespace Nsp.Mcp.Tools.Tests;

public sealed class AxisWriteVerifierTests
{
    [Fact] public void EnablesOnlyLiveValidatedAxisDirections()
    {
        var enabled = KnownTables.All.SelectMany(t => new[] {
            (id: t.TableId + ":row", enabled: t.SupportsRowBreakpointEdits),
            (id: t.TableId + ":column", enabled: t.SupportsColumnBreakpointEdits)
        }).Where(p => p.enabled).Select(p => p.id).Order().ToArray();
        Assert.Equal(new[] { "coolant_temperature_bias:row", "cranking_ignition:column", "injector_stage1_dead_time:column" }, enabled);
    }
    [Fact] public void PendingReadbackPreservesLegacyPointsAndExactDimensions()
    {
        AxisWriteVerifier.RequirePendingAxis([6, 9, 10], ["6.00", "9.00", "10.00", "(null)"]);
        Assert.Throws<InvalidOperationException>(() => AxisWriteVerifier.RequirePendingAxis([6, 9, 10], ["6.50", "9.00", "10.00", ""]));
        Assert.Throws<InvalidOperationException>(() => AxisWriteVerifier.RequirePendingAxis([6, 9, 10], ["6", "", "9", "10"]));
        Assert.Throws<InvalidOperationException>(() => AxisWriteVerifier.RequirePendingAxis([6, 9, 10], ["6", "9"]));
        Assert.Throws<InvalidOperationException>(() => AxisWriteVerifier.RequirePendingAxis([6, 9, 10], ["6", "9", "10", "12"]));
        Assert.Throws<InvalidOperationException>(() => AxisWriteVerifier.RequirePendingAxis([6, 9, 10], ["6", "NaN", "10"]));
    }
    [Fact] public void PredictsInterpolationInBothDisplayDirections()
    {
        var before = new TableSnapshotData([3000, 2000, 1000], [6, 8, 10], [[10, 20, 30], [20, 30, 40], [30, 40, 50]]);
        var column = AxisWriteVerifier.Interpolate(before, false, [6, 9, 10], 1);
        Assert.Equal(new double[] {10, 25, 30}, column.Values[0]);
        var row = AxisWriteVerifier.Interpolate(before, true, [3000, 2500, 1000], 1);
        Assert.Equal(new double[] {15, 25, 35}, row.Values[1]);
        Assert.Throws<ArgumentException>(() => AxisWriteVerifier.Interpolate(before, false, [5, 8, 10], 1));
        Assert.NotNull(AxisWriteVerifier.VerifyExpected(column, column with { Values = before.Values }));
    }
    [Fact] public void RejectsStaleAndCrossingBreakpoints()
    {
        Assert.Throws<ArgumentException>(() => AxisWriteVerifier.Replace([6, 8, 10], 1, 7, 9));
        Assert.Throws<ArgumentException>(() => AxisWriteVerifier.Replace([6, 8, 10], 1, 8, 10));
        Assert.Throws<ArgumentException>(() => AxisWriteVerifier.Replace([6, 8, 10], 1, 8, double.NaN));
    }
    [Fact] public void PreservesDisplayDirectionAndSourceArray()
    {
        double[] before = [3000, 2000, 1000];
        Assert.Equal(new double[] {3000, 2100, 1000}, AxisWriteVerifier.Replace(before, 1, 2000, 2100));
        Assert.Equal(2000, before[1]);
    }
    [Fact] public void DetectsInterpolationAndWrongAxis()
    {
        var before = new TableSnapshotData([1000], [6, 8], [[1, 2]]);
        Assert.Null(AxisWriteVerifier.Verify(before, before with {Columns = [6, 9]}, false, [6, 9]));
        Assert.NotNull(AxisWriteVerifier.Verify(before, before with {Columns = [6, 9], Values = [[1, 1.5]]}, false, [6, 9]));
        Assert.NotNull(AxisWriteVerifier.Verify(before, before with {Columns = [6, 9], Rows = [1100]}, false, [6, 9]));
    }
}
