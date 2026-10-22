using System.Diagnostics;
using System.Windows.Automation;
using System.Runtime.InteropServices;
using System.Text;

namespace Nsp.Automation.WindowManager;

public sealed record NspWindowHandle(AutomationElement Window, int ProcessId, string Title);
public sealed record NativeNspWindowHandle(IntPtr Handle, int ProcessId, string Title);

public sealed class NspWindowNotFoundException(string message) : Exception(message);

public sealed class AmbiguousNspWindowException(string message) : Exception(message);

/// <summary>
/// Finds the NSP top-level window that has the expected .nexmap file open, and refuses to
/// guess when zero or multiple candidates match. This directly addresses the build log's
/// "avoid the second, older NSP instance" lesson — multiple NSP processes/windows can be
/// open simultaneously, and automation must never silently pick one.
///
/// Because this runs in-process inside the VM (not via host-side screen capture), it talks
/// to real HWNDs/UIA elements directly — the entire noWindowsAvailable/cgWindowNotFound
/// failure class from the manual sessions cannot occur here structurally.
/// </summary>
public sealed class NspWindowLocator(string processNameHint = "NSP")
{
    private delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    public NspWindowHandle Locate(string? expectedOpenFileName = null)
    {
        var native = LocateNative(expectedOpenFileName);
        return new(Nsp.Automation.Uia.LiveUi.FromHandle(native.Handle), native.ProcessId, native.Title);
    }

    /// <summary>Fresh HWND discovery without entering UIA/COM; safe for recovery heartbeat.</summary>
    public NativeNspWindowHandle LocateNative(string? expectedOpenFileName = null)
    {
        // Excludes our own process: a hint like "NSP" also substring-matches this very
        // server's own process name ("Nsp.Mcp.Server"), which otherwise makes the locator
        // find itself as a candidate window — a real bug this caught on first live run.
        var currentProcessId = Environment.ProcessId;

        var candidates = Process.GetProcesses()
            .Where(p => p.Id != currentProcessId && p.ProcessName.Contains(processNameHint, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
        {
            throw new NspWindowNotFoundException(
                $"No running process matched process-name hint '{processNameHint}'. Is NSP open in this session? " +
                "(Update the hint in config once the real NSP.exe process name is confirmed.)");
        }

        var nativeMatches = new List<(IntPtr Handle, int Pid, string Title)>();

        var candidateIds = candidates.Select(p => p.Id).ToHashSet();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (!candidateIds.Contains((int)pid)) return true;
            var buffer = new StringBuilder(4096); GetWindowText(hwnd, buffer, buffer.Capacity);
            var title = buffer.ToString();
            // A dropdown/menu/tooltip can be the first process window in UIA's tree.
            // Enumerate all native windows and identify the actual NSP main caption.
            if (!(title == "Haltech NSP" || title.StartsWith("Haltech NSP -", StringComparison.Ordinal))) return true;
            if (expectedOpenFileName is null ||
                title.Contains(expectedOpenFileName, StringComparison.OrdinalIgnoreCase))
            {
                nativeMatches.Add((hwnd, (int)pid, title));
            }
            return true;
        }, IntPtr.Zero);

        // Keep COM accessibility calls outside native enumeration callbacks.
        var matches = nativeMatches.Select(m => new NativeNspWindowHandle(m.Handle, m.Pid, m.Title)).ToList();
        return matches.Count switch
        {
            0 => throw new NspWindowNotFoundException(
                expectedOpenFileName is null
                    ? "No NSP window found."
                    : $"No NSP window found with '{expectedOpenFileName}' open. Found {candidates.Count} NSP process(es) but none matched by title."),
            1 => matches[0],
            _ => throw new AmbiguousNspWindowException(
                $"{matches.Count} NSP windows matched — refusing to guess. Titles: {string.Join(" | ", matches.Select(m => m.Title))}. " +
                "Close the stale/older instance and retry."),
        };
    }
}
