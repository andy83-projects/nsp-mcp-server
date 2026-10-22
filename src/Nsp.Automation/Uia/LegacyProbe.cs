using System.Runtime.InteropServices;
using System.Text;
using Accessibility;
using System.Windows.Automation;

namespace Nsp.Automation.Uia;

public static class LegacyProbe
{
    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint objectId, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible);
    [DllImport("oleacc.dll")] private static extern int AccessibleChildren(IAccessible container, int start, int count,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] object[] children, out int obtained);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr hwnd, EnumProc callback, IntPtr data);
    private delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);

    public static object Inspect(AutomationElement main, string scopeId, int limit)
    {
        var scope = LiveUi.Scope(main, scopeId);
        var windows = new List<object>();
        var rootHandle = new IntPtr(scope.Current.NativeWindowHandle);
        var handles = new List<IntPtr> { rootHandle };
        EnumChildWindows(rootHandle, (h, _) => { if (handles.Count < limit) handles.Add(h); return handles.Count < limit; }, IntPtr.Zero);
        foreach (var h in handles)
        {
            var cls = new StringBuilder(512); var title = new StringBuilder(512);
            GetClassName(h, cls, cls.Capacity); GetWindowText(h, title, title.Capacity);
            windows.Add(new { hwnd = h.ToInt64(), className = cls.ToString(), title = title.ToString() });
        }
        var nodes = new List<object>();
        var iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
        var hr = AccessibleObjectFromWindow(rootHandle, 0xFFFFFFFC, ref iid, out var root);
        if (hr >= 0 && root is not null)
        {
            void Visit(IAccessible acc, object child, string path, int depth)
            {
                if (nodes.Count >= limit || depth > 16) return;
                try
                {
                    var count = child is int id && id == 0 ? acc.accChildCount : 0;
                    nodes.Add(new { path, name = acc.get_accName(child), value = acc.get_accValue(child),
                        role = acc.get_accRole(child), state = acc.get_accState(child), action = acc.get_accDefaultAction(child), children = count });
                    if (count <= 0) return;
                    var children = new object[Math.Min(count, limit - nodes.Count)];
                    if (AccessibleChildren(acc, 0, children.Length, children, out var obtained) < 0) return;
                    for (var i = 0; i < obtained; i++)
                    {
                        if (children[i] is IAccessible next) Visit(next, 0, path + "/" + i, depth + 1);
                        else if (children[i] is int childId) Visit(acc, childId, path + "/" + childId, depth + 1);
                    }
                }
                catch (COMException ex) { nodes.Add(new { path, error = ex.Message }); }
            }
            Visit(root, 0, "root", 0);
        }
        return new { hr, windows, nodes };
    }
}
