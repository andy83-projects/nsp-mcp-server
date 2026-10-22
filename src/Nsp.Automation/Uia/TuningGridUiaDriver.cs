using System.Globalization;
using System.Windows.Automation;
using Nsp.Automation.Core;
using Nsp.Automation.Fallback;
using Nsp.TuningTables;

namespace Nsp.Automation.Uia;

/// <summary>Read real Grid/Table patterns. NSP's ValuePattern cells are read-only; writes
/// select an exact UIA cell, type into the native inline editor, commit, then compare the
/// complete grid and axes so an unintended neighbouring-cell change cannot count as success.</summary>
public sealed class TuningGridUiaDriver(NspTableNavigator navigator) : ICellDriver
{
    public bool CanHandle(TableDefinition table) => table.DriverKind == TableDriverKind.Uia;

    private static GridPattern Grid(AutomationElement grid) => (GridPattern)grid.GetCurrentPattern(GridPattern.Pattern);
    private static double Number(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n))
            throw new InvalidOperationException($"Expected a finite numeric table value, received '{value}'.");
        return n;
    }
    private static double Cell(AutomationElement grid, int row, int col)
    {
        var g = Grid(grid);
        if (row < 0 || row >= g.Current.RowCount || col < 0 || col >= g.Current.ColumnCount)
            throw new ArgumentOutOfRangeException(nameof(row), $"Cell ({row},{col}) is outside {g.Current.RowCount}x{g.Current.ColumnCount}.");
        return Number(LiveUi.Value(g.GetItem(row, col)) ?? throw new InvalidOperationException("Cell has no ValuePattern."));
    }
    private (double[] Rows, double[] Columns) Axes(AutomationElement grid, TableDefinition table)
    {
        var g = Grid(grid);
        var t = (TablePattern)grid.GetCurrentPattern(TablePattern.Pattern);
        if (table.HasNoAxes)
        {
            if (g.Current.RowCount != 1 || g.Current.ColumnCount != 1 ||
                t.Current.GetRowHeaders().Length != 0 || t.Current.GetColumnHeaders().Length != 0)
                throw new InvalidOperationException("Expected an axis-free one-cell table; its live shape/axes changed.");
            return ([], []);
        }
        var rows = table.ReadDescendingRowsFromAxisSetup ? AxisSetupRows(grid, table) :
            t.Current.GetRowHeaders().Select(e => Number(e.Current.Name)).ToArray();
        var cols = t.Current.GetColumnHeaders().Select(e => Number(e.Current.Name)).ToArray();
        if (cols.Length != g.Current.ColumnCount || (g.Current.RowCount > 1 && rows.Length != g.Current.RowCount))
            throw new InvalidOperationException("Table headers do not match the live grid dimensions.");
        return (rows, cols);
    }

    private double[] AxisSetupRows(AutomationElement grid, TableDefinition table)
    {
        if (!grid.Current.IsEnabled) throw new InvalidOperationException("Table axis setup is disabled in the current configuration.");
        // Read owner-grid properties before opening a modal dialog. Querying the
        // disabled owner DataGrid provider while its axis dialog is open can stall UIA.
        var actualColumns = ((TablePattern)grid.GetCurrentPattern(TablePattern.Pattern)).Current.GetColumnHeaders()
            .Select(e => Number(e.Current.Name)).ToArray();
        var main = navigator.MainWindow;
        Console.WriteLine("[AXES] opening");
        LiveUi.Click(main, Grid(grid).GetItem(0, 0));
        MenuNavigator.SendKeysToWindow(main, "{F3}");
        Thread.Sleep(150);
        var dialog = LiveUi.Scope(main, "TableAxisSetupForm");
        try
        {
            Console.WriteLine("[AXES] dialog found");
            if (dialog.Current.Name != table.DisplayName + " : Table Axis Setup")
                throw new InvalidOperationException("Axis dialog identity does not match the requested table.");
            if (table.SupportsSliceAxis)
            {
                var slices = LiveUi.Unique(dialog, "SlicesEnableCheckBox", null, "CheckBox");
                if (((TogglePattern)slices.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState != ToggleState.Off)
                    throw new InvalidOperationException("Enabled third-axis slices are not supported.");
            }
            Console.WriteLine("[AXES] slice check complete");
            static double[] ReadAxis(AutomationElement element)
            {
                var gp = Grid(element);
                if (gp.Current.RowCount != 1) throw new InvalidOperationException("Expected a one-row axis setup grid.");
                var result = new List<double>();
                var ended = false;
                foreach (var value in GridText(element)[0])
                {
                    if (string.IsNullOrWhiteSpace(value) || value == "(null)") { ended = true; continue; }
                    if (ended) throw new InvalidOperationException("Axis contains an interior empty breakpoint.");
                    result.Add(Number(value));
                }
                return result.ToArray();
            }
            var columns = ReadAxis(LiveUi.Scope(dialog, "HorizDataGridView"));
            Console.WriteLine("[AXES] columns read");
            if (!columns.SequenceEqual(actualColumns)) throw new InvalidOperationException("Axis dialog columns disagree with the live table.");
            var rows = ReadAxis(LiveUi.Scope(dialog, "VertDataGridView")).Reverse().ToArray();
            Console.WriteLine("[AXES] read complete");
            return rows;
        }
        finally { LiveUi.Click(dialog, LiveUi.Unique(dialog, "CancellingButton", "Cancel", "Button")); }
    }
    internal static string[][] GridText(AutomationElement grid)
    {
        OperationContext.Checkpoint("Reading complete grid");
        var g = Grid(grid);
        var rows = g.Current.RowCount; var cols = g.Current.ColumnCount;
        if (rows < 1 || cols < 1 || (long)rows * cols > 10000) throw new InvalidOperationException("Grid exceeds snapshot bounds.");
        var cache = new CacheRequest { TreeScope = TreeScope.Element };
        cache.Add(GridItemPattern.RowProperty); cache.Add(GridItemPattern.ColumnProperty); cache.Add(ValuePattern.ValueProperty);
        AutomationElementCollection cells;
        using (cache.Activate())
            cells = grid.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem));
        var result = Enumerable.Range(0, rows).Select(_ => new string[cols]).ToArray();
        var seen = new HashSet<(int, int)>();
        foreach (AutomationElement cell in cells)
        {
            var row = (int)cell.GetCachedPropertyValue(GridItemPattern.RowProperty);
            var col = (int)cell.GetCachedPropertyValue(GridItemPattern.ColumnProperty);
            if (row < 0 || row >= rows || col < 0 || col >= cols || !seen.Add((row, col)))
                throw new InvalidOperationException("Duplicate or invalid grid address in snapshot.");
            result[row][col] = (string)cell.GetCachedPropertyValue(ValuePattern.ValueProperty);
        }
        if (seen.Count != rows * cols) throw new InvalidOperationException("Accessibility snapshot omitted cells.");
        return result;
    }
    private static double[][] Values(AutomationElement grid) => GridText(grid).Select(row => row.Select(Number).ToArray()).ToArray();

    internal TableSnapshotData Snapshot(TableDefinition table, AutomationElement grid)
    {
        var axes = Axes(grid, table);
        return new(axes.Rows, axes.Columns, Values(grid));
    }

    public Task<AutomationResult> ReadCellAsync(TableDefinition table, int row, int col, CancellationToken cancellationToken = default)
    {
        lock (MenuNavigator.InteractionLock)
        {
            try { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(AutomationResult.Succeeded("uia", Cell(navigator.ResolveGrid(table), row, col))); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ElementNotAvailableException)
            { return Task.FromResult(AutomationResult.Failed("uia", ex.Message)); }
        }
    }

    public Task<AutomationResult> WriteCellAsync(TableDefinition table, int row, int col, double appliedValue, CancellationToken cancellationToken = default)
    {
        lock (MenuNavigator.InteractionLock)
        {
            double? beforeValue = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!double.IsFinite(appliedValue) || Math.Abs(Math.Round(appliedValue, table.CellDecimalPlaces) - appliedValue) > 1e-10)
                    throw new ArgumentException($"Value must be finite with at most {table.CellDecimalPlaces} decimal places.");
                var main = navigator.MainWindow;
                LiveUi.RequireOffline(main);
                var grid = navigator.ResolveGrid(table);
                beforeValue = Cell(grid, row, col);
                var axesBefore = Axes(grid, table);
                var before = Values(grid);
                cancellationToken.ThrowIfCancellationRequested(); // Safe cancellation boundary: no value has been typed yet.
                var target = Grid(grid).GetItem(row, col);
                InputSynthesizer.ReleaseAllModifierKeys();
                InputSynthesizer.Escape(); // cancel pending edits/shrink a multi-cell selection
                LiveUi.Click(main, target);
                var focused = AutomationElement.FocusedElement;
                var inside = false;
                for (var i = 0; i < 12 && focused is not null; i++, focused = TreeWalker.RawViewWalker.GetParent(focused))
                    if (System.Windows.Automation.Automation.Compare(focused, grid)) { inside = true; break; }
                if (!inside) throw new InvalidOperationException("Keyboard focus did not land inside the requested table.");
                cancellationToken.ThrowIfCancellationRequested();
                OperationContext.Checkpoint("Ready to write table cell");
                using var deferredCancellation = OperationContext.DeferCancellation();
                // After typing, finish verification even if the client disconnects.
                // Never Ctrl+A here: in a tuning grid it selects every cell.
                var text = appliedValue.ToString("0." + new string('#', table.CellDecimalPlaces), CultureInfo.InvariantCulture);
                // NSP ignores KEYEVENTF_UNICODE in this custom grid. Its inline editor
                // responds to the ordinary key events produced by SendKeys instead.
                MenuNavigator.SendKeysToWindow(main, text + "{ENTER}");
                Thread.Sleep(150);
                InputSynthesizer.Escape(); // discard any uncommitted buffer if Enter was ignored
                Thread.Sleep(100);
                var after = Values(grid);
                var axesAfter = Axes(grid, table);
                var error = TableWriteVerifier.Verify(new(axesBefore.Rows, axesBefore.Columns, before),
                    new(axesAfter.Rows, axesAfter.Columns, after), row, col, appliedValue);
                if (error is not null) throw new InvalidOperationException(error + " No save performed.");
                return Task.FromResult(AutomationResult.Succeeded("uia", after[row][col]) with { BeforeValue = beforeValue });
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ElementNotAvailableException)
            {
                return Task.FromResult(AutomationResult.Failed("uia", ex.Message) with { BeforeValue = beforeValue });
            }
        }
    }

    public Task<AutomationResult> ReadTableSnapshotAsync(TableDefinition table, CancellationToken cancellationToken = default)
    {
        lock (MenuNavigator.InteractionLock)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var grid = navigator.ResolveGrid(table);
                var axes = Axes(grid, table);
                return Task.FromResult(AutomationResult.Succeeded("uia", new { tableId = table.TableId, rowBreakpoints = axes.Rows,
                    columnBreakpoints = axes.Columns, values = Values(grid), cellUnit = table.CellUnit }));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ElementNotAvailableException)
            { return Task.FromResult(AutomationResult.Failed("uia", ex.Message)); }
        }
    }

    public Task<(double[] RowBreakpoints, double[] ColumnBreakpoints)> GetAxesAsync(TableDefinition table, CancellationToken cancellationToken = default)
    {
        lock (MenuNavigator.InteractionLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var axes = Axes(navigator.ResolveGrid(table), table);
            return Task.FromResult((axes.Rows, axes.Columns));
        }
    }
}
