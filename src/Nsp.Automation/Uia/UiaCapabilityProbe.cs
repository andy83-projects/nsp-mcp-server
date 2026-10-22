using System.Text.Json;
using System.Windows.Automation;
using Nsp.Automation.Fallback;
using Nsp.Automation.WindowManager;

namespace Nsp.Automation.Uia;

public enum UiaSupportLevel
{
    Full,
    Partial,
    None,
}

public sealed record UiaProbeFinding(string RegionName, UiaSupportLevel SupportLevel, string Notes, IReadOnlyList<string> SampleAutomationIds);

public sealed record UiaElementMatch(string Name, string AutomationId, string ControlType, int Depth, IReadOnlyList<string> SupportedPatterns);

/// <summary>
/// Phase 0 diagnostic: walks NSP's UIA tree for the main menu, the F4 Function Manager,
/// and one tuning table, and classifies each as Full/Partial/None. This is a go/no-go
/// gate — nobody has inspected NSP's accessibility tree yet, so this must run before
/// TuningGridUiaDriver is built out for real. Run it on the Windows VM against the real
/// app; write its findings into docs/PHASE0_FINDINGS.md.
/// </summary>
public sealed class UiaCapabilityProbe
{
    public UiaProbeFinding ProbeRegion(AutomationElement root, string regionName, int maxDepth = 6)
    {
        var elements = CollectDescendants(root, maxDepth);

        var withAutomationId = elements.Count(e => !string.IsNullOrEmpty(SafeGet(() => e.Current.AutomationId)));
        var gridItems = elements.Count(e => e.GetSupportedPatterns()
            .Any(p => p == GridItemPattern.Pattern || p == TableItemPattern.Pattern));

        var level = gridItems > 0 || withAutomationId > elements.Count / 2
            ? UiaSupportLevel.Full
            : withAutomationId > 0
                ? UiaSupportLevel.Partial
                : UiaSupportLevel.None;

        var sampleIds = elements
            .Select(e => SafeGet(() => e.Current.AutomationId))
            .Where(id => !string.IsNullOrEmpty(id))
            .Take(20)
            .Select(id => id!)
            .ToList();

        return new UiaProbeFinding(
            regionName,
            level,
            $"{elements.Count} elements walked, {withAutomationId} with AutomationId, {gridItems} exposing Grid/TableItem patterns.",
            sampleIds);
    }

    /// <summary>Walks the tree looking for elements whose Name or AutomationId contains the
    /// given text (case-insensitive) — a quick way to locate a specific element (e.g. "Available
    /// Functions") once you know roughly what it's called, without the sample cap of ProbeRegion.</summary>
    public List<UiaElementMatch> FindElements(AutomationElement root, string textContains, int maxDepth = 20, int limit = 30, string? controlType = null)
    {
        var matches = new List<UiaElementMatch>();
        foreach (var (element, depth) in BoundedControls(root, maxDepth))
        {
            var name = SafeGet(() => element.Current.Name) ?? "";
            var id = SafeGet(() => element.Current.AutomationId) ?? "";
            if (controlType is not null && element.Current.ControlType.ProgrammaticName != "ControlType." + controlType) continue;
            if (!name.Contains(textContains, StringComparison.OrdinalIgnoreCase) && !id.Contains(textContains, StringComparison.OrdinalIgnoreCase)) continue;
            matches.Add(new(name, id, element.Current.ControlType.ProgrammaticName, depth,
                element.GetSupportedPatterns().Select(p => p.ProgrammaticName).ToArray()));
            if (matches.Count >= Math.Clamp(limit, 1, 100)) break;
        }
        return matches;
    }

    /// <summary>
    /// Finds the first element whose Name contains the given text (optionally also matching
    /// a ControlType, e.g. "Hyperlink") and clicks it with a REAL mouse click at its bounding
    /// rectangle's center — general-purpose navigation primitive (e.g. clicking a function's
    /// Hyperlink in the Available Functions panel to jump to its settings page).
    ///
    /// Confirmed live (2026-10-20): UIA's InvokePattern.Invoke() on these Hyperlinks reports
    /// success but does NOT actually trigger navigation — only a real click does. This
    /// activates the window to the OS foreground first (same ForegroundWindowActivator
    /// AttachThreadInput technique as keyboard shortcuts) since a background-process click
    /// may not register reliably otherwise, then clicks via NativeClick.ClickAtScreenPoint
    /// using the element's actual screen coordinates.
    /// </summary>
    public UiaElementMatch? TryClickFirstMatch(AutomationElement root, string textContains, string? controlTypeFilter, int maxDepth = 20)
    {
        UiaElementMatch? result = null;
        Walk(root, 0);
        return result;

        void Walk(AutomationElement element, int depth)
        {
            if (result is not null)
            {
                return;
            }

            var name = SafeGet(() => element.Current.Name) ?? "";
            var controlTypeName = element.Current.ControlType.ProgrammaticName;

            var controlTypeMatches = controlTypeFilter is null ||
                controlTypeName.Contains(controlTypeFilter, StringComparison.OrdinalIgnoreCase);

            if (controlTypeMatches && name.Contains(textContains, StringComparison.OrdinalIgnoreCase))
            {
                var rect = element.Current.BoundingRectangle;
                if (!rect.IsEmpty)
                {
                    var hwnd = new IntPtr(root.Current.NativeWindowHandle);
                    ForegroundWindowActivator.Activate(hwnd);
                    Thread.Sleep(150);

                    var center = new System.Drawing.Point(
                        (int)(rect.Left + rect.Width / 2),
                        (int)(rect.Top + rect.Height / 2));
                    NativeClick.ClickAtScreenPoint(center);

                    var patterns = element.GetSupportedPatterns().Select(p => p.ProgrammaticName).ToList();
                    result = new UiaElementMatch(name, SafeGet(() => element.Current.AutomationId) ?? "", controlTypeName, depth, patterns);
                    return;
                }
            }

            if (depth >= maxDepth)
            {
                return;
            }

            AutomationElement? child;
            try
            {
                child = TreeWalker.RawViewWalker.GetFirstChild(element);
            }
            catch (ElementNotAvailableException)
            {
                return;
            }

            while (child is not null && result is null)
            {
                Walk(child, depth + 1);
                try
                {
                    child = TreeWalker.RawViewWalker.GetNextSibling(child);
                }
                catch (ElementNotAvailableException)
                {
                    break;
                }
            }
        }
    }

    public string ToJson(IEnumerable<UiaProbeFinding> findings) =>
        JsonSerializer.Serialize(findings, new JsonSerializerOptions { WriteIndented = true });

    private static List<AutomationElement> CollectDescendants(AutomationElement root, int maxDepth) =>
        BoundedControls(root, maxDepth).Select(e => e.Element).ToList();

    private static IEnumerable<(AutomationElement Element, int Depth)> BoundedControls(AutomationElement root, int maxDepth)
    {
        var queue = new Queue<(AutomationElement Element, int Depth)>();
        var seen = new HashSet<string>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        queue.Enqueue((root, 0));
        var visited = 0;
        while (queue.Count > 0 && visited < 500 && watch.Elapsed < TimeSpan.FromSeconds(8))
        {
            Nsp.Automation.Core.OperationContext.Checkpoint("Bounded UIA diagnostic search");
            var (element, depth) = queue.Dequeue();
            if (!seen.Add(string.Join(".", element.GetRuntimeId()))) continue;
            visited++;
            yield return (element, depth);
            if (depth >= Math.Clamp(maxDepth, 0, 24) || element.Current.ControlType == ControlType.DataGrid ||
                element.Current.ControlType == ControlType.Document) continue;
            foreach (var child in LiveUi.RawChildren(element))
            {
                if (visited + queue.Count >= 500 || watch.Elapsed >= TimeSpan.FromSeconds(8)) break;
                queue.Enqueue((child, depth + 1));
            }
        }
    }

    private static string? SafeGet(Func<string> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return null;
        }
    }
}
