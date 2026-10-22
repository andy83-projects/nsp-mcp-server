namespace Nsp.TuningTables;

public static class AxisWriteVerifier
{
    public static void RequirePendingAxis(double[] expected, string[] cells)
    {
        var count = 0;
        var ended = false;
        foreach (var text in cells)
        {
            if (string.IsNullOrWhiteSpace(text) || text == "(null)") { ended = true; continue; }
            if (ended || count >= expected.Length || !double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value != expected[count])
                throw new InvalidOperationException($"Pending axis differs at native index {count}: expected '{(count < expected.Length ? expected[count].ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "blank")}', observed '{text}'; cancelling.");
            count++;
        }
        if (count != expected.Length) throw new InvalidOperationException("Pending axis length differs; cancelling.");
    }

    public static TableSnapshotData Interpolate(TableSnapshotData before, bool rows, double[] requested, int decimalPlaces)
    {
        var axis = rows ? before.Rows : before.Columns;
        if (requested.Length != axis.Length || axis.Length < 2 || decimalPlaces < 0 || decimalPlaces > 8)
            throw new ArgumentException("Interpolation requires unchanged axis dimensions and a supported precision.");
        var order = Enumerable.Range(0, axis.Length).OrderBy(i => axis[i]).ToArray();
        double Sample(int row, int col, double point)
        {
            if (point < axis[order[0]] || point > axis[order[^1]])
                throw new ArgumentException("Axis edits outside the original range are not yet supported.");
            for (var j = 0; j < order.Length; j++)
            {
                var index = order[j];
                var first = rows ? before.Values[index][col] : before.Values[row][index];
                if (point == axis[index]) return first;
                if (j + 1 < order.Length && point < axis[order[j+1]])
                {
                    var next = order[j+1];
                    var second = rows ? before.Values[next][col] : before.Values[row][next];
                    return Math.Round(first + (second-first) * (point-axis[index]) / (axis[next]-axis[index]), decimalPlaces);
                }
            }
            throw new ArgumentException("Cannot interpolate this breakpoint.");
        }
        var values = before.Values.Select((row, r) => row.Select((_, c) => Sample(r, c, requested[rows ? r : c])).ToArray()).ToArray();
        return new(rows ? requested : before.Rows, rows ? before.Columns : requested, values);
    }

    public static string? VerifyExpected(TableSnapshotData expected, TableSnapshotData after)
    {
        if (!expected.Rows.SequenceEqual(after.Rows) || !expected.Columns.SequenceEqual(after.Columns)) return "Axis readback mismatch.";
        if (expected.Values.Length != after.Values.Length || expected.Values.Where((row, i) => !row.SequenceEqual(after.Values[i])).Any())
            return "Table differs from the predicted interpolation; no save performed.";
        return null;
    }
    public static double[] Replace(double[] before, int index, double expectedBefore, double value)
    {
        if (index < 0 || index >= before.Length || !double.IsFinite(value) || !double.IsFinite(expectedBefore))
            throw new ArgumentException("Invalid breakpoint address or non-finite value.");
        if (before[index] != expectedBefore) throw new ArgumentException("Breakpoint differs from expectedValue; no write performed.");
        var result = (double[])before.Clone(); result[index] = value;
        var descending = before.Length > 1 && before[0] > before[^1];
        for (var i = 0; i < result.Length; i++)
            if (!double.IsFinite(result[i]) || (i > 0 && (descending ? result[i] >= result[i-1] : result[i] <= result[i-1])))
                throw new ArgumentException("Breakpoints must remain finite and strictly ordered, without duplicates.");
        return result;
    }

    public static string? Verify(TableSnapshotData before, TableSnapshotData after, bool rows, double[] requested)
    {
        if (!(rows ? after.Rows : after.Columns).SequenceEqual(requested)) return "Requested breakpoints were not applied exactly.";
        if (!(rows ? before.Columns.SequenceEqual(after.Columns) : before.Rows.SequenceEqual(after.Rows))) return "The other axis changed.";
        if (before.Values.Length != after.Values.Length || before.Values.Where((row, i) => !row.SequenceEqual(after.Values[i])).Any())
            return "Table cell values changed while editing a breakpoint.";
        return null;
    }
}
