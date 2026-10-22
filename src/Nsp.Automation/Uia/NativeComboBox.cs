using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace Nsp.Automation.Uia;

public static class NativeComboBox
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Info
    {
        public int Size; public Rect Item, Button; public int State;
        public IntPtr Combo, Edit, List;
    }
    [DllImport("user32.dll")] private static extern bool GetComboBoxInfo(IntPtr hwnd, ref Info info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(
        IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr TextMessage(IntPtr hwnd, uint message, IntPtr wparam, StringBuilder text,
        uint flags, uint timeout, out IntPtr result);

    public static string[] Options(AutomationElement combo)
    {
        var hwnd = new IntPtr(combo.Current.NativeWindowHandle);
        if (SendMessageTimeout(hwnd, 0x146, IntPtr.Zero, IntPtr.Zero, 2, 1500, out var count) == IntPtr.Zero ||
            count.ToInt64() < 0 || count.ToInt64() > 512)
            throw new InvalidOperationException("Cannot read bounded native dropdown options.");
        var result = new List<string>();
        for (var i = 0; i < count.ToInt32(); i++)
        {
            if (SendMessageTimeout(hwnd, 0x149, new IntPtr(i), IntPtr.Zero, 2, 1500, out var length) == IntPtr.Zero ||
                length.ToInt64() < 0 || length.ToInt64() > 4096)
                throw new InvalidOperationException("Invalid native dropdown item length.");
            var text = new StringBuilder(length.ToInt32() + 1);
            if (TextMessage(hwnd, 0x148, new IntPtr(i), text, 2, 1500, out var copied) == IntPtr.Zero || copied.ToInt64() < 0)
                throw new InvalidOperationException("Cannot read native dropdown item.");
            result.Add(text.ToString());
        }
        return result.ToArray();
    }

    public static AutomationElement List(AutomationElement combo)
    {
        var info = new Info { Size = Marshal.SizeOf<Info>() };
        if (!GetComboBoxInfo(new IntPtr(combo.Current.NativeWindowHandle), ref info) || info.List == IntPtr.Zero)
            throw new InvalidOperationException("Cannot identify this dropdown's native list.");
        return AutomationElement.FromHandle(info.List);
    }
}
