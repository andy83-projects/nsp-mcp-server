using Nsp.TuningTables;
using Xunit;

namespace Nsp.Mcp.Tools.Tests;

public sealed class TableWriteVerifierTests
{
    private static TableSnapshotData Snapshot(params double[] values) => new([], [8, 10, 12], [values]);

    [Fact]
    public void AcceptsOnlyTheIntendedCellChange() => Assert.Null(TableWriteVerifier.Verify(Snapshot(3.7, 3.4, 3.1), Snapshot(3.7, 3.4, 3.2), 0, 2, 3.2));

    [Fact]
    public void RejectsNeighbourChangeEvenWhenTargetMatches() => Assert.NotNull(TableWriteVerifier.Verify(Snapshot(3.7, 3.4, 3.1), Snapshot(3.7, 3.2, 3.2), 0, 2, 3.2));

    [Fact]
    public void RejectsSilentNoOp() => Assert.NotNull(TableWriteVerifier.Verify(Snapshot(3.7, 3.4, 3.1), Snapshot(3.7, 3.4, 3.1), 0, 2, 3.2));

    [Fact]
    public void RejectsChangedAxes()
    {
        var after = Snapshot(3.7, 3.4, 3.2) with { Columns = [8, 10, 14] };
        Assert.NotNull(TableWriteVerifier.Verify(Snapshot(3.7, 3.4, 3.1), after, 0, 2, 3.2));
    }

    [Fact]
    public void RejectsMissingCells() => Assert.NotNull(TableWriteVerifier.Verify(Snapshot(3.7, 3.4, 3.1), Snapshot(3.7, 3.4), 0, 2, 3.2));

    [Fact]
    public void RejectsNonFiniteReadback() => Assert.NotNull(TableWriteVerifier.Verify(Snapshot(3.7, 3.4, 3.1), Snapshot(3.7, 3.4, double.NaN), 0, 2, 3.2));

    [Fact]
    public void VerifiesAxisFreeScalarWithoutInventingBreakpoints()
    {
        var before = new TableSnapshotData([], [], [[22.86]]);
        var after = new TableSnapshotData([], [], [[22.87]]);
        Assert.Null(TableWriteVerifier.Verify(before, after, 0, 0, 22.87));
        Assert.NotNull(TableWriteVerifier.Verify(before, before, 0, 0, 22.87));
        Assert.NotNull(TableWriteVerifier.Verify(before, after with { Columns = [0] }, 0, 0, 22.87));
        Assert.NotNull(TableWriteVerifier.Verify(before, after with { Values = [[22.87, 1]] }, 0, 0, 22.87));
    }
}
