using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Nsp.Automation.Uia;

public static class NativeDialogs
{
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    public static bool IsOpen(AutomationElement dialog) => IsWindow(LiveUi.Handle(dialog));
    public static AutomationElement? AxisDialog(AutomationElement main)
    {
        // Owned NSP dialogs appear in RawView but may be absent from filtered
        // native/proxy window queries. Inspect only immediate siblings, as the
        // independently verified diagnostic probe does.
        var root = main;
        var child = TreeWalker.RawViewWalker.GetFirstChild(root);
        for (var i = 0; child is not null && i < 32; i++)
        {
            if (child.Current.ControlType == ControlType.Window &&
                (child.Current.AutomationId == "TableAxisSetupForm" || child.Current.Name.EndsWith(" : Table Axis Setup", StringComparison.Ordinal)))
            {
                return child;
            }
            child = TreeWalker.RawViewWalker.GetNextSibling(child);
        }
        return null;
    }

    public static AutomationElement OpenAxisDialog(AutomationElement main, AutomationElement grid)
    {
        // Use the same real cell selection and F3 path as verified snapshots.
        // Invoking the custom context-menu command can block UIA in a modal call.
        LiveUi.Click(main, ((GridPattern)grid.GetCurrentPattern(GridPattern.Pattern)).GetItem(0, 0));
        MenuNavigator.SendKeysToWindow(main, "{F3}");
        return WaitForAxisDialog(main);
    }

    public static AutomationElement WaitForAxisDialog(AutomationElement main)
    {
        for (var i = 0; i < 20; i++)
        {
            var dialog = AxisDialog(main);
            if (dialog is not null) return dialog;
            Thread.Sleep(100);
        }
        throw new InvalidOperationException("Axis setup dialog did not appear.");
    }
    public static void WaitClosed(IntPtr handle)
    {
        for (var i = 0; i < 50; i++) { if (!IsWindow(handle)) return; Thread.Sleep(100); }
        throw new InvalidOperationException("Native dialog did not close after its OK action.");
    }
    public static void CancelAxisDialog(AutomationElement dialog)
    {
        if (dialog.Current.ControlType != ControlType.Window || !dialog.Current.Name.EndsWith(" : Table Axis Setup", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to close an unrelated dialog.");
        CloseNative(dialog);
    }
    public static void CancelDialog(AutomationElement dialog, string expectedId)
    {
        if (dialog.Current.AutomationId != expectedId) throw new InvalidOperationException("Refusing to close an unrelated dialog.");
        CloseNative(dialog);
    }
    private static void CloseNative(AutomationElement dialog)
    {
        var handle = LiveUi.Handle(dialog);
        if (handle == IntPtr.Zero) throw new InvalidOperationException("Dialog has no native close target.");
        if (!PostMessage(handle, 0x10, IntPtr.Zero, IntPtr.Zero)) throw new InvalidOperationException("Could not cancel axis setup.");
        for (var i = 0; i < 30; i++) { if (!IsWindow(handle)) return; Thread.Sleep(100); }
        throw new InvalidOperationException("Axis setup remained open after cancellation.");
    }
}
