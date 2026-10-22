using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using Nsp.Automation.Fallback;
using Nsp.TuningTables;

namespace Nsp.Automation.Uia;

/// <summary>Replace one breakpoint inside the still-uncommitted axis dialog.
/// Full-axis clipboard paste re-clamps unrelated legacy points (e.g. 6V to 6.5V).
/// Native delete/insert preserves those points and never commits an intermediate size.</summary>
internal static class NativeAxisEditor
{
    public static void Replace(AutomationElement dialog, AutomationElement grid, bool rows, int index,
        double[] before, double[] requested, double value)
    {
        AxisWriteVerifier.RequirePendingAxis(before, TuningGridUiaDriver.GridText(grid)[0]);
        var target = ((GridPattern)grid.GetCurrentPattern(GridPattern.Pattern)).GetItem(0, index);
        if (target.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scroll)) ((ScrollItemPattern)scroll).ScrollIntoView();
        InputSynthesizer.ReleaseAllModifierKeys();
        LiveUi.Click(dialog, target);
        var suffix = rows ? "VertButton" : "HorizButton";
        LiveUi.Click(dialog, LiveUi.Unique(dialog, "Delete" + suffix, "Delete Values", "Button"));
        AxisWriteVerifier.RequirePendingAxis(before.Where((_, i) => i != index).ToArray(), TuningGridUiaDriver.GridText(grid)[0]);
        LiveUi.Click(dialog, LiveUi.Unique(dialog, "Insert" + suffix, "Insert Value", "Button"));
        AutomationElement? entry = null;
        for (var i = 0; entry is null && i < 30; i++)
        {
            entry = LiveUi.RawChildren(dialog).SingleOrDefault(e => e.Current.ControlType == ControlType.Window &&
                e.Current.Name == "Insert Value" && (e.Current.AutomationId is "DirectValueEntryForm" or ""));
            if (entry is null) Thread.Sleep(100);
        }
        if (entry is null) throw new InvalidOperationException("Owned Insert Value dialog did not appear.");
        var handle = LiveUi.Handle(entry);
        var closed = false;
        try
        {
            // Refuse values outside the native advertised limits before an error popup.
            double Limit(string id)
            {
                var label = LiveUi.WalkControls(entry, 4, 64).Single(e => e.Current.AutomationId == id);
                var match = Regex.Match(label.Current.Name, @"[-+]?\d+(?:\.\d+)?\s*$");
                if (!match.Success) throw new InvalidOperationException("Cannot read native breakpoint limits.");
                return double.Parse(match.Value, CultureInfo.InvariantCulture);
            }
            if (value < Limit("MinLabel") || value > Limit("MaxLabel"))
                throw new ArgumentException("Requested breakpoint is outside the native insertion limits.");
            var inputs = LiveUi.InputControls(entry).Where(e => e.Current.IsEnabled && !e.Current.IsOffscreen).ToArray();
            var input = inputs.SingleOrDefault(e => e.Current.AutomationId == "ValueComboBox" && e.Current.ControlType == ControlType.ComboBox)
                ?? inputs.Single(e => e.Current.ControlType == ControlType.Edit);
            LiveUi.SetValue(input, value.ToString("R", CultureInfo.InvariantCulture));
            LiveUi.Click(entry, LiveUi.Unique(entry, "OKButton", "OK", "Button"));
            NativeDialogs.WaitClosed(handle);
            closed = true;
        }
        finally
        {
            if (!closed) NativeDialogs.CancelDialog(entry, entry.Current.AutomationId);
        }
        AxisWriteVerifier.RequirePendingAxis(requested, TuningGridUiaDriver.GridText(grid)[0]);
    }
}
