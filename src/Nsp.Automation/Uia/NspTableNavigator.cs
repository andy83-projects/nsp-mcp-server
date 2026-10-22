using System.Globalization;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using Nsp.Automation.Fallback;
using Nsp.Automation.WindowManager;
using Nsp.TuningTables;

namespace Nsp.Automation.Uia;

public sealed class NspTableNavigator(NspWindowLocator locator, NspAppDriverOptions options, TableCatalog catalog) : ILiveClampRangeSource
{
    public AutomationElement MainWindow => locator.Locate(options.ExpectedOpenFileName).Window;

    public static string HelpText(AutomationElement main)
    {
        var tree = LiveUi.Scope(main, "MainTreeView");
        // NSP auto-hides this pane when a real grid cell receives focus. Its native
        // help document remains current and readable while the pane is hidden.
        return string.Join("\n", LiveUi.NativeChildren(tree).Where(c => c.ClassName.Contains("RICHEDIT", StringComparison.OrdinalIgnoreCase))
            .Select(c => LiveUi.NativeText(c.Handle)));
    }

    public AutomationElement ResolveGrid(TableDefinition table)
    {
        lock (MenuNavigator.InteractionLock)
        {
            Console.WriteLine("[TABLE] locating main");
            var main = MainWindow;
            Console.WriteLine("[TABLE] main found");
            if (Nsp.Automation.WindowManager.OwnedWindows.Dialogs(LiveUi.Handle(main)).Length != 0)
                throw new InvalidOperationException("Close NSP's open dialog before navigating to a tuning table.");
            AutomationElement? grid = null;
            if (string.IsNullOrEmpty(table.NavigationSearchText) || string.IsNullOrEmpty(table.HelpIdentity))
                throw new InvalidOperationException("Table navigation metadata has not been verified.");
            PageNavigation.Navigate(main, table.NavigationSearchText, () => TryCurrent(main, table, out grid), 40, destinationIdentity: table.DisplayName);
            return grid!;
        }
    }

    private static bool TryCurrent(AutomationElement main, TableDefinition table, out AutomationElement? grid)
    {
        Console.WriteLine("[TABLE] inspect current");
        grid = null;
        var page = LiveUi.Scope(main, "MainPageTab");
        // Scalar/configuration pages have native headings. Painted tuning tables do
        // not. Avoid walking unrelated settings and the embedded browser entirely.
        if (PageNavigation.Identities(page).Length > 0) return false;
        var grids = LiveUi.Grids(page);
        Console.WriteLine($"[TABLE] grids {grids.Count}");
        if (grids.Count != 1) return false;
        var candidate = grids[0];
        var gp = (GridPattern)candidate.GetCurrentPattern(GridPattern.Pattern);
        if (gp.Current.RowCount < 1 || gp.Current.ColumnCount < 1) return false;
        // The table's painted caption has no UIA Text element. Focusing a real cell updates
        // NSP's help document to the full table path, providing an independent identity.
        Console.WriteLine("[TABLE] selecting cell");
        TreeNavigator.SelectGridCell(main, candidate, gp.GetItem(0, 0));
        Console.WriteLine("[TABLE] reading help");
        Thread.Sleep(100);
        if (!table.MatchesHelpDocument(HelpText(main))) return false;
        grid = candidate;
        return true;
    }

    public Task<ClampRange?> TryGetLiveRangeAsync(string tableId, int? row, int? col, CancellationToken cancellationToken = default)
    {
        lock (MenuNavigator.InteractionLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var table = catalog.TryGet(tableId);
            if (table is null) return Task.FromResult<ClampRange?>(null);
            ResolveGrid(table);
            var match = Regex.Match(HelpText(MainWindow), @"Minimum:\s*(-?\d+(?:\.\d+)?),\s*Maximum:\s*(-?\d+(?:\.\d+)?)");
            if (!match.Success) return Task.FromResult<ClampRange?>(null);
            var min = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var max = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            if (!double.IsFinite(min) || !double.IsFinite(max) || min > max) throw new InvalidOperationException("Invalid live table range.");
            return Task.FromResult<ClampRange?>(new ClampRange(min, max, "live-ui: " + table.HelpIdentity));
        }
    }
}
