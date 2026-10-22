using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using Nsp.Automation.Fallback;
using Nsp.Automation.WindowManager;

namespace Nsp.Automation.Uia;

public static class LiveUi
{
    private delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr hwnd, EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    public static bool IsVisible(IntPtr hwnd) => IsWindowVisible(hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
    [StructLayout(LayoutKind.Sequential)] private struct HitPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(HitPoint point);
    private sealed record HandleBox(IntPtr Value);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AutomationElement, HandleBox> ElementHandles = new();
    public static AutomationElement FromHandle(IntPtr handle)
    {
        var element = AutomationElement.FromHandle(handle);
        ElementHandles.GetValue(element, _ => new(handle));
        return element;
    }
    public static IntPtr ParentHandle(IntPtr handle) => GetParent(handle);
    public static System.Windows.Rect NativeBounds(IntPtr handle)
    {
        using var dpi = new PhysicalDpiScope();
        if (!GetWindowRect(handle, out var r)) throw new InvalidOperationException("Cannot read native control bounds.");
        return new(r.Left, r.Top, Math.Max(0, r.Right-r.Left), Math.Max(0, r.Bottom-r.Top));
    }
    public static void SetNativeText(IntPtr handle, string value)
    {
        if (SetTextMessage(handle, 0xC, IntPtr.Zero, value, 2, 2000, out _) == IntPtr.Zero || NativeText(handle) != value)
            throw new InvalidOperationException("Native search text did not respond or failed readback.");
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr SetTextMessage(IntPtr hwnd, uint message, IntPtr wparam, string text, uint flags, uint timeout, out IntPtr result);
    public static IntPtr Handle(AutomationElement e) => ElementHandles.GetValue(e, x => new(new IntPtr(x.Current.NativeWindowHandle))).Value;
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr TextMessage(IntPtr hwnd, uint message, IntPtr count, StringBuilder text, uint flags, uint timeout, out IntPtr result);
    public static string NativeText(IntPtr handle)
    {
        var text = new StringBuilder(65536);
        if (TextMessage(handle, 0xD, new IntPtr(text.Capacity), text, 2, 2000, out var copied) == IntPtr.Zero || copied.ToInt64() >= text.Capacity - 1)
            throw new InvalidOperationException("Native help text did not respond or exceeded the size limit.");
        return text.ToString();
    }
    public sealed record NativeControl(IntPtr Handle, string ClassName, string Text, int Style, bool Visible, System.Windows.Rect Bounds);
    public static IReadOnlyList<NativeControl> NativeChildren(AutomationElement scope)
    {
        var handles = new List<IntPtr>();
        EnumChildWindows(Handle(scope), (h, _) => { handles.Add(h); return handles.Count < 1500; }, IntPtr.Zero);
        return handles.Select(h =>
        {
            var cls = new StringBuilder(256); var text = new StringBuilder(4096);
            GetClassName(h, cls, cls.Capacity); GetWindowText(h, text, text.Capacity); GetWindowRect(h, out var r);
            return new NativeControl(h, cls.ToString(), text.ToString(), GetWindowLong(h, -16), IsWindowVisible(h),
                new System.Windows.Rect(r.Left, r.Top, Math.Max(0, r.Right-r.Left), Math.Max(0, r.Bottom-r.Top)));
        }).ToArray();
    }

    public static IEnumerable<AutomationElement> InputControls(AutomationElement scope) => NativeChildren(scope)
        .Where(c => c.Visible && (c.ClassName.Contains("WindowsForms10.EDIT", StringComparison.OrdinalIgnoreCase) ||
            c.ClassName.Contains("WindowsForms10.COMBOBOX", StringComparison.OrdinalIgnoreCase) ||
            c.ClassName.Contains("WindowsForms10.BUTTON", StringComparison.OrdinalIgnoreCase) || c.ClassName.Contains("msctls_updown32") || c.ClassName.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
            c.ClassName.Equals("ComboBox", StringComparison.OrdinalIgnoreCase) || c.ClassName.Equals("Button", StringComparison.OrdinalIgnoreCase)))
        .Select(c => FromHandle(c.Handle));

    public static IEnumerable<AutomationElement> ButtonControls(AutomationElement scope) => NativeChildren(scope)
        .Where(c => c.Visible && (c.ClassName.Contains("WindowsForms10.BUTTON", StringComparison.OrdinalIgnoreCase) ||
            c.ClassName.Equals("Button", StringComparison.OrdinalIgnoreCase)))
        .Select(c => FromHandle(c.Handle));

    public static AutomationElement? NumericEditor(AutomationElement element)
    {
        if (element.Current.ControlType != ControlType.Spinner) return null;
        return RawChildren(element).SingleOrDefault(e => e.Current.ControlType == ControlType.Edit);
    }

    public static AutomationElement NativeParent(AutomationElement element)
    {
        var parent = GetParent(Handle(element));
        if (parent == IntPtr.Zero) throw new InvalidOperationException("Native control has no parent row.");
        return FromHandle(parent);
    }

    public static string? ContainerId(AutomationElement element)
    {
        var parentHandle = GetParent(Handle(element));
        if (parentHandle == IntPtr.Zero) return null;
        var id = FromHandle(parentHandle).Current.AutomationId;
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    public static string[] NativeContext(AutomationElement element)
    {
        var parent = GetParent(Handle(element));
        if (parent == IntPtr.Zero) return [];
        // Do not enumerate provider children here: a compound field's parent can
        // expose thousands of virtual cells, or block in GetNextSibling.
        return NativeChildren(FromHandle(parent))
            .Where(c => c.Visible && GetParent(c.Handle) == parent &&
                c.ClassName.Contains("WindowsForms10.Static", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Text))
            .Select(c => c.Text).Distinct().ToArray();
    }

    public static List<AutomationElement> Grids(AutomationElement scope)
    {
        var children = NativeChildren(scope).Where(c => c.Visible).ToArray();
        var panels = children.Where(c => c.ClassName.Contains("WindowsForms10.Window")).ToArray();
        var leaves = panels.Where(c => c.Bounds.Width > 500 && c.Bounds.Height > 40 &&
            !panels.Any(other => GetParent(other.Handle) == c.Handle)).ToArray();
        return leaves.Select(c => FromHandle(c.Handle))
            .Where(e => e.TryGetCurrentPattern(GridPattern.Pattern, out var raw) && IsExposedGrid(e, (GridPattern)raw)).ToList();
    }

    private static bool IsExposedGrid(AutomationElement grid, GridPattern pattern)
    {
        if (pattern.Current.RowCount < 1 || pattern.Current.ColumnCount < 1) return false;
        // NSP can retain other native grids behind the current page with WS_VISIBLE
        // and IsOffscreen=false. Native hit testing distinguishes the exposed grid
        // without clicking the background grid or relying on a screenshot.
        var cell = pattern.GetItem(0, 0);
        var rect = cell.Current.BoundingRectangle;
        if (cell.Current.IsOffscreen || rect.IsEmpty) return false;
        using var dpi = new PhysicalDpiScope();
        var hit = WindowFromPoint(new HitPoint { X = (int)(rect.Left + rect.Width / 2), Y = (int)(rect.Top + rect.Height / 2) });
        var handle = Handle(grid);
        var exposed = handle != IntPtr.Zero && (hit == handle || IsChild(handle, hit));
        Console.WriteLine($"[GRID] hwnd {handle}, native hit {hit}, exposed {exposed}");
        return exposed;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int Root, string Id), (IntPtr Handle, AutomationElement Element)> ScopeHandles = new();
    public static void ClearScopeCache() => ScopeHandles.Clear();
    public static IEnumerable<AutomationElement> RawChildren(AutomationElement root)
    {
        var child = TreeWalker.RawViewWalker.GetFirstChild(root);
        for (var i = 0; child is not null && i < 256; i++)
        {
            yield return child;
            child = TreeWalker.RawViewWalker.GetNextSibling(child);
        }
    }
    public static AutomationElement Scope(AutomationElement main, string? id)
    {
        if (string.IsNullOrEmpty(id)) return main;
        if (id == "TableAxisSetupForm" || id.EndsWith("DataGridView", StringComparison.Ordinal))
        {
            if (main.Current.AutomationId == id || (id == "TableAxisSetupForm" && main.Current.Name.EndsWith(" : Table Axis Setup", StringComparison.Ordinal))) return main;
            var raw = RawChildren(main).FirstOrDefault(e => e.Current.AutomationId == id ||
                (id == "TableAxisSetupForm" && e.Current.ControlType == ControlType.Window && e.Current.Name.EndsWith(" : Table Axis Setup", StringComparison.Ordinal)));
            if (raw is not null) return raw;
        }
        var owned = OwnedWindows.Dialogs(Handle(main)).Select(FromHandle)
            .Where(e => id.StartsWith("owned-title:", StringComparison.Ordinal)
                ? e.Current.Name == id[12..] : e.Current.AutomationId == id).ToArray();
        if (owned.Length > 1) throw new InvalidOperationException($"Owned dialog ID '{id}' is ambiguous.");
        if (owned.Length == 1) return owned[0];
        if (id.StartsWith("owned-title:", StringComparison.Ordinal)) throw new InvalidOperationException("Exact owned dialog title not found.");
        var rootHandle = Handle(main);
        var key = (rootHandle.ToInt32(), id);
        // Only the three permanent main-window regions are cached. Dialog handles can
        // be destroyed/reused and must always be resolved afresh by exact ID.
        var permanent = id is "MainPageTab" or "MainTreeView" or "MainToolStrip";
        if (permanent && ScopeHandles.TryGetValue(key, out var cached) && IsWindow(cached.Handle) && IsChild(rootHandle, cached.Handle))
        {
            GetWindowThreadProcessId(rootHandle, out var rootProcess);
            GetWindowThreadProcessId(cached.Handle, out var childProcess);
            if (rootProcess == childProcess) return cached.Element;
            ScopeHandles.TryRemove(key, out _);
        }
        var direct = permanent ? null : main.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.AutomationIdProperty, id));
        if (direct is not null)
        {
            if (permanent && Handle(direct) != IntPtr.Zero) ScopeHandles[key] = (Handle(direct), direct);
            return direct;
        }
        var handles = new List<IntPtr>();
        EnumChildWindows(rootHandle, (h, _) => { handles.Add(h); return handles.Count < 1500; }, IntPtr.Zero);
        IEnumerable<IntPtr> ordered = handles.AsEnumerable().Reverse();
        if (id == "MainPageTab")
        {
            GetWindowRect(rootHandle, out var rootRect);
            ordered = handles.Where(h => GetWindowRect(h, out var r) &&
                r.Right - r.Left > (rootRect.Right - rootRect.Left) / 2 &&
                r.Bottom - r.Top > (rootRect.Bottom - rootRect.Top) / 2);
        }
        if (id == "MainTreeView")
        {
            GetWindowRect(rootHandle, out var r);
            ordered = handles.Where(h => GetWindowRect(h, out var b) && b.Right-b.Left < (r.Right-r.Left)/2 &&
                b.Bottom-b.Top > (r.Bottom-r.Top)/2 && b.Left < r.Left+(r.Right-r.Left)/3);
        }
        if (id.EndsWith("DataGridView", StringComparison.Ordinal))
            ordered = handles.Where(h => GetWindowRect(h, out var r) && r.Right-r.Left > 500 && r.Bottom-r.Top > 60)
                .Where(h => { var cls = new StringBuilder(256); GetClassName(h, cls, cls.Capacity); return cls.ToString().Contains("WindowsForms10.Window"); });
        foreach (var h in ordered)
        {
            var element = FromHandle(h);
            if (element.Current.AutomationId != id) continue;
            if (permanent) ScopeHandles[key] = (h, element);
            return element;
        }
        AutomationElement? found = null;
        foreach (var e in WalkControls(main, 16, 500))
        {
            var elementId = e.Current.AutomationId;
            if (elementId == id) { found = e; break; }
        }
        if (found is null) throw new InvalidOperationException($"UI region '{id}' not found.");
        return found;
    }

    public static AutomationElement Unique(AutomationElement scope, string? id, string? name, string? type = null, bool? enabled = null)
    {
        if (string.IsNullOrEmpty(id) && name is null)
            throw new ArgumentException("An exact AutomationId or Name is required.");
        if (type == "Text" && name is not null)
        {
            var nativeLabels = NativeChildren(scope);
            var labels = nativeLabels.Where(c => c.Visible && c.Text == name)
                .Select(c => FromHandle(c.Handle)).Where(e => e.Current.ControlType == ControlType.Text &&
                    (id is null || e.Current.AutomationId == id) && (enabled is null || e.Current.IsEnabled == enabled)).ToArray();
            if (labels.Length == 1) return labels[0];
            if (labels.Length > 1) throw new InvalidOperationException("Exact native text selector is ambiguous.");
            if (nativeLabels.Count != 0) throw new InvalidOperationException("Exact native text selector was not found in this scope.");
        }
        var inputs = type is "Edit" or "ComboBox" or "CheckBox" or "RadioButton" or "Spinner" or "Button";
        if (!inputs && !string.IsNullOrEmpty(id) && scope.Current.ControlType == ControlType.Window)
        {
            var matchesById = RawChildren(scope).Where(e => e.Current.AutomationId == id).ToArray();
            if (matchesById.Length > 1) throw new InvalidOperationException("Exact ID is ambiguous in this scope.");
            if (matchesById.Length == 1)
            {
                var candidate = matchesById[0];
                if ((name is not null && candidate.Current.Name != name) ||
                    (type is not null && candidate.Current.ControlType.ProgrammaticName != "ControlType." + type) ||
                    (enabled is not null && candidate.Current.IsEnabled != enabled))
                    throw new InvalidOperationException("Exact ID has an unexpected label or control type.");
                return candidate;
            }
        }
        var matches = (type == "Button" ? ButtonControls(scope) : inputs ? InputControls(scope) : WalkControls(scope, 24, 4000)).Where(e =>
            (string.IsNullOrEmpty(id) || e.Current.AutomationId == id) &&
            (name is null || e.Current.Name == name) &&
            (type is null || e.Current.ControlType.ProgrammaticName == "ControlType." + type) &&
            (enabled is null || e.Current.IsEnabled == enabled)).ToList();
        if (matches.Count == 1) return matches[0];
        // Fallback for compound grid-style fields (e.g. Engine Geometry's per-row
        // "textBoxRow1Col1" panes): the AutomationId lives on a labeled container, not on
        // its nameless Edit/ComboBox child — the direct id+type search above can never
        // match those. Only tried when the direct search found nothing and a type was
        // requested, so it can't silently mask a real ambiguity.
        if (matches.Count == 0 && inputs && !string.IsNullOrEmpty(id))
        {
            var containers = WalkControls(scope, 24, 4000).Where(e => e.Current.AutomationId == id).ToArray();
            if (containers.Length > 1) throw new InvalidOperationException($"Container '{id}' is ambiguous; refusing to guess.");
            if (containers.Length == 1)
            {
                var nested = InputControls(containers[0]).Where(e =>
                    e.Current.ControlType.ProgrammaticName == "ControlType." + type && (name is null || e.Current.Name == name)).ToList();
                if (nested.Count == 1) return nested[0];
                if (nested.Count > 1) throw new InvalidOperationException($"Container '{id}' has {nested.Count} matching nested inputs; refusing to guess.");
            }
        }
        throw new InvalidOperationException($"Expected one matching element, found {matches.Count}.");
    }

    public static void Click(AutomationElement main, AutomationElement element)
    {
        if (element.Current.IsOffscreen || !element.Current.IsEnabled)
            throw new InvalidOperationException("Target is hidden or disabled; refusing to click.");
        var rect = element.Current.BoundingRectangle;
        if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) throw new InvalidOperationException("Target has no visible bounds.");
        var native = element;
        for (var depth = 0; Handle(native) == IntPtr.Zero && depth < 32; depth++)
            native = TreeWalker.RawViewWalker.GetParent(native) ?? throw new InvalidOperationException("Target has no native input window.");
        var root = OwnedWindows.Root(Handle(main), Handle(native));
        if (root == Handle(main)) OwnedWindows.DefaultInput(root);
        OwnedWindows.RequireForeground(root);
        NativeClick.ClickAtScreenPoint(new Point((int)(rect.Left + rect.Width / 2), (int)(rect.Top + rect.Height / 2)));
        Thread.Sleep(150);
    }

    public static string? Value(AutomationElement e) =>
        e.TryGetCurrentPattern(ValuePattern.Pattern, out var p) && p is ValuePattern v ? v.Current.Value : null;

    public static void SetValue(AutomationElement e, string value)
    {
        if (!e.TryGetCurrentPattern(ValuePattern.Pattern, out var p) || p is not ValuePattern v || v.Current.IsReadOnly)
            throw new InvalidOperationException("Target does not expose writable ValuePattern.");
        v.SetValue(value);
        if (v.Current.Value != value) throw new InvalidOperationException("Value readback mismatch.");
    }

    public static bool? EcuConnected(AutomationElement main)
    {
        var strip = Scope(main, "MainToolStrip");
        // Connect/Disconnect is a direct ToolStrip item near the beginning. Do
        // not materialize every virtual descendant just to read that one item.
        foreach (var e in RawChildren(strip).Take(64))
        {
            if (e.Current.ControlType != ControlType.Button) continue;
            var name = e.Current.Name;
            if (name == "Connect/Disconnect (currently disconnected)") return false;
            if (name == "Connect/Disconnect (currently connected)") return true;
        }
        return null;
    }

    public static void RequireOffline(AutomationElement main)
    {
        if (EcuConnected(main) != false)
            throw new InvalidOperationException("NSP must visibly report currently disconnected before a write.");
    }

    public static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public static IEnumerable<AutomationElement> Walk(AutomationElement root, int maxDepth = 16, int limit = 500)
        => WalkCore(root, maxDepth, limit, false);

    /// <summary>Find controls without expanding thousands of virtual table cells or document text nodes.</summary>
    public static IEnumerable<AutomationElement> WalkControls(AutomationElement root, int maxDepth = 16, int limit = 500)
        => WalkCore(root, maxDepth, limit, true);

    private static IEnumerable<AutomationElement> WalkCore(AutomationElement root, int maxDepth, int limit, bool controlsOnly)
    {
        var queue = new Queue<(AutomationElement, int)>();
        queue.Enqueue((root, 0));
        var count = 0;
        while (queue.Count > 0 && count++ < limit)
        {
            var (e, depth) = queue.Dequeue();
            yield return e;
            if (depth >= maxDepth) continue;
            if (controlsOnly && depth > 0 && (e.Current.ControlType == ControlType.DataGrid || e.Current.ControlType == ControlType.Document)) continue;
            var child = TreeWalker.RawViewWalker.GetFirstChild(e);
            var siblings = new HashSet<string>();
            while (child is not null && queue.Count + count < limit)
            {
                // A malformed provider can repeat a sibling. Bound the inner traversal,
                // not only the number of nodes yielded by the outer BFS.
                if (!siblings.Add(string.Join(",", child.GetRuntimeId()))) break;
                queue.Enqueue((child, depth + 1));
                child = TreeWalker.RawViewWalker.GetNextSibling(child);
            }
        }
    }
}
