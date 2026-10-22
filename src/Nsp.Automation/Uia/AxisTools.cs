using System.ComponentModel;
using System.Text.Json;
using System.Windows.Automation;
using ModelContextProtocol.Server;
using Nsp.Audit;
using Nsp.TuningTables;

namespace Nsp.Automation.Uia;

[McpServerToolType]
public sealed class AxisTools(NspTableNavigator navigator, TuningGridUiaDriver driver, TableCatalog catalog, AuditLogger audit)
{
    [McpServerTool(Name = "nsp_write_axis_breakpoint", Destructive = true)]
    [Description("Replace one existing breakpoint without resizing, using native delete/insert within one uncommitted axis dialog; no clipboard or image dependency. Only catalog axes explicitly validated for edit/interpolation/restore are enabled; see nsp_list_tables. Index follows nsp_get_table_axes display order. Requires expectedValue, strictly ordered finite breakpoints, disconnected NSP and confirm=true. Verifies pending axis, both committed axes and every table cell; no save/upload. Set allowInterpolation=true for predicted cell changes. Preview does not guarantee native range/precision acceptance. Third-axis slices remain unsupported.")]
    public async Task<object> WriteAxisBreakpoint(string tableId, string axis, int index, double expectedValue, double value, bool confirm, bool allowInterpolation = false)
    {
        TableSnapshotData? before = null, after = null, predicted = null;
        double[]? requested = null;
        string[]? dialogAxisBefore = null, dialogAxisPending = null;
        var commitAttempted = false;
        var axisCommitted = false;
        string? error = null;
        try
        {
            lock (MenuNavigator.InteractionLock)
            {
                if (axis is not ("row" or "column")) throw new ArgumentException("axis must be row or column.");
                var table = catalog.TryGet(tableId) ?? throw new ArgumentException("Unknown table ID.");
                if (!(axis == "row" ? table.SupportsRowBreakpointEdits : table.SupportsColumnBreakpointEdits))
                    throw new ArgumentException("Breakpoint editing is not enabled for this table/axis: native interpolation and display-unit behavior have not passed validation. No edit performed.");
                var main = navigator.MainWindow;
                var grid = navigator.ResolveGrid(table);
                before = driver.Snapshot(table, grid);
                var rows = axis == "row";
                requested = AxisWriteVerifier.Replace(rows ? before.Rows : before.Columns, index, expectedValue, value);
                predicted = AxisWriteVerifier.Interpolate(before, rows, requested, table.CellDecimalPlaces);
                var changesCells = before.Values.Where((row, i) => !row.SequenceEqual(predicted.Values[i])).Any();
                if (confirm && changesCells && !allowInterpolation)
                    throw new ArgumentException("NSP will interpolate cell values; inspect the dry run and explicitly set allowInterpolation=true.");
                if (confirm && value != expectedValue)
                {
                    Nsp.Automation.Core.OperationContext.Checkpoint("Ready to edit pending axis");
                    using var deferredCancellation = Nsp.Automation.Core.OperationContext.DeferCancellation();
                    LiveUi.RequireOffline(main);
                    // Preserve the verified cell focus established by ResolveGrid.
                    var dialog = NativeDialogs.OpenAxisDialog(main, grid);
                    var committed = false;
                    try
                    {
                        if (dialog.Current.Name != table.DisplayName + " : Table Axis Setup")
                            throw new InvalidOperationException("Axis dialog identity mismatch.");
                        var axisGrid = LiveUi.Scope(dialog, rows ? "VertDataGridView" : "HorizDataGridView");
                        var nativeIndex = rows && table.ReadDescendingRowsFromAxisSetup ? requested.Length - 1 - index : index;
                        var original = TuningGridUiaDriver.GridText(axisGrid)[0];
                        dialogAxisBefore = original;
                        var behavior = LiveUi.Unique(dialog, rows ? "VertTypeComboBox" : "HorizTypeComboBox", null, "ComboBox");
                        if (FormFieldDriver.Read(behavior) != "Continuous") throw new InvalidOperationException("Only continuous axes are supported for interpolation.");
                        var nativeBefore = rows && table.ReadDescendingRowsFromAxisSetup ? before.Rows.Reverse().ToArray() : rows ? before.Rows : before.Columns;
                        var nativeAxis = rows && table.ReadDescendingRowsFromAxisSetup ? requested.Reverse().ToArray() : requested;
                        // A full-axis paste re-clamps unchanged legacy breakpoints.
                        // Delete/insert only the requested point within this uncommitted
                        // dialog; verify the deletion and complete replacement before OK.
                        NativeAxisEditor.Replace(dialog, axisGrid, rows, nativeIndex, nativeBefore, nativeAxis, value);
                        var pending = TuningGridUiaDriver.GridText(axisGrid)[0];
                        dialogAxisPending = pending;
                        AxisWriteVerifier.RequirePendingAxis(nativeAxis, pending);
                        var ok = LiveUi.Unique(dialog, "OKButton", "OK", "Button");
                        var dialogHandle = LiveUi.Handle(dialog);
                        commitAttempted = true;
                        LiveUi.Click(dialog, ok);
                        NativeDialogs.WaitClosed(dialogHandle);
                        committed = true;
                        axisCommitted = true;
                    }
                    finally
                    {
                        if (!committed) NativeDialogs.CancelAxisDialog(dialog);
                    }
                    after = driver.Snapshot(table, navigator.ResolveGrid(table));
                    error = AxisWriteVerifier.VerifyExpected(predicted, after);
                }
                else if (confirm) after = before;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ElementNotAvailableException or System.Runtime.InteropServices.ExternalException)
        { error = ex.Message; }
        await audit.AppendAsync(new AuditLogEntry {
            Timestamp = DateTimeOffset.UtcNow, ToolName = "nsp_write_axis_breakpoint", DriverUsed = "uia", DryRun = !confirm,
            Success = error is null, InputsJson = JsonSerializer.Serialize(new { tableId, axis, index, expectedValue, value, allowInterpolation, method = "native-delete-insert" }),
            RequestedValueJson = JsonSerializer.Serialize(new { requested, predicted }),
            AppliedValueJson = JsonSerializer.Serialize(new { commitAttempted, axisCommitted, dialogAxisBefore, dialogAxisPending }),
            BeforeValueJson = JsonSerializer.Serialize(before), AfterValueJson = JsonSerializer.Serialize(after), ErrorMessage = error });
        return new { success = error is null, dryRun = !confirm, tableId, axis, index, commitAttempted, axisCommitted, requestedBreakpoints = requested, before, predicted, after, dialogAxisBefore, dialogAxisPending, errorMessage = error };
    }
}
