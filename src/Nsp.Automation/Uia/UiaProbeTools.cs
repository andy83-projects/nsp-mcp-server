using System.ComponentModel;
using System.IO;
using System.Windows.Automation;
using ModelContextProtocol.Server;
using Nsp.Automation.WindowManager;
using Nsp.Automation.Fallback;

namespace Nsp.Automation.Uia;

/// <summary>
/// Exposes the Phase 0 UIA capability probe as an MCP tool, so running it doesn't need a
/// separate console app. Open NSP (and, for a specific region, its F4 Function Manager or
/// a tuning table) first, then call this — write the results into
/// ../../docs/PHASE0_FINDINGS.md by hand, since that file is what seeds CapabilityRegistry's
/// defaults.
/// </summary>
[McpServerToolType]
public sealed class UiaProbeTools(NspWindowLocator windowLocator, UiaCapabilityProbe probe, NspAppDriverOptions options, NspTableNavigator tables, Nsp.TuningTables.TableCatalog catalog)
{
    [McpServerTool(Name = "nsp_cancel_dialog", Destructive = true)]
    [Description("Cancel one exact NSP-owned dialog by AutomationId and exact title, verifying it closed. Discards pending dialog edits. Requires confirm=true.")]
    public object CancelDialog(string automationId, string expectedTitle, bool confirm)
    {
        if (!confirm) return new { dryRun = true, automationId, expectedTitle };
        lock (MenuNavigator.InteractionLock)
        {
            var main = windowLocator.Locate(options.ExpectedOpenFileName).Window;
            var dialog = SettingsUi.Dialogs(main).SingleOrDefault(e =>
                e.Current.AutomationId == automationId && e.Current.Name == expectedTitle);
            if (dialog is null) throw new InvalidOperationException("Exact dialog not found.");
            NativeDialogs.CancelDialog(dialog, automationId);
            return new { success = true, automationId, expectedTitle };
        }
    }

    [McpServerTool(Name = "nsp_cancel_axis_setup", Destructive = true)]
    [Description("Cancel the exact axis setup dialog through its native Close message, verifying it disappeared. Discards pending axis dialog edits; no save or upload.")]
    public object CancelAxisSetup(bool confirm)
    {
        if (!confirm) return new { dryRun = true };
        lock (MenuNavigator.InteractionLock)
        {
            var main = windowLocator.Locate(options.ExpectedOpenFileName).Window;
            var dialog = NativeDialogs.AxisDialog(main);
            if (dialog is not null) NativeDialogs.CancelAxisDialog(dialog);
            return new { success = true, wasOpen = dialog is not null };
        }
    }

    [McpServerTool(Name = "nsp_inspect_table", ReadOnly = true)]
    [Description("Navigate to an exact help-identified table and read its full live grid and axis-setup metadata, then Cancel the dialog. No calibration edits or image assistance.")]
    public object InspectTable(string searchText, string helpIdentity)
    {
        lock (MenuNavigator.InteractionLock)
        {
            var definition = new Nsp.TuningTables.TableDefinition { TableId = "inspection", DisplayName = "inspection", RowAxisLabel = "", ColumnAxisLabel = "",
                CellUnit = "", DriverKind = Nsp.TuningTables.TableDriverKind.Uia, NavigationSearchText = searchText, HelpIdentity = helpIdentity };
            var grid = tables.ResolveGrid(definition);
            var main = tables.MainWindow;
            Console.WriteLine("[INSPECT] snapshot");
            var values = TuningGridUiaDriver.GridText(grid);
            object Header(AutomationElement e) => new { name = e.Current.Name, value = LiveUi.Value(e),
                patterns = e.GetSupportedPatterns().Select(p => p.ProgrammaticName).ToArray() };
            var tablePattern = (TablePattern)grid.GetCurrentPattern(TablePattern.Pattern);
            var headers = new { rows = tablePattern.Current.GetRowHeaders().Select(Header).ToArray(),
                columns = tablePattern.Current.GetColumnHeaders().Select(Header).ToArray() };
            var help = NspTableNavigator.HelpText(main);
            var tableId = catalog.TryGet(helpIdentity)?.TableId;
            if (!grid.Current.IsEnabled) return new { tableId, helpIdentity, values, axesAvailable = false, errorMessage = "This table is disabled in the current configuration." };
            Console.WriteLine("[INSPECT] snapshot read");
            // Preserve the verified cell focus established by ResolveGrid.
            Console.WriteLine("[INSPECT] sending F3");
            AutomationElement dialog;
            try { dialog = NativeDialogs.OpenAxisDialog(main, grid); }
            catch (InvalidOperationException ex)
            {
                return new { tableId, helpIdentity, values, headers, help, axesAvailable = false, errorMessage = ex.Message,
                    dialogs = SettingsUi.Dialogs(main).Select(d => new { title = d.Current.Name, id = d.Current.AutomationId }).ToArray() };
            }
            Console.WriteLine("[INSPECT] F3 sent");
            Console.WriteLine("[INSPECT] axis dialog found");
            try
            {
                return new { tableId, helpIdentity, title = dialog.Current.Name, values, headers, help, axesAvailable = true,
                    columnAxis = TuningGridUiaDriver.GridText(LiveUi.Scope(dialog, "HorizDataGridView")),
                    rowAxis = TuningGridUiaDriver.GridText(LiveUi.Scope(dialog, "VertDataGridView")),
                    buttons = LiveUi.RawChildren(dialog).Where(e => e.Current.ControlType == ControlType.Button)
                        .Select(e => new { id = e.Current.AutomationId, name = e.Current.Name, enabled = e.Current.IsEnabled }).ToArray(),
                    labels = LiveUi.NativeChildren(dialog).Where(c => c.Visible && c.ClassName.Contains("Static") && c.Text.Length > 0)
                        .Select(c => new { c.Text, top = c.Bounds.Top, left = c.Bounds.Left }).ToArray() };
            }
            finally { NativeDialogs.CancelAxisDialog(dialog); }
        }
    }

    [McpServerTool(Name = "nsp_discover_tables", ReadOnly = true)]
    [Description("Survey tables matching NSP tree search without images. Selects real cells to read their exact help identities; returns live dimensions, headers and numeric limits. Does not edit values, save or upload. Bounded traversal; may change current page.")]
    public object DiscoverTables(string searchText, int maxTables = 8, CancellationToken cancellationToken = default)
    {
        lock (MenuNavigator.InteractionLock)
        {
            var main = windowLocator.Locate(options.ExpectedOpenFileName).Window;
            var results = new Dictionary<string, object>();
            bool Inspect()
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = LiveUi.Scope(main, "MainPageTab");
                if (PageNavigation.Identities(page).Length > 0) return false;
                var grids = LiveUi.Grids(page);
                if (grids.Count != 1) return false;
                var g = (GridPattern)grids[0].GetCurrentPattern(GridPattern.Pattern);
                if (g.Current.RowCount < 1 || g.Current.ColumnCount < 1) return false;
                TreeNavigator.SelectGridCell(main, grids[0], g.GetItem(0, 0));
                var help = NspTableNavigator.HelpText(main);
                var identity = help.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                if (identity.Length == 0) return false;
                var table = (TablePattern)grids[0].GetCurrentPattern(TablePattern.Pattern);
                results.TryAdd(identity, new { tableId = catalog.TryGet(identity)?.TableId, registered = catalog.TryGet(identity) is not null, identity, help, rows = g.Current.RowCount, columns = g.Current.ColumnCount,
                    columnHeaders = table.Current.GetColumnHeaders().Select(e => e.Current.Name).ToArray(),
                    rowHeaders = table.Current.GetRowHeaders().Select(e => e.Current.Name).ToArray() });
                return results.Count >= Math.Clamp(maxTables, 1, 30);
            }
            string? stopped = null;
            try { PageNavigation.Navigate(main, searchText, Inspect, 60, cancellationToken, forceSearch: true); }
            catch (InvalidOperationException ex) { stopped = ex.Message; }
            return new { tables = results.Values.ToArray(), stopped };
        }
    }

    [McpServerTool(Name = "nsp_native_region", ReadOnly = true)]
    [Description("Read native child-window metadata only; avoids virtual browser/grid accessibility descendants.")]
    public object NativeRegion(string scopeId = "MainPageTab")
    {
        lock (MenuNavigator.InteractionLock)
        {
            var main = windowLocator.Locate(options.ExpectedOpenFileName).Window;
            var scope = LiveUi.Scope(main, scopeId);
            return new { identities = PageNavigation.Identities(scope), children = LiveUi.NativeChildren(scope).Where(c => c.Visible).Select(c =>
                new { handle = c.Handle.ToInt64(), c.ClassName, c.Text, c.Style, bounds = c.Bounds.ToString() }).ToArray() };
        }
    }

    [McpServerTool(Name = "nsp_input_diagnostics", ReadOnly = true)]
    [Description("Read native DPI awareness and logical/physical cursor position. No image capture or input.")]
    public object InputDiagnostics() => NativeClick.Diagnostics();
    [McpServerTool(Name = "nsp_step_tree_control", Destructive = true)]
    [Description("Diagnostic: send one ordinary arrow directly to the native navigation tree, avoiding global Alt commands. Requires confirm=true; no image recognition or calibration edit.")]
    public object StepTreeControl(bool forward, bool confirm)
    {
        if (!confirm) return new { dryRun = true, forward };
        lock (MenuNavigator.InteractionLock)
        {
            var main = windowLocator.Locate(options.ExpectedOpenFileName).Window;
            TreeNavigator.Step(main, forward);
            return new { success = true, forward };
        }
    }

    [McpServerTool(Name = "nsp_inspect_legacy", ReadOnly = true)]
    [Description("Inspect bounded MSAA accessibility and native child-window metadata for a named region. Does not use images or edit NSP.")]
    public object InspectLegacy(string scopeId = "MainTreeView", int limit = 200)
    {
        lock (MenuNavigator.InteractionLock)
            return LegacyProbe.Inspect(windowLocator.Locate(options.ExpectedOpenFileName).Window, scopeId, Math.Clamp(limit, 1, 1000));
    }

    [McpServerTool(Name = "nsp_step_navigation", Destructive = true)]
    [Description("Move one filtered navigation result forward or backward using NSP's native shortcut, then dismiss menu focus. Does not edit, save or upload. Requires confirm=true. Inspect the resulting page to verify navigation.")]
    public object StepNavigation(bool forward, bool confirm)
    {
        if (!confirm) return new { dryRun = true, forward };
        lock (MenuNavigator.InteractionLock)
        {
            var main = windowLocator.Locate(options.ExpectedOpenFileName).Window;
            if (!ForegroundWindowActivator.Activate(new IntPtr(main.Current.NativeWindowHandle)))
                throw new InvalidOperationException("NSP could not be activated.");
            InputSynthesizer.ReleaseAllModifierKeys();
            InputSynthesizer.NavigateTree(forward);
            Thread.Sleep(300);
            return new { helpText = NspTableNavigator.HelpText(main) };
        }
    }

    [McpServerTool(Name = "nsp_inspect_region", ReadOnly = true)]
    [Description("Inspect a live NSP region by exact AutomationId, including current values, bounds, class names and patterns. Empty scopeId inspects the main window. Bounded diagnostic for dialog/table discovery.")]
    public object InspectRegion(string scopeId = "", int maxDepth = 12, int limit = 200)
    {
        lock (MenuNavigator.InteractionLock)
        {
            var main = windowLocator.Locate(options.ExpectedOpenFileName).Window;
            var scope = LiveUi.Scope(main, scopeId);
            var result = new List<object>();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var e in LiveUi.WalkControls(scope, Math.Clamp(maxDepth, 0, 24), Math.Clamp(limit, 1, 500)))
            {
                if (watch.Elapsed > TimeSpan.FromSeconds(8)) break;
                Nsp.Automation.Core.OperationContext.Checkpoint("Bounded region inspection");
                try
                {
                    var c = e.Current;
                    object? bounds = null;
                    try { var r = c.BoundingRectangle; if (!r.IsEmpty) bounds = new { x = r.X, y = r.Y, width = r.Width, height = r.Height }; }
                    catch (ElementNotAvailableException) { }
                    result.Add(new { name = c.Name, id = c.AutomationId, type = c.ControlType.ProgrammaticName,
                        className = c.ClassName, offscreen = c.IsOffscreen, enabled = c.IsEnabled,
                        value = LiveUi.Value(e), bounds,
                        patterns = e.GetSupportedPatterns().Select(p => p.ProgrammaticName).ToArray() });
                }
                catch (ElementNotAvailableException) { }
            }
            return result;
        }
    }

    [McpServerTool(Name = "nsp_act_on_ui_element", Destructive = true)]
    [Description("Diagnostic action on ONE exact live element in an explicit region. action is click, focus, or set_value. Match by exact AutomationId and/or Name, optionally ControlType without prefix. Repeated native buttons can use controlKey plus exactName/controlType from a fresh settings inventory. Ambiguous/hidden click targets are refused. Requires confirm=true. Does not save or upload a map.")]
    public object ActOnUiElement(string scopeId, string action, bool confirm, string? automationId = null,
        string? exactName = null, string? controlType = null, string? value = null, string? controlKey = null)
    {
        if (!confirm) return new { dryRun = true, scopeId, action, automationId, exactName, value, controlKey };
        lock (MenuNavigator.InteractionLock)
        {
            var main = windowLocator.Locate(options.ExpectedOpenFileName).Window;
            var scope = LiveUi.Scope(main, scopeId);
            if (controlKey is not null && (exactName is null || controlType is null))
                throw new ArgumentException("A runtime controlKey requires exactName and controlType from a fresh inventory.");
            var target = controlKey is null ? LiveUi.Unique(scope, automationId, exactName, controlType) :
                LiveUi.InputControls(scope).SingleOrDefault(e => SettingsUi.Key(e) == controlKey && e.Current.Name == exactName &&
                    e.Current.ControlType.ProgrammaticName == "ControlType." + controlType && (automationId is null || e.Current.AutomationId == automationId))
                ?? throw new InvalidOperationException("Runtime control key is stale or its exact identity changed.");
            switch (action)
            {
                case "click": LiveUi.Click(main, target); break;
                case "invoke": ((InvokePattern)target.GetCurrentPattern(InvokePattern.Pattern)).Invoke(); break;
                case "focus": target.SetFocus(); break;
                case "set_value": LiveUi.SetValue(target, value ?? throw new ArgumentException("value required")); break;
                default: throw new ArgumentException("action must be click, invoke, focus, or set_value");
            }
            return new { success = true, action, verifiedValue = action == "set_value" ? LiveUi.Value(target) : null };
        }
    }

    [McpServerTool(Name = "nsp_send_keyboard_shortcut", Destructive = true)]
    [Description("Input primitive: for an open dialog specify targetScopeId and exact targetTitle from nsp_list_process_windows; an unspecified target refuses when dialogs are open. Activates the NSP window (the real foreground activation, not just focus) and sends an arbitrary SendKeys-format key sequence — e.g. \"{F1}\" for help, \"{F3}\" for Table Axes Setup, \"D\" to cycle display mode. See docs/NSP_KEYBOARD_SHORTCUTS.md for the full reference. Requires confirm=true.")]
    public object SendKeyboardShortcut(string keys, bool confirm, string? targetScopeId = null, string? targetTitle = null)
    {
        if (!confirm)
        {
            return new { dryRun = true, message = $"Would send '{keys}' to the NSP window." };
        }

        var window = windowLocator.Locate(options.ExpectedOpenFileName);
        if (!TryInputTarget(window.Window, targetScopeId, targetTitle, out var target, out var error)) return error!;
        MenuNavigator.SendKeysToWindow(target!, keys);

        return new { success = true, keysSent = keys };
    }

    [McpServerTool(Name = "nsp_capture_screenshot", ReadOnly = true)]
    [Description("Optional diagnostic PNG of NSP foreground client area (or exact targetScopeId/targetTitle dialog client area; use the same target for clicks) at physical 1:1 pixels. Activates NSP, then copies only its on-screen client rectangle; overlapping windows or menus can appear. Saves under logs/screenshots and returns path, size and title. Use nsp_discover_pages/native tools first; screenshots are never used by runtime navigation or settings. Does not edit, save or upload a map.")]
    public object CaptureScreenshot(string? targetScopeId = null, string? targetTitle = null)
    {
        var window = windowLocator.Locate(options.ExpectedOpenFileName);
        if (!TryInputTarget(window.Window, targetScopeId, targetTitle, out var target, out var error)) return error!;
        var handle = LiveUi.Handle(target!);
        using var bitmap = ScreenCapture.CaptureWindow(handle);

        var directory = Path.Combine("logs", "screenshots");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"nsp-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.png");
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);

        return new { success = true, path = Path.GetFullPath(path), width = bitmap.Width, height = bitmap.Height, coordinateSpace = "physical_client_pixels", captureSource = "foreground_client_screen", windowTitle = target!.Current.Name, targetScopeId, targetTitle, nativeHandle = handle.ToInt64() };
    }

    [McpServerTool(Name = "nsp_click_in_screenshot", Destructive = true)]
    [Description("Fallback of last resort: specify the same targetScopeId/targetTitle used for the screenshot; unspecified target refuses while dialogs are open. Clicks a pixel coordinate taken directly from an nsp_capture_screenshot image (same client-area pixel space, 1:1 — no conversion needed on the caller's side). Only for elements nothing else can address: NSP's navigation tree and its search results are custom-drawn with zero UIA children, so native traversal tools should be tried first; use this only for diagnosis when structured navigation fails. Prefer every named/structured tool first; reach for this only after a screenshot has shown exactly what to click and nothing else can reach it. Requires confirm=true.")]
    public object ClickInScreenshot(int x, int y, bool confirm, string? targetScopeId = null, string? targetTitle = null)
    {
        if (!confirm) return new { dryRun = true, x, y };
        var window = windowLocator.Locate(options.ExpectedOpenFileName);
        if (!TryInputTarget(window.Window, targetScopeId, targetTitle, out var target, out var error)) return error!;
        var handle = LiveUi.Handle(target!);
        OwnedWindows.RequireForeground(handle);
        NativeClick.Click(handle, new System.Drawing.Point(x, y));
        return new { success = true, x, y };
    }

    [McpServerTool(Name = "nsp_probe_ui_tree", ReadOnly = true)]
    [Description("Phase 0 diagnostic: walks the current NSP window's UIA tree and classifies it as Full/Partial/None addressable, with sample AutomationIds found. Always probes whatever window currently matches the configured expected file name — open the dialog/table you want inspected first, then call this.")]
    public object ProbeUiTree(int maxDepth = 6)
    {
        var window = windowLocator.Locate(options.ExpectedOpenFileName);
        var finding = probe.ProbeRegion(window.Window, window.Title, maxDepth);

        return new
        {
            finding.RegionName,
            supportLevel = finding.SupportLevel.ToString(),
            finding.Notes,
            finding.SampleAutomationIds,
        };
    }

    [McpServerTool(Name = "nsp_find_ui_element", ReadOnly = true)]
    [Description("Phase 0/1 diagnostic: searches the current NSP window's UIA tree for elements whose Name or AutomationId contains the given text (case-insensitive), returning each match's Name, AutomationId, ControlType, tree depth, and supported UIA patterns. Use this to locate a specific control once you know roughly what it's called (e.g. \"Available Functions\"), rather than reading through nsp_probe_ui_tree's capped sample list.")]
    public object FindUiElement(string textContains, int maxDepth = 20, int limit = 30, string scopeId = "", string? controlType = null)
    {
        var window = windowLocator.Locate(options.ExpectedOpenFileName);
        var matches = probe.FindElements(LiveUi.Scope(window.Window, scopeId), textContains, maxDepth, limit, controlType);

        return new
        {
            query = textContains, scopeId, controlType, complete = false,
            coverage = "Bounded control-only search: 500 visited nodes / 8 seconds; virtual grid/document children excluded. No matches is not proof of absence.",
            matchCount = matches.Count,
            matches = matches.Select(m => new
            {
                m.Name,
                m.AutomationId,
                m.ControlType,
                m.Depth,
                m.SupportedPatterns,
            }),
        };
    }

    [McpServerTool(Name = "nsp_invoke_ui_element", Destructive = true)]
    [Description("Phase 1/2 navigation primitive: finds the first element whose Name contains the given text (optionally also matching a ControlType, e.g. \"Hyperlink\" or \"Button\") and clicks it with a REAL mouse click at its screen coordinates — e.g. click a function's Hyperlink in the Available Functions panel to jump to its settings page. (Confirmed live that UIA's InvokePattern alone reports success without actually triggering navigation for these controls, hence a real click.) Requires confirm=true; without it, returns a dry-run preview only.")]
    public object InvokeUiElement(string textContains, string? controlType, bool confirm, int maxDepth = 20)
    {
        if (!confirm)
        {
            return new
            {
                dryRun = true,
                message = $"Would click the first element matching '{textContains}'" +
                           (controlType is null ? "" : $" with ControlType containing '{controlType}'") + ".",
            };
        }

        var window = windowLocator.Locate(options.ExpectedOpenFileName);
        var clicked = probe.TryClickFirstMatch(window.Window, textContains, controlType, maxDepth);

        return clicked is null
            ? new { success = false, message = $"No visible element found matching '{textContains}'." }
            : new { success = true, clicked.Name, clicked.AutomationId, clicked.ControlType, clicked.Depth };
    }

    [McpServerTool(Name = "nsp_search_navigation_tree", Destructive = true)]
    [Description("Set NSP navigation search text. Custom-drawn result rows are not UIA-addressable; setting the filter does not verify or select a destination. Use nsp_discover_pages to obtain exact settings headings and nsp_navigate_to_page to navigate and verify. Some functions have a Wiring child; others have connections directly on their own page. Requires confirm=true; no calibration edit/save/upload.")]
    public object SearchNavigationTree(string searchText, bool confirm)
    {
        if (!confirm)
        {
            return new { dryRun = true, message = $"Would search the navigation tree for '{searchText}'." };
        }

        var window = windowLocator.Locate(options.ExpectedOpenFileName);

        // The search box's accessible Name is dynamic — it's "Available Functions:" only
        // while that filter mode/label is showing; it can be empty otherwise (confirmed
        // live 2026-10-20). Match by ControlType only, scoped under MainTreeView so we don't
        // accidentally grab an unrelated Edit elsewhere in the window.
        var treeView = window.Window.FindFirst(
            TreeScope.Children,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "MainTreeView"));

        var searchScope = treeView ?? window.Window;

        var searchBox = searchScope.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

        if (searchBox is null || !searchBox.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) || pattern is not ValuePattern value)
        {
            return new { success = false, message = "Navigation search box not found or does not support ValuePattern." };
        }

        value.SetValue(searchText);
        System.Threading.Thread.Sleep(300);

        return new { success = true, searchText };
    }

    [McpServerTool(Name = "nsp_list_process_windows", ReadOnly = true)]
    [Description("Diagnostic: enumerates the selected NSP main window and its verified visible native owned windows, including dialogs omitted from the desktop UIA children. Returns exact owned-title scopes even when AutomationId is empty. Use this when a visibly-present UI region (e.g. a docked sidebar) can't be found by nsp_find_ui_element from the main window — it may actually be a separate top-level window not nested in the main window's UIA tree at all, same as the Help browser.")]
    public object ListProcessWindows()
    {
        var mainWindow = windowLocator.Locate(options.ExpectedOpenFileName);
        var processId = mainWindow.Window.Current.ProcessId;

        var topLevelWindows = new[] { LiveUi.Handle(mainWindow.Window) }.Concat(OwnedWindows.Dialogs(LiveUi.Handle(mainWindow.Window)))
            .Select(AutomationElement.FromHandle);

        var results = new List<object>();
        foreach (AutomationElement window in topLevelWindows)
        {
            var rect = window.Current.BoundingRectangle;
            results.Add(new
            {
                nativeHandle = LiveUi.Handle(window).ToInt64(),
                scopeId = LiveUi.Handle(window) == LiveUi.Handle(mainWindow.Window) ? "" : "owned-title:" + window.Current.Name,
                controlType = window.Current.ControlType.ProgrammaticName,
                name = window.Current.Name,
                automationId = window.Current.AutomationId,
                boundingRectangle = rect.IsEmpty ? null : $"{rect.Left},{rect.Top} {rect.Width}x{rect.Height}",
                isOffscreen = window.Current.IsOffscreen,
            });
        }

        return new { processId, windowCount = results.Count, windows = results };
    }
    private static bool TryInputTarget(AutomationElement main, string? scopeId, string? title, out AutomationElement? target, out object? error)
    {
        target = null; error = null;
        try { target = InputTarget(main, scopeId, title); return true; }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            error = new { success = false, errorCode = "invalid_input_target", errorMessage = ex.Message, actionAttempted = false,
                nextAction = "Call nsp_list_process_windows and use the exact dialog scopeId and name as targetScopeId/targetTitle. Screenshot capture and coordinate clicks must use the same target." };
            return false;
        }
    }
    private static AutomationElement InputTarget(AutomationElement main, string? scopeId, string? title)
    {
        if (scopeId is null && title is null) { OwnedWindows.DefaultInput(LiveUi.Handle(main)); return main; }
        if (string.IsNullOrWhiteSpace(scopeId) || string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("A dialog input target requires both targetScopeId and exact targetTitle.");
        return SettingsUi.Scope(main, scopeId, title);
    }

}
