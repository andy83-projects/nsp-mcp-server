using System.Windows.Automation;

namespace Nsp.Automation.WindowManager;

/// <summary>
/// Re-validates that a located NSP window handle is still alive before trusting it for the
/// next action — windows can disappear/reattach, and NSP itself can lag on redraws
/// independent of the host-capture issues that drove the old remote-control approach.
/// </summary>
public sealed class WindowHealthMonitor(NspWindowLocator locator)
{
    public bool IsAlive(NspWindowHandle handle)
    {
        try
        {
            _ = handle.Window.Current.BoundingRectangle;
            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    public NspWindowHandle EnsureAlive(NspWindowHandle handle, string? expectedOpenFileName)
    {
        // A living HWND does not make its old title/map/dirty state current. Resolve
        // the native identity on every call, including the configured map filter.
        var current = locator.LocateNative(expectedOpenFileName);
        return current.ProcessId == handle.ProcessId &&
            current.Handle == Nsp.Automation.Uia.LiveUi.Handle(handle.Window) && IsAlive(handle)
            ? handle with { Title = current.Title }
            : locator.Locate(expectedOpenFileName);
    }
}
