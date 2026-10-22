using System.IO;
using System.Windows.Automation;
using Nsp.Automation.Core;
using Nsp.Automation.WindowManager;

namespace Nsp.Automation.Uia;

public sealed record NspAppDriverOptions
{
    /// <summary>Filename (not full path) NspWindowLocator should match against the NSP
    /// window title, e.g. "Example_Offline_Draft.nexmap".</summary>
    public string? ExpectedOpenFileName { get; set; }
}

/// <summary>
/// Concrete IAppDriver built from the UIA primitives (NspWindowLocator, MenuNavigator,
/// FunctionManagerDriver, FormFieldDriver) — the "menus/dialogs are probably UIA-clean" bet
/// from the plan. If Phase 0 shows otherwise for some region, swap in a fallback-backed
/// implementation at the DI registration in Program.cs rather than branching inside here.
///
/// Several methods are explicitly NotImplementedException stubs for navigation that is
/// genuinely NSP-dialog-specific and cannot be written correctly without the real app's
/// AutomationIds/dialog structure in front of us — that's exactly what Phase 0/1/2 of the
/// plan exist to discover. Implementing them now would just be guessing.
/// </summary>
public sealed class NspUiaAppDriver(
    NspWindowLocator windowLocator,
    WindowHealthMonitor windowHealthMonitor,
    MenuNavigator menuNavigator,
    FunctionManagerDriver functionManagerDriver,
    FormFieldDriver formFieldDriver,
    NspAppDriverOptions options) : IAppDriver
{
    private NspWindowHandle? _cachedWindow;

    private NspWindowHandle ResolveWindow()
    {
        _cachedWindow = _cachedWindow is null
            ? windowLocator.Locate(options.ExpectedOpenFileName)
            : windowHealthMonitor.EnsureAlive(_cachedWindow, options.ExpectedOpenFileName);

        return _cachedWindow;
    }

    public Task<AutomationResult> GetAppStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var window = ResolveWindow();

            return Task.FromResult(AutomationResult.Succeeded("uia", new
            {
                windowFound = true,
                openTitle = window.Title,
                hasUnsavedChanges = window.Title.Contains('*'),
                ecuConnected = LiveUi.EcuConnected(window.Window),
            }));
        }
        catch (Exception ex) when (ex is NspWindowNotFoundException or AmbiguousNspWindowException)
        {
            return Task.FromResult(AutomationResult.Failed("uia", ex.Message));
        }
    }

    public Task<AutomationResult> OpenMapAsync(string filePath, CancellationToken cancellationToken = default)
    {
        lock (MenuNavigator.InteractionLock)
        {
            try
            {
                var fullPath = Path.GetFullPath(filePath);
                if (!File.Exists(fullPath)) throw new ArgumentException("Map file does not exist.");
                var window = ResolveWindow();
                if (window.Title.Contains(fullPath, StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(AutomationResult.Succeeded("uia", window.Title));
                if (window.Title.Contains('*')) throw new InvalidOperationException("Current map has unsaved changes. Open refused; no changes discarded.");
                // Reuse a picker left open by an earlier timed-out request. Cold
                // file dialogs can take several seconds and expose an empty ID.
                var dialog = FindOpenMapDialog(window.Window);
                if (dialog is null) MenuNavigator.SendKeysToWindow(window.Window, "^o");
                for (var i = 0; dialog is null && i < 50; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Thread.Sleep(100);
                    dialog = FindOpenMapDialog(window.Window);
                }
                if (dialog is null) throw new InvalidOperationException("Open Map dialog did not appear within five seconds.");
                // Windows repeats 1148 on the filename pane, combo and edit.
                // Select by both ID and type inside this exact owned picker.
                var filename = dialog.FindAll(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "1148"),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)));
                if (filename.Count != 1) throw new InvalidOperationException("Open Map filename edit is ambiguous or unavailable.");
                LiveUi.SetValue(filename[0], fullPath);
                var open = LiveUi.Unique(dialog, "1", null, "Button");
                ((InvokePattern)open.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                for (var i = 0; i < 50; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var title = window.Window.Current.Name;
                    if (title.Contains(fullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _cachedWindow = window with { Title = title };
                        options.ExpectedOpenFileName = Path.GetFileName(fullPath);
                        return Task.FromResult(AutomationResult.Succeeded("uia", title));
                    }
                    Thread.Sleep(200);
                }
                throw new InvalidOperationException("The requested full map path was not verified after opening.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ElementNotAvailableException)
            { return Task.FromResult(AutomationResult.Failed("uia", ex.Message)); }
        }
    }

    private static AutomationElement? FindOpenMapDialog(AutomationElement main)
    {
        var child = TreeWalker.RawViewWalker.GetFirstChild(main);
        for (var i = 0; child is not null && i < 32; i++)
        {
            if (child.Current.ControlType == ControlType.Window && child.Current.Name == "Open Map") return child;
            child = TreeWalker.RawViewWalker.GetNextSibling(child);
        }
        return null;
    }

    public Task<AutomationResult> SaveMapAsync(bool confirm, CancellationToken cancellationToken = default)
    {
        if (!confirm)
        {
            return Task.FromResult(AutomationResult.Succeeded("uia", "dry-run: would send Ctrl+S"));
        }

        var window = ResolveWindow();
        var identity = window.Title.TrimEnd(' ', '*');
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var deferredCancellation = OperationContext.DeferCancellation();
        MenuNavigator.SendKeysToWindow(window.Window, "^s");
        do
        {
            OperationContext.Checkpoint("Verifying saved map title");
            var refreshed = windowLocator.Locate(options.ExpectedOpenFileName);
            _cachedWindow = refreshed;
            if (refreshed.ProcessId != window.ProcessId ||
                LiveUi.Handle(refreshed.Window) != LiveUi.Handle(window.Window) ||
                refreshed.Title.TrimEnd(' ', '*') != identity)
                return Task.FromResult(AutomationResult.Failed("uia", "Map/window identity changed during save; inspect the actual saved outcome before retrying."));
            if (OwnedWindows.Dialogs(LiveUi.Handle(refreshed.Window)).Length != 0)
                return Task.FromResult(AutomationResult.Failed("uia", "An NSP dialog is open after Ctrl+S; inspect it before retrying. Save outcome is not verified."));
            if (!refreshed.Title.Contains('*'))
                return Task.FromResult(AutomationResult.Succeeded("uia", refreshed.Title));
            Thread.Sleep(100);
        } while (watch.Elapsed < TimeSpan.FromSeconds(5));
        return Task.FromResult(AutomationResult.Failed("uia", "Save outcome was not verified within five seconds. It may complete later; inspect the current title and dialogs before retrying."));
    }

    public Task<AutomationResult> OpenFunctionManagerAsync(CancellationToken cancellationToken = default)
    {
        var window = ResolveWindow();
        var dialog = menuNavigator.OpenFunctionManager(window.Window, TimeSpan.FromSeconds(3));

        return Task.FromResult(dialog is null
            ? AutomationResult.Failed("uia", "F4 Function Manager dialog did not appear within timeout.")
            : AutomationResult.Succeeded("uia", "Function Manager opened"));
    }

    public Task<AutomationResult> SetFunctionEnabledAsync(string functionName, bool enabled, bool confirm, CancellationToken cancellationToken = default)
    {
        if (!confirm)
        {
            return Task.FromResult(AutomationResult.Succeeded("uia", $"dry-run: would set '{functionName}' enabled={enabled}"));
        }

        var window = ResolveWindow();
        // OpenFunctionManager just confirms the "Available Functions:" panel is showing (the
        // checkboxes only exist in the UIA tree while it's open) — the actual search scope
        // must be the full app window, not the narrow panel element it returns. See
        // FunctionManagerDriver.FindFunctionCheckbox for why.
        _ = menuNavigator.OpenFunctionManager(window.Window, TimeSpan.FromSeconds(3))
            ?? throw new InvalidOperationException("Function Manager did not open.");

        var state = functionManagerDriver.SetFunctionEnabled(window.Window, functionName, enabled);
        return Task.FromResult(AutomationResult.Succeeded("uia", state.Enabled, state.ValidationMarkers));
    }

    public Task<AutomationResult> GetFunctionStateAsync(string functionName, CancellationToken cancellationToken = default)
    {
        var window = ResolveWindow();
        _ = menuNavigator.OpenFunctionManager(window.Window, TimeSpan.FromSeconds(3))
            ?? throw new InvalidOperationException("Function Manager did not open.");

        var state = functionManagerDriver.GetFunctionState(window.Window, functionName);
        return Task.FromResult(AutomationResult.Succeeded("uia", state.Enabled, state.ValidationMarkers));
    }

    public Task<AutomationResult> SetIoAssignmentAsync(string functionName, string connectionField, string pinOrResourceId, bool confirm, CancellationToken cancellationToken = default) =>
        Task.FromResult(IoAssignmentDriver.Write(ResolveWindow().Window, functionName, connectionField, pinOrResourceId, confirm));

    public Task<AutomationResult> GetIoAssignmentAsync(string functionName, string connectionField, CancellationToken cancellationToken = default)
    {
        lock (MenuNavigator.InteractionLock)
        {
            try { return Task.FromResult(AutomationResult.Succeeded("uia", IoAssignmentDriver.Read(ResolveWindow().Window, functionName, connectionField))); }
            catch (InvalidOperationException ex) { return Task.FromResult(AutomationResult.Failed("uia", ex.Message)); }
        }
    }

    public Task<AutomationResult> SetFieldValueAsync(string dialogPath, string fieldLabel, string value, bool confirm, CancellationToken cancellationToken = default)
    {
        if (!confirm)
        {
            return Task.FromResult(AutomationResult.Succeeded("uia", $"dry-run: would set '{fieldLabel}' = '{value}'"));
        }

        lock (MenuNavigator.InteractionLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var window = ResolveWindow();
            var scope = ResolveDialogScope(window.Window, dialogPath);
            LiveUi.RequireOffline(window.Window);
            formFieldDriver.TrySetValue(scope, fieldLabel, value, window.Window);
            return Task.FromResult(AutomationResult.Succeeded("uia", formFieldDriver.GetValue(scope, fieldLabel)));
        }
    }

    public Task<AutomationResult> GetFieldValueAsync(string dialogPath, string fieldLabel, CancellationToken cancellationToken = default)
    {
        lock (MenuNavigator.InteractionLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var window = ResolveWindow();
            var scope = ResolveDialogScope(window.Window, dialogPath);
            return Task.FromResult(AutomationResult.Succeeded("uia", formFieldDriver.GetValue(scope, fieldLabel)));
        }
    }

    public Task<AutomationResult> ListValidationErrorsAsync(CancellationToken cancellationToken = default)
    {
        var window = ResolveWindow();
        var markers = functionManagerDriver.ReadValidationMarkers(window.Window);
        return Task.FromResult(AutomationResult.Succeeded("uia", new
        {
            markers, coverage = "current_accessible_text_only", mapValidity = "unknown",
            nextAction = "An empty marker list is not a valid-map verdict. Inspect NSP's Configuration Errors panel and verify all required settings separately."
        }, markers));
    }

    /// <summary>Resolve an exact open dialog title or navigate to the requested page heading.</summary>
    private static AutomationElement ResolveDialogScope(AutomationElement mainWindow, string dialogPath)
    {
        if (string.IsNullOrWhiteSpace(dialogPath) || dialogPath == "MainPageTab") return LiveUi.Scope(mainWindow, "MainPageTab");
        var dialog = mainWindow.FindFirst(TreeScope.Children, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
            new PropertyCondition(AutomationElement.NameProperty, dialogPath)));
        if (dialog is not null) return dialog;
        var leaf = dialogPath.Split([" - ", "/", ">"], StringSplitOptions.RemoveEmptyEntries).Last().Trim();
        PageNavigation.Navigate(mainWindow, leaf, () => PageNavigation.Matches(mainWindow, dialogPath));
        return LiveUi.Scope(mainWindow, "MainPageTab");
    }
}
