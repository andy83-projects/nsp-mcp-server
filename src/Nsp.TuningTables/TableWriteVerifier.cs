namespace Nsp.TuningTables;

public sealed record TableSnapshotData(double[] Rows, double[] Columns, double[][] Values);

public static class TableWriteVerifier
{
    public static string? Verify(TableSnapshotData before, TableSnapshotData after, int row, int col, double expectedValue)
    {
        if (!double.IsFinite(expectedValue)) return "Requested value is not finite.";
        if (!before.Rows.SequenceEqual(after.Rows) || !before.Columns.SequenceEqual(after.Columns)) return "Table axes changed during the write.";
        if (before.Values.Length != after.Values.Length) return "Table row count changed during the write.";
        if (row < 0 || row >= before.Values.Length || col < 0 || col >= before.Values[row].Length) return "Target cell is outside the snapshot.";
        for (var r = 0; r < before.Values.Length; r++)
        {
            if (before.Values[r].Length != after.Values[r].Length) return "Table column count changed during the write.";
            for (var c = 0; c < before.Values[r].Length; c++)
            {
                var expected = r == row && c == col ? expectedValue : before.Values[r][c];
                if (!double.IsFinite(expected) || !double.IsFinite(after.Values[r][c]) || Math.Abs(after.Values[r][c] - expected) > 1e-8)
                    return $"Table verification mismatch at ({r},{c}): expected {expected}, read {after.Values[r][c]}.";
            }
        }
        return null;
    }
}
