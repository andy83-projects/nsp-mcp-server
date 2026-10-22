using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Nsp.Automation.Fallback;

/// <summary>UIA rectangles are physical pixels. Keep native coordinate APIs in that same space.</summary>
public sealed class PhysicalDpiScope : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    private readonly IntPtr previous;
    public static void InitializeProcess() => SetProcessDpiAwarenessContext(new IntPtr(-4));
    public PhysicalDpiScope()
    {
        previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        if (previous == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enter physical coordinate space.");
    }
    public void Dispose() => SetThreadDpiAwarenessContext(previous);
}
