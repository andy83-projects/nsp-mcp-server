using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Nsp.Audit;
using Nsp.Automation.Core;
using Nsp.TuningTables;

namespace Nsp.Mcp.Tools;

/// <summary>
/// Tuning-table cell read/write tools. Writes are guarded per the explicit safety decision:
/// clamp to a known min/max, require confirm=true, audit-log every write (and every
/// dry-run) with before/after values. If no clamp range is known from either the live UI
/// or local config, the write is refused outright rather than applied unclamped.
/// </summary>
[McpServerToolType]
public sealed class TableTools(NspAppController controller, ClampRangeResolver clampResolver, AuditLogger auditLogger)
{
    [McpServerTool(Name = "nsp_list_tables", ReadOnly = true)]
    [Description("Lists all registered tuning tables and their capabilities. Cell/axis tools accept a listed tableId, exact helpIdentity or unique displayName; unregistered identities return a structured refusal. Units refer to the current NSP display.")]
    public object ListTables()
    {
        var tables = controller.Tables.ListTables().Select(t => new
        {
            t.TableId,
            t.DisplayName,
            t.RowAxisLabel,
            t.ColumnAxisLabel,
            t.CellUnit,
            t.CellDecimalPlaces,
            t.HasNoAxes,
            t.HelpIdentity,
            t.NavigationSearchText,
            t.SupportsSliceAxis,
            t.SupportsRowBreakpointEdits,
            t.SupportsColumnBreakpointEdits,
            driverKind = t.DriverKind.ToString(),
        });

        return new { tables };
    }

    [McpServerTool(Name = "nsp_get_table_axes", ReadOnly = true)]
    [Description("Reads the actual axis breakpoint values (e.g. RPM columns, load/MAP rows) live from NSP for the given table — needed because cell addressing depends on real breakpoints, not assumed ones.")]
    public async Task<object> GetTableAxes(string tableId, CancellationToken cancellationToken)
    {
        var table = controller.Tables.TryGet(tableId);
        if (table is null) return UnknownTable(tableId);
        tableId = table.TableId;
        try
        {
            var driver = controller.ResolveCellDriver(tableId);
            var (rows, cols) = await driver.GetAxesAsync(table, cancellationToken);
            return new { table.TableId, rowBreakpoints = rows, columnBreakpoints = cols };
        }
        catch (Exception ex) when (AxesReadFailed(ex)) { return AxesUnavailable(tableId, ex); }
    }

    [McpServerTool(Name = "nsp_read_cell", ReadOnly = true)]
    [Description("Reads a tuning-table cell's current value by (row, col).")]
    public async Task<object> ReadCell(string tableId, int row, int col, CancellationToken cancellationToken)
    {
        var table = controller.Tables.TryGet(tableId);
        if (table is null) return UnknownTable(tableId);
        tableId = table.TableId;
        var driver = controller.ResolveCellDriver(tableId);
        var result = await driver.ReadCellAsync(table, row, col, cancellationToken);
        return ToolResultJson.From(result);
    }

    [McpServerTool(Name = "nsp_read_table_snapshot", ReadOnly = true)]
    [Description("Bulk-reads the full grid for a table, for diffing/audit/baseline capture before a tuning session.")]
    public async Task<object> ReadTableSnapshot(string tableId, CancellationToken cancellationToken)
    {
        var table = controller.Tables.TryGet(tableId);
        if (table is null) return UnknownTable(tableId);
        tableId = table.TableId;
        var driver = controller.ResolveCellDriver(tableId);
        var result = await driver.ReadTableSnapshotAsync(table, cancellationToken);
        return ToolResultJson.From(result);
    }

    [McpServerTool(Name = "nsp_write_cell", Destructive = true)]
    [Description("Writes a tuning-table cell. Resolves a clamp range from the live UI first, then local clamp-ranges.json config; if neither has a range, the write is REFUSED. If confirm=false, returns a dry-run preview (resolved range + current value) without touching NSP. If confirm=true and the value is out of range, it is clamped (not rejected) and wasClamped=true is reported alongside the original and applied values.")]
    public async Task<object> WriteCell(
        string tableId,
        int row,
        int col,
        double value,
        [Description("Must be true to actually write. False returns a dry-run preview only.")] bool confirm,
        CancellationToken cancellationToken)
    {
        var table = controller.Tables.TryGet(tableId);
        if (table is null) return UnknownTable(tableId);
        tableId = table.TableId;
        if (!double.IsFinite(value)) return ToolResultJson.From(AutomationResult.Failed("none", "Value must be finite."));
        var clampResolution = await clampResolver.ResolveAsync(tableId, row, col, cancellationToken);

        if (clampResolution.Outcome == ClampOutcome.NoRangeKnown)
        {
            var refusal = AutomationResult.Failed(
                "none",
                $"No clamp range known for table '{tableId}' from either the live UI or clamp-ranges.json — refusing to write unclamped. " +
                "Add an entry to clamp-ranges.json (see clamp-ranges.example.json) or confirm the UI exposes a min/max.");

            await LogWriteAsync(tableId, row, col, value, dryRun: !confirm, clampResolution, appliedValue: null, result: refusal, cancellationToken);
            return ToolResultJson.From(refusal);
        }

        var range = clampResolution.Range!;
        var (applied, wasClamped) = ClampRangeResolver.Clamp(value, range);

        if (!confirm)
        {
            var before = await controller.ResolveCellDriver(tableId).ReadCellAsync(table, row, col, cancellationToken);
            if (!before.Success)
            {
                await LogWriteAsync(tableId, row, col, value, true, clampResolution, null, before, cancellationToken);
                return ToolResultJson.From(before);
            }
            var preview = AutomationResult.Succeeded("none", before.VerifiedValue);
            await LogWriteAsync(tableId, row, col, value, dryRun: true, clampResolution, applied, preview, cancellationToken);

            return new
            {
                dryRun = true,
                currentValue = before.VerifiedValue,
                requestedValue = value,
                wouldApplyValue = applied,
                wasClamped,
                clampRange = new { range.Min, range.Max, range.Provenance },
            };
        }

        var driver = controller.ResolveCellDriver(tableId);
        var writeResult = await driver.WriteCellAsync(table, row, col, applied, cancellationToken);
        await LogWriteAsync(tableId, row, col, value, dryRun: false, clampResolution, applied, writeResult, cancellationToken);

        return new
        {
            writeResult.Success,
            driverUsed = writeResult.DriverUsed,
            requestedValue = value,
            appliedValue = applied,
            wasClamped,
            verifiedAfterValue = writeResult.VerifiedValue,
            clampRange = new { range.Min, range.Max, range.Provenance },
            validationMarkers = writeResult.ValidationMarkers,
            errorMessage = writeResult.ErrorMessage,
        };
    }

    [McpServerTool(Name = "nsp_write_table_range", Destructive = true)]
    [Description("Batched convenience wrapper over nsp_write_cell for applying several cells at once — same per-cell clamp+confirm+audit+verify guarantees. Returns per-cell results so partial failures are visible rather than all-or-nothing silent.")]
    public async Task<object> WriteTableRange(
        string tableId,
        [Description("Cells to write, each as {row, col, value}.")] IReadOnlyList<CellWrite> cells,
        [Description("Must be true to actually write any cell.")] bool confirm,
        CancellationToken cancellationToken)
    {
        var table = controller.Tables.TryGet(tableId);
        if (table is null) return UnknownTable(tableId);
        tableId = table.TableId;
        if (cells.Count > 1000 || cells.Any(c => !double.IsFinite(c.Value)) ||
            cells.Select(c => (c.Row, c.Col)).Distinct().Count() != cells.Count)
            return new { success = false, errorMessage = "A range must contain at most 1000 unique, finite cell requests." };
        (double[] RowBreakpoints, double[] ColumnBreakpoints) axes;
        try { axes = await controller.ResolveCellDriver(tableId).GetAxesAsync(table, cancellationToken); }
        catch (Exception ex) when (AxesReadFailed(ex)) { return AxesUnavailable(tableId, ex); }
        var rowCount = axes.RowBreakpoints.Length == 0 ? 1 : axes.RowBreakpoints.Length;
        var columnCount = table.HasNoAxes ? 1 : axes.ColumnBreakpoints.Length;
        if (cells.Any(c => c.Row < 0 || c.Row >= rowCount || c.Col < 0 || c.Col >= columnCount))
            return new { success = false, errorMessage = "A range includes a cell outside the live axes; no values written." };
        var results = new List<object>(cells.Count);

        foreach (var cell in cells)
        {
            var result = await WriteCell(tableId, cell.Row, cell.Col, cell.Value, confirm, cancellationToken);
            results.Add(result);
            var json = JsonSerializer.SerializeToElement(result);
            if ((json.TryGetProperty("Success", out var status) || json.TryGetProperty("success", out status)) && status.ValueKind == JsonValueKind.False)
                return new { tableId, results, stoppedOnFailure = true };
        }

        return new { tableId, results };
    }

    [McpServerTool(Name = "nsp_get_audit_log", ReadOnly = true)]
    [Description("Reads recent entries from the append-only audit log, newest first.")]
    public async Task<object> GetAuditLog(
        [Description("ISO-8601 timestamp; only entries at or after this time are returned. Omit for no lower bound.")] string? since = null,
        string? toolNameFilter = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset? sinceParsed = since is null ? null : DateTimeOffset.Parse(since);
        var entries = await auditLogger.QueryAsync(sinceParsed, toolNameFilter, limit <= 0 ? 100 : limit, cancellationToken);
        return new { entries };
    }

    private object UnknownTable(string tableId) => new
    {
        success = false, errorCode = "unknown_table", tableId, actionAttempted = false,
        errorMessage = "Table is unregistered or its exact identity is ambiguous; no UI action or value write attempted.",
        nextAction = "Call nsp_list_tables. Use a listed tableId, exact helpIdentity or unique displayName. Discovery alone does not register a table.",
        availableTables = controller.Tables.ListTables().Select(t => new { t.TableId, t.HelpIdentity }).ToArray(),
    };

    private static bool AxesReadFailed(Exception ex) => ex is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException;

    private static object AxesUnavailable(string tableId, Exception ex) => new
    {
        success = false, errorCode = "axes_unavailable", tableId, axesAvailable = false, valuesWritten = false,
        errorMessage = ex.Message,
        nextAction = "Inspect the exact table and open dialogs in NSP. Its axis editor may be unavailable while a function is disabled. No function was enabled and no values were written; do not infer axis values or replay an edit.",
    };

    private async Task LogWriteAsync(
        string tableId,
        int row,
        int col,
        double requestedValue,
        bool dryRun,
        ClampResolution clampResolution,
        double? appliedValue,
        AutomationResult result,
        CancellationToken cancellationToken)
    {
        await auditLogger.AppendAsync(new AuditLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            ToolName = "nsp_write_cell",
            InputsJson = JsonSerializer.Serialize(new { tableId, row, col, requestedValue }),
            DryRun = dryRun,
            Success = result.Success,
            DriverUsed = result.DriverUsed,
            ClampProvenance = clampResolution.Outcome == ClampOutcome.Resolved ? clampResolution.Range!.Provenance : "rejected-no-range",
            ClampMin = clampResolution.Range?.Min,
            ClampMax = clampResolution.Range?.Max,
            RequestedValueJson = JsonSerializer.Serialize(requestedValue),
            AppliedValueJson = appliedValue is null ? null : JsonSerializer.Serialize(appliedValue),
            BeforeValueJson = result.BeforeValue is null ? null : JsonSerializer.Serialize(result.BeforeValue),
            AfterValueJson = result.VerifiedValue is null ? null : JsonSerializer.Serialize(result.VerifiedValue),
            WasClamped = appliedValue is not null && appliedValue.Value != requestedValue,
            ValidationMarkersObserved = result.ValidationMarkers,
            ErrorMessage = result.ErrorMessage,
        }, CancellationToken.None); // A disconnected client must not erase an already-applied write's audit record.
    }
}

public sealed record CellWrite(int Row, int Col, double Value);
