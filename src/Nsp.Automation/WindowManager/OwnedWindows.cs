using System.Runtime.InteropServices;
using System.Text;

namespace Nsp.Automation.WindowManager;

/// <summary>Native, bounded owner-chain discovery; never chooses another process or an unrelated window.</summary>
public static class OwnedWindows
{
    private delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    public static bool BelongsTo(IntPtr main, IntPtr target)
    {
        if (main == IntPtr.Zero || target == IntPtr.Zero) return false;
        GetWindowThreadProcessId(main, out var pid);
        GetWindowThreadProcessId(target, out var targetPid);
        if (pid == 0 || targetPid != pid) return false;
        var root = GetAncestor(target, 2); // GA_ROOT, not GA_ROOTOWNER (which erases dialog identity).
        for (var depth = 0; root != IntPtr.Zero && depth < 16; depth++, root = GetWindow(root, 4))
            if (root == main) return true;
        return false;
    }
    public static IntPtr Root(IntPtr main, IntPtr target)
    {
        if (!BelongsTo(main, target)) throw new InvalidOperationException("Input target is not part of the selected NSP window's owner chain.");
        return GetAncestor(target, 2);
    }
    public static IntPtr[] Dialogs(IntPtr main)
    {
        var result = new List<IntPtr>();
        EnumWindows((h, _) => {
            if (h != main && IsWindowVisible(h) && BelongsTo(main, h))
            {
                var text = new StringBuilder(1024); GetWindowText(h, text, text.Capacity);
                if (text.Length > 0) result.Add(h); // Excludes dropdown/tool-tip implementation windows.
            }
            return true;
        }, IntPtr.Zero);
        if (result.Count > 32) throw new InvalidOperationException("Too many owned NSP windows; refusing ambiguous input.");
        return result.ToArray();
    }
    public static void RequireForeground(IntPtr target)
    {
        if (!IsWindowEnabled(target) || !ForegroundWindowActivator.Activate(target) || GetForegroundWindow() != target)
            throw new InvalidOperationException("The exact NSP target could not be made foreground; no input sent.");
    }
    public static IntPtr DefaultInput(IntPtr main)
    {
        if (Dialogs(main).Length != 0)
            throw new InvalidOperationException("An NSP dialog is open. Inventory nsp_list_process_windows and specify its exact targetScopeId/targetTitle; no main-window input sent.");
        return main;
    }
}
