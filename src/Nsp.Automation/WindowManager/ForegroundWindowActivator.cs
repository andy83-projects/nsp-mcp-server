using System.Runtime.InteropServices;

namespace Nsp.Automation.WindowManager;

/// <summary>
/// Forces a window to the OS foreground from a background process. Windows deliberately
/// blocks a plain SetForegroundWindow call from a process that didn't last receive user
/// input (the "foreground lock" anti-focus-stealing restriction) — UIA's
/// AutomationElement.SetFocus() alone hits the same wall and can silently no-op. The
/// standard, legitimate workaround is to temporarily attach this thread's input queue to
/// the current foreground thread's, which Windows treats as authorization to activate.
/// Required before any keyboard-shortcut-driven action (SendKeys), since those keystrokes
/// go to whichever window the OS currently considers foreground/focused.
/// </summary>
public static class ForegroundWindowActivator
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attachId, uint attachToId, bool attach);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const int ShowRestore = 9;

    public static bool Activate(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        if (IsIconic(windowHandle))
        {
            ShowWindow(windowHandle, ShowRestore);
        }

        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == windowHandle)
        {
            return true;
        }

        var currentThreadId = GetCurrentThreadId();
        var foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, out _);
        var targetThreadId = GetWindowThreadProcessId(windowHandle, out _);

        var attachedToForeground = foregroundThreadId != currentThreadId && AttachThreadInput(currentThreadId, foregroundThreadId, true);
        var attachedToTarget = targetThreadId != currentThreadId && AttachThreadInput(currentThreadId, targetThreadId, true);

        try
        {
            return SetForegroundWindow(windowHandle);
        }
        finally
        {
            if (attachedToForeground)
            {
                AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }

            if (attachedToTarget)
            {
                AttachThreadInput(currentThreadId, targetThreadId, false);
            }
        }
    }
}
