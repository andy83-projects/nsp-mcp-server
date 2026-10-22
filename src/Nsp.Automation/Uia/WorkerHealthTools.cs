using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using ModelContextProtocol.Server;
using Nsp.Automation.Core;
using Nsp.Automation.WindowManager;

namespace Nsp.Automation.Uia;

[McpServerToolType]
public sealed class WorkerHealthTools(NspWindowLocator locator, NspAppDriverOptions options)
{
    private delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr result);
    [McpServerTool(Name = "nsp_probe_recovery_state", ReadOnly = true)]
    [Description("Non-mutating recovery diagnostic, available even while recovery is blocked. Default uses fresh native HWND discovery, heartbeat, title and visible owned captioned windows without UIA/COM. includeAutomation=true additionally verifies ECU connection through fresh UIA; this may time out if accessibility is wedged. Native success alone does not prove UIA health or authorize edits. Never clicks, closes, saves or restarts NSP. Supervisor probe deadline is separate from normal operation timeout.")]
    public object Probe(bool includeAutomation = false)
    {
        OperationContext.Checkpoint("Recovery probe: locating native NSP window (no UIA)");
        NativeNspWindowHandle located;
        var unique = true;
        try
        {
            try { located = locator.LocateNative(); }
            catch (AmbiguousNspWindowException) when (!string.IsNullOrWhiteSpace(options.ExpectedOpenFileName))
            { located = locator.LocateNative(options.ExpectedOpenFileName); unique = false; }
        }
        catch (Exception ex) when (ex is NspWindowNotFoundException or AmbiguousNspWindowException)
        { return new { success = false, customerActionRequired = true, message = "Ask the customer to open/check the expected NSP map; its main window is missing or ambiguous.", dialogs = Array.Empty<object>() }; }
        OperationContext.Checkpoint("Recovery probe: native heartbeat");
        if (SendMessageTimeout(located.Handle, 0, IntPtr.Zero, IntPtr.Zero, 3, 2000, out _) == IntPtr.Zero)
            return new { success = false, customerActionRequired = true, nspPid = located.ProcessId, message = "NSP did not respond. Ask the customer to check or restart NSP; no NSP process was terminated.", dialogs = Array.Empty<object>() };
        // A legitimate Open Map can change the filename since worker startup.
        // Bind the unique existing main window; never load/replace a map to recover.
        if (unique) options.ExpectedOpenFileName = null;
        var dialogs = new List<object>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || (GetWindowLong(hwnd, -16) & 0xC00000) != 0xC00000) return true;
            var owner = GetWindow(hwnd, 4); // GW_OWNER; do not enumerate UIA descendants.
            for (var depth = 0; owner != IntPtr.Zero && depth < 16; depth++, owner = GetWindow(owner, 4))
                if (owner == located.Handle)
                {
                    var title = new System.Text.StringBuilder(4096);
                    GetWindowText(hwnd, title, title.Capacity);
                    dialogs.Add(new { title = title.ToString(), nativeHandle = hwnd.ToInt64() });
                    break;
                }
            return true;
        }, IntPtr.Zero);
        if (!IsWindowEnabled(located.Handle) && dialogs.Count == 0)
            dialogs.Add(new { title = "NSP main window is disabled; a modal/unknown blocking window may be present.", nativeHandle = located.Handle.ToInt64() });
        bool? connected = null;
        if (includeAutomation)
        {
            OperationContext.Checkpoint("Recovery probe: fresh UIA offline/dialog verification");
            // No cached app/window scopes after sleep/wake. The supervisor bounds this
            // optional accessibility phase; the basic heartbeat never enters COM.
            LiveUi.ClearScopeCache();
            Console.WriteLine("[RECOVERY PROBE] Creating fresh main UIA element");
            var main = AutomationElement.FromHandle(located.Handle);
            Console.WriteLine("[RECOVERY PROBE] Reading exact Connect/Disconnect toolbar item");
            connected = LiveUi.EcuConnected(main);
            Console.WriteLine("[RECOVERY PROBE] Connection state: " + (connected?.ToString() ?? "unknown"));
            // Dialog detection remains native above. Enumerating the entire main
            // accessibility tree here can block on virtual controls unrelated to
            // recovery. A disabled main window also refuses acknowledgement even
            // when a captionless modal cannot be named by the native scan.
        }
        return new { success = true, nspPid = located.ProcessId, probedAt = DateTimeOffset.UtcNow,
            automationChecked = includeAutomation, appState = new { windowFound = true, openTitle = located.Title,
                hasUnsavedChanges = located.Title.Contains('*'), ecuConnected = connected }, dialogs };
    }
}
