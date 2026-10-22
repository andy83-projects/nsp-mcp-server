using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Nsp.Automation.WindowManager;

namespace Nsp.Automation.Fallback;

/// <summary>Optional diagnostic capture. Runtime navigation never reads images.</summary>
public static class ScreenCapture
{
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    public static Bitmap CaptureWindow(IntPtr windowHandle)
    {
        using var dpi = new PhysicalDpiScope();
        if (!ForegroundWindowActivator.Activate(windowHandle) || GetForegroundWindow() != windowHandle)
            throw new InvalidOperationException("NSP must be foreground for a diagnostic client-area capture.");
        if (!GetClientRect(windowHandle, out var rect) || rect.Right <= 0 || rect.Bottom <= 0)
            throw new InvalidOperationException("Cannot read NSP client bounds.");
        var origin = new Point(0, 0);
        if (!ClientToScreen(windowHandle, ref origin)) throw new InvalidOperationException("Cannot resolve NSP client origin.");
        // NSP's PrintWindow path rendered at 40% size inside a full-size bitmap
        // on the VM's high-DPI display, even with a compatible GDI DC. Copy the
        // actual foreground client pixels so diagnostic clicks share its coordinates.
        var bitmap = new Bitmap(rect.Right, rect.Bottom, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(origin, Point.Empty, bitmap.Size, CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
}
