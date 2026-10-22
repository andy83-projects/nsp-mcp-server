using System.Diagnostics;
using System.Windows.Automation;

namespace Nsp.Automation.Uia;

public static class PageNavigation
{
    public static string[] Identities(AutomationElement scope)
    {
        var native = LiveUi.NativeChildren(scope).Where(c => c.Visible && !string.IsNullOrWhiteSpace(c.Text)).ToArray();
        var firstStatic = native.Where(c => c.ClassName.Contains("WindowsForms10.Static", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Bounds.Top).DefaultIfEmpty(double.PositiveInfinity).Min();
        var labels = native.Where(c => c.ClassName.Contains("WindowsForms10.Static", StringComparison.OrdinalIgnoreCase) ||
            (c.ClassName.Contains("WindowsForms10.Button", StringComparison.OrdinalIgnoreCase) &&
             ((c.Style & 15) is 2 or 3 or 5 or 6 or 7 ||
              // NSP draws sensor headings as owner-drawn checkboxes. Ordinary
              // buttons such as the base-fuel Q action are not page headings.
              ((c.Style & 15) == 11 && c.Bounds.Top <= firstStatic + 8 &&
               (c.Text.Contains(" - ", StringComparison.Ordinal) || LiveUi.FromHandle(c.Handle).Current.ControlType == ControlType.CheckBox))))).ToList();
        if (labels.Count == 0) return [];
        var top = labels.Min(c => c.Bounds.Top);
        return labels.Where(c => c.Bounds.Top <= top + 8).Select(c => c.Text).Distinct().ToArray();
    }

    public static bool Matches(AutomationElement main, string expected)
    {
        var wanted = LiveUi.Normalize(expected);
        var identities = Identities(LiveUi.Scope(main, "MainPageTab"));
        Console.WriteLine("[PAGE] " + string.Join(" | ", identities.Take(8)));
        return identities.Any(n => LiveUi.Normalize(n) == wanted);
    }

    public static object Navigate(AutomationElement main, string searchText, Func<bool> matches, int maxSteps = 60, CancellationToken cancellationToken = default, bool forceSearch = false, string? destinationIdentity = null)
    {
        var watch = Stopwatch.StartNew();
        var traversal = new Nsp.Automation.Core.NavigationBudget(maxSteps, () => watch.Elapsed);
        cancellationToken.ThrowIfCancellationRequested();
        Nsp.Automation.Core.OperationContext.Checkpoint("Checking navigation destination");
        if (Nsp.Automation.WindowManager.OwnedWindows.Dialogs(LiveUi.Handle(main)).Length != 0)
            throw new InvalidOperationException("Close the open dialog before navigating.");
        if (!forceSearch && matches()) return new { alreadyAtDestination = true, queries = Array.Empty<string>(), steps = 0 };
        Nsp.Automation.Core.OperationContext.Checkpoint("Resolving native navigation search");
        var search = LiveUi.NativeChildren(MenuNavigator.EnsureNavigationTree(main))
            .Where(c => c.Visible && c.ClassName.Contains("WindowsForms10.Edit", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (search.Length != 1) throw new InvalidOperationException("Expected one native navigation search input; no search sent.");
        var queries = Nsp.Automation.Core.NavigationQueries.Build(searchText, destinationIdentity);
        var tried = new List<string>();
        for (var queryIndex = 0; queryIndex < queries.Length; queryIndex++)
        {
            var query = queries[queryIndex];
            Nsp.Automation.Core.OperationContext.Checkpoint("Filtering navigation: " + query);
            cancellationToken.ThrowIfCancellationRequested();
            if (watch.Elapsed >= TimeSpan.FromSeconds(50) || !traversal.CanStep) break;
            // Selecting a page can auto-hide the pane. Reopen it before the next
            // filter/reset so native tree bounds cannot point into the page behind it.
            MenuNavigator.EnsureNavigationTree(main);
            tried.Add(query);
            LiveUi.SetNativeText(search[0].Handle, query);
            Thread.Sleep(700); // NSP's filter timer is 400ms; let its rebuild finish.
            TreeNavigator.SeedSelection(main);
            if (matches()) return new { alreadyAtDestination = false, queries = tried.ToArray(), steps = traversal.Steps, elapsedMs = watch.ElapsedMilliseconds };
            var attempt = Stopwatch.StartNew();
            var remainingQueries = queries.Length - queryIndex;
            var budget = TimeSpan.FromMilliseconds(Math.Max(0, (50000 - watch.Elapsed.TotalMilliseconds) / remainingQueries));
            var attemptSteps = traversal.Steps;
            // Reserve time and arrows for shorter fallbacks. A failing full phrase
            // must not consume the entire shared budget before they can be tried.
            var stepBudget = Math.Max(1, (Math.Clamp(maxSteps, 1, 120) - traversal.Steps) / remainingQueries);
            // SeedSelection resets the viewport and selects the first row; do not
            // skip that row by sending arrows before checking its destination.
            // Filtering retains the old selected node, which can already be the
            // LAST result. A forward-only scan then never leaves that node. Sweep
            // both ways and stop each direction at a stable visible heading.
            foreach (var forward in new[] { true, false })
            {
                string? previous = null;
                var unchanged = 0;
                for (var i = 0; traversal.Steps - attemptSteps < stepBudget && traversal.CanStep && watch.Elapsed < TimeSpan.FromSeconds(50) && attempt.Elapsed < budget; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Nsp.Automation.Core.OperationContext.Checkpoint("Navigating query '" + query + "', step " + i);
                    traversal.TakeStep();
                    TreeNavigator.Step(main, forward);
                    if (matches()) return new { alreadyAtDestination = false, queries = tried.ToArray(), steps = traversal.Steps, elapsedMs = watch.ElapsedMilliseconds };
                    var current = string.Join(" | ", Identities(LiveUi.Scope(main, "MainPageTab")));
                    unchanged = current.Length > 0 && current == previous ? unchanged + 1 : 0;
                    previous = current;
                    if (unchanged >= 3) break;
                }
            }
            if (watch.Elapsed >= TimeSpan.FromSeconds(50)) break;
        }
        throw new InvalidOperationException($"Destination not verified after {traversal.Steps} native steps ({watch.Elapsed.TotalSeconds:F1}s). Queries: {string.Join(" | ", tried)}. No calibration value was edited.");
    }
}
