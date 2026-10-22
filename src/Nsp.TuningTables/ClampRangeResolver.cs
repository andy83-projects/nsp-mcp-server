namespace Nsp.TuningTables;

public sealed record ClampRange(double Min, double Max, string Provenance);

/// <summary>
/// Implemented by the automation layer (Nsp.Automation) so this cross-platform project
/// never takes a dependency on Windows-only UIA code. Returns null when the live UI does
/// not expose a declared min/max for the given table/cell.
/// </summary>
public interface ILiveClampRangeSource
{
    Task<ClampRange?> TryGetLiveRangeAsync(string tableId, int? row, int? col, CancellationToken cancellationToken = default);
}

public enum ClampOutcome
{
    /// <summary>A range was found (live UI or local config) and can be used to clamp a write.</summary>
    Resolved,

    /// <summary>No range is known from any source — the write must be refused, not performed unclamped.</summary>
    NoRangeKnown,
}

public sealed record ClampResolution(ClampOutcome Outcome, ClampRange? Range);

/// <summary>
/// Sources a per-table (or per-cell) clamp range: live UI first, then local config,
/// else refuses — matching the explicit guardrails-on decision that nothing is ever
/// written unclamped. Every resolution records its provenance for the audit log.
/// </summary>
public sealed class ClampRangeResolver(ClampRangeConfig localConfig, ILiveClampRangeSource? liveSource = null)
{
    public async Task<ClampResolution> ResolveAsync(
        string tableId,
        int? row = null,
        int? col = null,
        CancellationToken cancellationToken = default)
    {
        if (liveSource is not null)
        {
            var live = await liveSource.TryGetLiveRangeAsync(tableId, row, col, cancellationToken);
            if (live is not null)
            {
                return new ClampResolution(ClampOutcome.Resolved, live);
            }
        }

        if (localConfig.TryGetRange(tableId, out var min, out var max, out var source))
        {
            return new ClampResolution(ClampOutcome.Resolved, new ClampRange(min, max, source));
        }

        return new ClampResolution(ClampOutcome.NoRangeKnown, null);
    }

    /// <summary>Clamps a requested value into range, reporting whether clamping actually changed it.</summary>
    public static (double Applied, bool WasClamped) Clamp(double requested, ClampRange range)
    {
        var applied = Math.Clamp(requested, range.Min, range.Max);
        return (applied, applied != requested);
    }
}
