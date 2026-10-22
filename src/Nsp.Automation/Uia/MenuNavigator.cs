using System.Windows.Automation;
using System.Windows.Forms;
using Nsp.Automation.Fallback;
using Nsp.Automation.WindowManager;

namespace Nsp.Automation.Uia;

/// <summary>
/// Generic UIA find/invoke primitives shared by every higher-level driver. Exact menu
/// item names/AutomationIds for NSP are unknown until the Phase 0 probe runs — this class
/// intentionally takes names as parameters rather than hardcoding NSP's menu structure.
/// </summary>
public sealed class MenuNavigator
{
    /// <summary>
    /// System.Windows.Forms.SendKeys is not thread-safe — confirmed live (2026-10-20) that
    /// two overlapping calls (an earlier request still running server-side after its client
    /// had already given up on a slow HTTP response) corrupted SendKeys' internal queue and
    /// crashed with an InvalidOperationException deep inside SendKeys.Send itself. All
    /// NSP keyboard interaction is serialized through this lock — the app only has one
    /// keyboard/mouse focus at a time regardless, so there is no legitimate reason for two
    /// automation actions to run concurrently against it.
    /// </summary>
    public static readonly object InteractionLock = new();
    private static object SendKeysLock => InteractionLock;

    public static AutomationElement EnsureNavigationTree(AutomationElement main)
    {
        var tree = LiveUi.Scope(main, "MainTreeView");
        if (LiveUi.IsVisible(LiveUi.Handle(tree))) return tree;
        Nsp.Automation.Core.OperationContext.Checkpoint("Showing hidden navigation tree");
        var toolbar = LiveUi.Scope(main, "MainToolStrip");
        var button = toolbar.FindFirst(TreeScope.Children, new PropertyCondition(
            AutomationElement.NameProperty, "Navigation Tree (show or hide by pressing '~')"));
        if (button is null || !button.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
            throw new InvalidOperationException("Navigation tree is hidden and its exact toolbar command is unavailable.");
        ((InvokePattern)pattern).Invoke();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(3))
        {
            if (LiveUi.IsVisible(LiveUi.Handle(tree))) return tree;
            Thread.Sleep(50);
        }
        throw new InvalidOperationException("Navigation tree did not become visible after its toolbar command.");
    }

    public AutomationElement? FindByName(AutomationElement root, string name, TreeScope scope = TreeScope.Descendants) =>
        root.FindFirst(scope, new PropertyCondition(AutomationElement.NameProperty, name));

    public AutomationElement? FindByAutomationId(AutomationElement root, string automationId, TreeScope scope = TreeScope.Descendants) =>
        root.FindFirst(scope, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));

    public bool Invoke(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern) && pattern is InvokePattern invoke)
        {
            invoke.Invoke();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Opens the F4 Function Manager via keyboard shortcut — the proven manual route from
    /// the build logs. Confirmed live (2026-10-20) that in this NSP version "Function
    /// Manager" is NOT a separate popup window — F4 shows an in-place "Available Functions:"
    /// search/filter panel within the same main window (a Text label + Edit + ComboBox +
    /// Pane, found ~depth 9 under MainForm). Detection therefore polls for that label's text,
    /// not for a new ControlType.Window.
    /// </summary>
    public AutomationElement? OpenFunctionManager(AutomationElement appWindow, TimeSpan timeout)
    {
        var existing = AvailableFunctionsPage(appWindow);
        if (existing is not null) return existing;
        Nsp.Automation.Core.OperationContext.Checkpoint("Posting Function Manager shortcut to native NSP window");
        var handle = LiveUi.Handle(appWindow);
        OwnedWindows.DefaultInput(handle);
        OwnedWindows.RequireForeground(handle);
        InputSynthesizer.ReleaseAllModifierKeys();
        if (!PostMessage(handle, 0x100, new IntPtr(0x73), new IntPtr(1)) ||
            !PostMessage(handle, 0x101, new IntPtr(0x73), new IntPtr(unchecked((int)0xC0000001))))
            throw new InvalidOperationException("Could not post the Function Manager shortcut.");
        return WaitForAvailableFunctionsPanel(appWindow, timeout);
    }

    /// <summary>
    /// Activates appWindow (the real AttachThreadInput-based activation, not just UIA's
    /// SetFocus() — see ForegroundWindowActivator for why that alone silently no-ops from a
    /// background process) and sends the given SendKeys-format key sequence to it. Generic
    /// primitive for any of NSP's keyboard shortcuts (F1 help, F3 table axes, D cycle
    /// display, etc.), not just F4.
    /// </summary>
    public static void SendKeysToWindow(AutomationElement appWindow, string keys)
    {
        lock (SendKeysLock)
        {
            Nsp.Automation.Core.OperationContext.Checkpoint("Sending native keyboard input");
            var handle = LiveUi.Handle(appWindow);
            OwnedWindows.DefaultInput(handle);
            OwnedWindows.RequireForeground(handle);
            Thread.Sleep(150);
            InputSynthesizer.ReleaseAllModifierKeys();
            Thread.Sleep(50);
            SendKeys.SendWait(keys);
        }
    }

    public static bool NavigateToFunctionWiringPage(AutomationElement appWindow, string functionName, int maxExtraSteps = 2)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
        lock (InteractionLock)
        {
            try
            {
                PageNavigation.Navigate(appWindow, functionName + " Wiring", () => IsOnCorrectWiringPage(appWindow, functionName));
            }
            catch (InvalidOperationException)
            {
                // Output-style functions (fans, etc.) have a separate "<Function> Wiring"
                // sub-page, but input/sensor-style functions (switches, etc.) commonly don't —
                // their Connections box lives directly on the function's own page. Confirmed
                // live 2026-10-21: searching "Clutch Switch Wiring" returns zero navigation
                // tree matches, while "Clutch Switch" alone lands on a page that has its own
                // ioPortLabel connection control. Retry against the bare function name before
                // giving up.
                PageNavigation.Navigate(appWindow, functionName, () => IsOnCorrectWiringPage(appWindow, functionName));
            }
            return true;
        }
    }

    /// <summary>Confirms both that a Connections box exists AND that it belongs to the
    /// requested function — checks the page's own heading (a Text element whose Name is e.g.
    /// "Engine Functions - Thermo Fan 1 - Wiring", or just "Sensors - Clutch Switch" for
    /// input-style functions with no separate Wiring sub-page) against a normalised
    /// (letters/digits only, lowercased) comparison, since NSP's search-visible function name
    /// ("Thermofan 1") and its displayed heading ("Thermo Fan 1") differ only in
    /// spacing/casing.</summary>
    internal static bool IsOnCorrectWiringPage(AutomationElement appWindow, string functionName)
    {
        var page = LiveUi.Scope(appWindow, "MainPageTab");
        var normalizedFunctionName = Normalize(functionName);
        if (!PageNavigation.Identities(page).Any(name =>
            Normalize(name).EndsWith(normalizedFunctionName + "wiring", StringComparison.Ordinal) ||
            Normalize(name).EndsWith(normalizedFunctionName, StringComparison.Ordinal))) return false;
        return LiveUi.NativeChildren(page).Where(c => c.Visible && c.ClassName.Contains("WindowsForms10.Static", StringComparison.OrdinalIgnoreCase))
            .Any(c => AutomationElement.FromHandle(c.Handle).Current.AutomationId == "ioPortLabel") ||
            LiveUi.NativeChildren(page).Any(c => c.Visible && c.ClassName.Contains("WindowsForms10.Button", StringComparison.OrdinalIgnoreCase) && c.Text == "Assign");
    }

    private static void Log(string message) =>
        Console.WriteLine($"[NAV {DateTime.UtcNow:HH:mm:ss.fff}] {message}");

    private static string Normalize(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);

    private static AutomationElement? AvailableFunctionsPage(AutomationElement main)
    {
        var page = LiveUi.Scope(main, "MainPageTab");
        return LiveUi.NativeChildren(page).Any(c => c.Visible && c.Text == "Available Functions:" &&
            c.ClassName.Contains("WindowsForms10.Static", StringComparison.OrdinalIgnoreCase)) ? page : null;
    }
    private static AutomationElement? WaitForAvailableFunctionsPanel(AutomationElement appWindow, TimeSpan timeout)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            Nsp.Automation.Core.OperationContext.Checkpoint("Waiting for native Available Functions page");
            var page = AvailableFunctionsPage(appWindow);
            if (page is not null) return page;
            Thread.Sleep(100);
        }
        return null;
    }
}
