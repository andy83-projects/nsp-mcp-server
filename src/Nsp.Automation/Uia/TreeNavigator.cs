using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using Nsp.Automation.Fallback;
using Nsp.Automation.WindowManager;

namespace Nsp.Automation.Uia;

public static class TreeNavigator
{
    private delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr hwnd, EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam,
        uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    public static void SelectGridCell(AutomationElement main, AutomationElement grid, AutomationElement cell)
    {
        var item = ((GridItemPattern)cell.GetCurrentPattern(GridItemPattern.Pattern)).Current;
        if (!System.Windows.Automation.Automation.Compare(item.ContainingGrid, grid))
            throw new InvalidOperationException("Requested cell does not belong to the verified grid.");
        var mainHandle = LiveUi.Handle(main);
        OwnedWindows.DefaultInput(mainHandle);
        OwnedWindows.RequireForeground(mainHandle);
        InputSynthesizer.ReleaseAllModifierKeys();
        InputSynthesizer.Escape();
        if (LiveUi.CellIsExposed(grid, cell))
        {
            LiveUi.Click(main, cell);
            LiveUi.RequireFocusWithin(grid);
            return;
        }
        // NSP can report IsOffscreen=false and full logical cell bounds behind a
        // clipped parent/sidebar. Never click those bounds and type into the old
        // selection. Anchor at an actually exposed cell, then use native arrows.
        var pattern = (GridPattern)grid.GetCurrentPattern(GridPattern.Pattern);
        var rows = pattern.Current.RowCount; var columns = pattern.Current.ColumnCount;
        if (rows < 1 || columns < 1 || (long)rows * columns > 10000)
            throw new InvalidOperationException("Grid exceeds selection bounds; no value input sent.");
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < columns; c++)
            {
                Nsp.Automation.Core.OperationContext.Checkpoint("Finding an exposed grid selection anchor");
                var anchor = pattern.GetItem(r, c);
                if (!LiveUi.CellIsExposed(grid, anchor)) continue;
                var keys = Nsp.Automation.Core.GridKeyboardNavigation.Keys(r, c, item.Row, item.Column, rows, columns);
                LiveUi.Click(main, anchor);
                LiveUi.RequireFocusWithin(grid);
                InputSynthesizer.Escape(); // collapse any retained multi-cell selection
                MenuNavigator.SendKeysToWindow(main, keys);
                LiveUi.RequireFocusWithin(grid);
                return;
            }
        throw new InvalidOperationException("No exposed native grid cell is available as a selection anchor; no value input sent.");
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, AutomationElement> Trees = new();
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
    public static AutomationElement Tree(AutomationElement main)
    {
        var root = LiveUi.Handle(main);
        if (Trees.TryGetValue(root, out var cached) && IsWindow(LiveUi.Handle(cached)) && IsChild(root, LiveUi.Handle(cached))) return cached;
        Console.WriteLine("[TREE] locating region");
        var scope = LiveUi.Scope(main, "MainTreeView");
        var handles = new List<IntPtr>();
        EnumChildWindows(LiveUi.Handle(scope), (h, _) => { handles.Add(h); return handles.Count < 256; }, IntPtr.Zero);
        string ClassName(IntPtr h) { var text = new StringBuilder(256); GetClassName(h, text, text.Capacity); return text.ToString(); }
        // NSP's custom tree owns two native Static edge labels. This distinguishes it
        // from its search/help containers without traversing virtual UIA descendants.
        var matches = handles.Where(h => ClassName(h).Contains("WindowsForms10.Window") &&
            handles.Count(c => GetParent(c) == h && ClassName(c).Contains("WindowsForms10.Static")) == 2).ToList();
        if (matches.Count != 1) throw new InvalidOperationException($"Expected one native NSP navigation tree, found {matches.Count}.");
        Console.WriteLine($"[TREE] resolved hwnd {matches[0]}");
        return Trees[root] = LiveUi.FromHandle(matches[0]);
    }

    public static void Step(AutomationElement main, bool forward)
    {
        Nsp.Automation.Core.OperationContext.Checkpoint("Navigating filtered tree");
        var tree = Tree(main);
        var hwnd = LiveUi.Handle(tree);
        // Address the tree itself. Alt+Arrow goes through NSP's global command route,
        // whereas the custom tree handles ordinary arrows and skips filtered nodes.
        var key = new IntPtr(forward ? 0x28 : 0x26);
        Console.WriteLine($"[TREE] sending arrow to {hwnd}");
        if (SendMessageTimeout(hwnd, 0x100, key, new IntPtr(1), 2, 2000, out _) == IntPtr.Zero)
            throw new InvalidOperationException("Navigation tree did not respond to key-down.");
        SendMessageTimeout(hwnd, 0x101, key, new IntPtr(unchecked((int)0xC0000001)), 2, 2000, out _);
        Thread.Sleep(150);
        Console.WriteLine("[TREE] arrow sent");
    }

    public static void SeedSelection(AutomationElement main)
    {
        Nsp.Automation.Core.OperationContext.Checkpoint("Navigating filtered tree");
        var tree = Tree(main);
        var hwnd = LiveUi.Handle(tree);
        // Reset the viewport before selecting its first row. Filtering can preserve
        // a scroll offset as well as the old selection.
        SendMessageTimeout(hwnd, 0x115, new IntPtr(6), IntPtr.Zero, 2, 2000, out _); // SB_TOP
        SendMessageTimeout(hwnd, 0x114, new IntPtr(6), IntPtr.Zero, 2, 2000, out _); // SB_LEFT
        SendMessageTimeout(hwnd, 0x20A, new IntPtr(32760 << 16), IntPtr.Zero, 2, 2000, out _);
        // Search can leave the custom control with no selected TreeNode, in which case
        // every arrow is a no-op. Seed the first row using a control-local anchor
        // derived from the live accessibility bounds, without screenshots/OCR. The
        // requested destination is independently verified after each subsequent step.
        if (!ForegroundWindowActivator.Activate(LiveUi.Handle(main)))
            throw new InvalidOperationException("NSP could not be activated for the tree reset.");
        InputSynthesizer.ReleaseAllModifierKeys();
        var bounds = LiveUi.NativeBounds(hwnd);
        // Tree() resolves the RESULTS control, not MainTreeView. The search and
        // F4 button are siblings outside it. Searching within it for an Edit always
        // fails; Top+100 then selects an arbitrary third/fourth row instead of row 1.
        // The first row starts immediately below the two-pixel native edge label.
        NativeClick.ClickAtScreenPoint(new System.Drawing.Point((int)bounds.Left + (int)Math.Min(100, bounds.Width / 2), (int)bounds.Top + 12));
        // Deliver a bounded synchronous selection to the resolved custom control.
        // A delivered mouse_event pair alone did not seed selection in live tests.
        // These are client-relative coordinates, never screenshot coordinates.
        var point = new IntPtr((12 << 16) | (int)Math.Min(100, bounds.Width / 2));
        if (SendMessageTimeout(hwnd, 0x200, IntPtr.Zero, point, 2, 2000, out _) == IntPtr.Zero ||
            SendMessageTimeout(hwnd, 0x201, new IntPtr(1), point, 2, 2000, out _) == IntPtr.Zero ||
            SendMessageTimeout(hwnd, 0x202, IntPtr.Zero, point, 2, 2000, out _) == IntPtr.Zero)
            throw new InvalidOperationException("Navigation tree did not respond to its selection reset.");
        Thread.Sleep(200);
    }
}
