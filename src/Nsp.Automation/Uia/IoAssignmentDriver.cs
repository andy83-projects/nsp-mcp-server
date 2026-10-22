using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using System.Windows.Automation;
using Nsp.Automation.Core;

namespace Nsp.Automation.Uia;

public sealed record IoAssignmentSnapshot([property: JsonPropertyName("port")] string Port, [property: JsonPropertyName("wire")] string? Wire);

public static class IoAssignmentDriver
{
    public static IoAssignmentSnapshot Read(AutomationElement main, string functionName, string field)
    {
        if (!MenuNavigator.IsOnCorrectWiringPage(main, functionName) &&
            !MenuNavigator.NavigateToFunctionWiringPage(main, functionName))
            throw new InvalidOperationException($"Could not verify {functionName}'s Wiring page.");
        return ReadCurrent(main, field);
    }

    private static AutomationElement ConnectionRow(AutomationElement main, string field)
    {
        if (string.IsNullOrWhiteSpace(field)) throw new ArgumentException("An exact connection field is required.");
        var labels = LiveUi.NativeChildren(LiveUi.Scope(main, "MainPageTab"))
            .Where(c => c.Visible && c.ClassName.Contains("WindowsForms10.Static", StringComparison.OrdinalIgnoreCase) &&
                LiveUi.Normalize(c.Text) == LiveUi.Normalize(field)).ToArray();
        if (labels.Length != 1) throw new InvalidOperationException($"Expected one exact connection row '{field}', found {labels.Length}.");
        var row = LiveUi.NativeParent(AutomationElement.FromHandle(labels[0].Handle));
        var buttons = LiveUi.NativeChildren(row).Where(c => c.Visible && c.ClassName.Contains("WindowsForms10.Button", StringComparison.OrdinalIgnoreCase) && c.Text == "Assign").ToArray();
        if (buttons.Length != 1) throw new InvalidOperationException("Connection label does not identify one native assignment row.");
        return row;
    }

    private static IoAssignmentSnapshot ReadCurrent(AutomationElement main, string field)
    {
        var row = ConnectionRow(main, field);
        var labels = LiveUi.NativeChildren(row).Where(c => c.Visible && c.ClassName.Contains("WindowsForms10.Static", StringComparison.OrdinalIgnoreCase)).ToArray();
        // Generic Condition input rows omit ioPortLabel. Read exact native resource
        // captions in the verified row; never take the first connection on a page.
        var parsed = NativeIoCaptions.Parse(labels.Select(c => c.Text));
        if (parsed is not null) return new(parsed.Port, parsed.Wire);
        var legacy = labels.Select(c => AutomationElement.FromHandle(c.Handle))
            .Where(e => e.Current.AutomationId == "ioPortLabel").ToArray();
        if (legacy.Length == 1 && !string.IsNullOrWhiteSpace(legacy[0].Current.Name))
            return new(legacy[0].Current.Name, labels.Select(c => c.Text).SingleOrDefault(t => Regex.IsMatch(t, @",\s*[ACE][1-9][0-9]?$", RegexOptions.IgnoreCase)));
        throw new InvalidOperationException("Connection resource cannot be read unambiguously; no assignment attempted.");
    }

    public static bool Matches(IoAssignmentSnapshot value, string requested)
    {
        var id = requested.Trim().ToUpperInvariant();
        return value.Port.Equals(id, StringComparison.OrdinalIgnoreCase) ||
            value.Port.Replace("25A-", "", StringComparison.OrdinalIgnoreCase).Equals(id, StringComparison.OrdinalIgnoreCase) ||
            (value.Wire?.Split(',').Last().Trim().Equals(id, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool ResourceCaptionMatches(string caption, string target) =>
        caption.Equals(target.Replace("25A-", ""), StringComparison.OrdinalIgnoreCase) ||
        caption.Split(',').Last().Trim().Equals(target, StringComparison.OrdinalIgnoreCase);

    private static void RevealResourceRow(AutomationElement dialog, AutomationElement row, AutomationElement label)
    {
        var parent = LiveUi.NativeParent(row);
        if (!parent.TryGetCurrentPattern(ScrollPattern.Pattern, out var raw))
            return; // NativeClick still independently refuses an occluded target.
        var scroll = (ScrollPattern)raw;
        for (var i = 0; i < 32; i++)
        {
            var viewport = LiveUi.NativeBounds(LiveUi.Handle(parent));
            var bounds = LiveUi.NativeBounds(LiveUi.Handle(label));
            if (bounds.Top >= viewport.Top && bounds.Bottom <= viewport.Bottom) return;
            if (!scroll.Current.VerticallyScrollable) break;
            var before = scroll.Current.VerticalScrollPercent;
            scroll.Scroll(ScrollAmount.NoAmount, bounds.Top < viewport.Top ? ScrollAmount.LargeDecrement : ScrollAmount.LargeIncrement);
            Thread.Sleep(50);
            if (scroll.Current.VerticalScrollPercent == before) break;
        }
        throw new InvalidOperationException("Exact native resource row could not be brought into view; no assignment committed.");
    }

    public static AutomationResult Write(AutomationElement main, string functionName, string field, string requested, bool confirm)
    {
        var target = requested.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(target, @"^(?:[ACE][1-9][0-9]?|(?:25A-)?HCO[1-4]|(?:DPO|INJ|IGN|HBO|AVI|SPI)[1-9][0-9]?)$"))
            return AutomationResult.Failed("uia", "Expected an explicit ECU pin or physical resource id (for example E4 or HCO4).");
        if (!confirm) return AutomationResult.Succeeded("none", new { dryRun = true, functionName, connectionField = field, pinOrResourceId = target });
        lock (MenuNavigator.InteractionLock)
        {
            AutomationElement? dialog = null;
            var committed = false;
            try
            {
                LiveUi.RequireOffline(main);
                var before = Read(main, functionName, field);
                if (Matches(before, target)) return AutomationResult.Succeeded("uia", new { before, after = before, changed = false });
                var page = LiveUi.Scope(main, "MainPageTab");
                LiveUi.Click(main, LiveUi.Unique(ConnectionRow(main, field), null, "Assign", "Button"));
                Thread.Sleep(250);
                dialog = SettingsUi.WaitDialog(main, "EditConnectionForm", "Select Connection");
                // NSP's picker search filters assignment/function text, not physical
                // resource captions: searching SPI5 or C19 can hide the desired row.
                // Inventory actual native row labels with an empty search instead.
                var search = LiveUi.Unique(dialog, "SearchTextBox", null, "Edit");
                LiveUi.SetNativeText(LiveUi.Handle(search), "");
                LiveUi.Click(dialog, LiveUi.Unique(dialog, "ShowAllButton", "Show All", "Button"));
                Thread.Sleep(500);
                var candidates = LiveUi.NativeChildren(dialog).Where(c => c.Visible &&
                    c.ClassName.Contains("WindowsForms10.Static", StringComparison.OrdinalIgnoreCase) &&
                    ResourceCaptionMatches(c.Text, target)).ToArray();
                // A pin and its resource can both match an alias. Group by native row,
                // not label, and refuse multiple devices/rows with the same resource.
                var rows = candidates.GroupBy(c => LiveUi.ParentHandle(c.Handle)).ToArray();
                if (rows.Length != 1) throw new InvalidOperationException($"Expected one native resource row matching '{target}', found {rows.Length}. No assignment committed.");
                var candidate = rows[0].First();
                var row = LiveUi.FromHandle(rows[0].Key);
                var texts = LiveUi.NativeChildren(row).Where(c => c.Visible &&
                    c.ClassName.Contains("WindowsForms10.Static", StringComparison.OrdinalIgnoreCase)).Select(c => c.Text).ToArray();
                if (texts.Any(t => t.Contains(", for ", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Requested resource is already assigned to another connection; no reassignment performed.");
                if (!texts.Contains("----"))
                    throw new InvalidOperationException("Resource row does not explicitly show unassigned status; no assignment committed.");
                var element = LiveUi.FromHandle(candidate.Handle);
                RevealResourceRow(dialog, row, element);
                LiveUi.Click(dialog, element);
                if (!ResourceCaptionMatches(LiveUi.NativeText(candidate.Handle), target))
                    throw new InvalidOperationException("Selected resource identity changed.");
                LiveUi.RequireOffline(main);
                LiveUi.Click(dialog, LiveUi.Unique(dialog, "OKButton", null, "Button"));
                committed = true;
                Thread.Sleep(350);
                var after = ReadCurrent(main, field);
                if (!Matches(after, target))
                    return AutomationResult.Failed("uia", $"Assignment readback mismatch: requested {target}, read {after.Port} / {after.Wire}. No automatic save was performed.");
                return AutomationResult.Succeeded("uia", new { before, after, changed = true });
            }
            catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException or ArgumentException)
            {
                if (!committed && dialog is not null)
                {
                    try { LiveUi.Click(dialog, LiveUi.Unique(dialog, "CancellingButton", null, "Button")); } catch { }
                }
                return AutomationResult.Failed("uia", ex.Message);
            }
        }
    }
}
