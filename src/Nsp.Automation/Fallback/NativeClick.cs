using System.Drawing;
using System.Runtime.InteropServices;

namespace Nsp.Automation.Fallback;

/// <summary>
/// Minimal mouse double-click simulation, converting a window's client-coordinate point to
/// screen coordinates before moving/clicking — needed to focus tuning-grid cells before
/// InputSynthesizer's keyboard-only sequence takes over.
/// </summary>
public static class NativeClick
{
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointStruct point);
    [DllImport("user32.dll")] private static extern bool GetPhysicalCursorPos(out PointStruct point);
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(PointStruct point);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    public static object Diagnostics()
    {
        GetCursorPos(out var logical); GetPhysicalCursorPos(out var physical);
        object Window(IntPtr hwnd) { var c=new System.Text.StringBuilder(256);var t=new System.Text.StringBuilder(2048);GetClassName(hwnd,c,c.Capacity);GetWindowText(hwnd,t,t.Capacity);return new { hwnd=hwnd.ToInt64(),className=c.ToString(),title=t.ToString() }; }
        return new { awareness = GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()),
            logical = new { x = logical.X, y = logical.Y }, physical = new { x = physical.X, y = physical.Y },
            underCursor = Window(WindowFromPoint(physical)), foreground = Window(GetForegroundWindow()),
            screenWidth = GetSystemMetrics(0), screenHeight = GetSystemMetrics(1), session = System.Diagnostics.Process.GetCurrentProcess().SessionId };
    }
    [StructLayout(LayoutKind.Sequential)] private struct ClientRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out ClientRect rect);
    private static void RequireClientPoint(IntPtr hwnd, Point point)
    {
        if (!GetClientRect(hwnd, out var r) || point.X < 0 || point.Y < 0 || point.X >= r.Right || point.Y >= r.Bottom)
            throw new InvalidOperationException("Click lies outside the exact target's client area; capture that same target again.");
    }
    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr windowHandle, ref PointStruct point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, int dx, int dy, int data, IntPtr extraInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointStruct
    {
        public int X;
        public int Y;
    }

    private const uint MouseEventFLeftDown = 0x0002;
    private const uint MouseEventFLeftUp = 0x0004;

    public static void DoubleClick(IntPtr windowHandle, Point clientPoint, int clickGapMs = 80)
    {
        using var dpi = new PhysicalDpiScope();
        RequireClientPoint(windowHandle, clientPoint);
        var screenPoint = new PointStruct { X = clientPoint.X, Y = clientPoint.Y };
        if (!ClientToScreen(windowHandle, ref screenPoint)) throw new InvalidOperationException("Cannot resolve the target's client origin.");
        SetCursorPos(screenPoint.X, screenPoint.Y);

        Click();
        Thread.Sleep(clickGapMs);
        Click();

        static void Click()
        {
            mouse_event(MouseEventFLeftDown, 0, 0, 0, IntPtr.Zero);
            mouse_event(MouseEventFLeftUp, 0, 0, 0, IntPtr.Zero);
        }
    }

    /// <summary>
    /// Single click at an already-absolute screen coordinate (e.g. an AutomationElement's
    /// Current.BoundingRectangle center) — no client-to-screen conversion. Confirmed live
    /// (2026-10-20) that UIA's InvokePattern.Invoke() on NSP's "jump to this function's
    /// settings page" Hyperlinks reports success but does not actually trigger navigation —
    /// a real click is required.
    /// </summary>
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    private static long LastClick;
    private static Point LastPoint;
    private static readonly object ClickLock = new();
    /// <summary>Single click at a window-client-relative point (e.g. a pixel coordinate read
    /// directly off a ScreenCapture.CaptureWindow screenshot, which is itself a client-rect
    /// capture) — converts to screen coordinates first, unlike ClickAtScreenPoint.</summary>
    public static void Click(IntPtr windowHandle, Point clientPoint)
    {
        using var dpi = new PhysicalDpiScope();
        RequireClientPoint(windowHandle, clientPoint);
        var screenPoint = new PointStruct { X = clientPoint.X, Y = clientPoint.Y };
        if (!ClientToScreen(windowHandle, ref screenPoint)) throw new InvalidOperationException("Cannot resolve the target's client origin.");
        ClickAtScreenPoint(new Point(screenPoint.X, screenPoint.Y));
    }

    public static void ClickAtScreenPoint(Point screenPoint)
    {
        lock (ClickLock)
        {
            var elapsed = Environment.TickCount64 - LastClick;
            if (Math.Abs(LastPoint.X-screenPoint.X) < 5 && Math.Abs(LastPoint.Y-screenPoint.Y) < 5 && elapsed <= GetDoubleClickTime())
                Thread.Sleep((int)(GetDoubleClickTime() - elapsed + 30));
            SingleClick(screenPoint);
            LastPoint = screenPoint; LastClick = Environment.TickCount64;
        }
    }
    private static void SingleClick(Point screenPoint)
    {
        using var dpi = new PhysicalDpiScope();
        if (!SetCursorPos(screenPoint.X, screenPoint.Y)) throw new InvalidOperationException("Could not position the pointer.");
        GetPhysicalCursorPos(out var position);
        Console.WriteLine($"[CLICK] requested {screenPoint.X},{screenPoint.Y}; physical {position.X},{position.Y}");
        if (position.X != screenPoint.X || position.Y != screenPoint.Y)
            throw new InvalidOperationException("Physical cursor position disagrees with the requested live UIA target; no click sent.");
        mouse_event(MouseEventFLeftDown, 0, 0, 0, IntPtr.Zero);
        mouse_event(MouseEventFLeftUp, 0, 0, 0, IntPtr.Zero);
    }
}
