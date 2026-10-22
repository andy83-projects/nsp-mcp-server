using System.Windows.Automation;
using System.Globalization;

namespace Nsp.Automation.Uia;

/// <summary>
/// Generic labeled-field get/set via UIA ValuePattern, for the many scalar settings (gear
/// ratios, soft-start duration, dwell points, etc.) that aren't table cells. Also implements
/// the cyan-highlight-then-OK commit pattern seen throughout the build logs for connection
/// selectors, done through the accessibility tree where SelectionItemPattern is exposed.
/// </summary>
public sealed class FormFieldDriver(MenuNavigator navigator)
{
    public string? GetValue(AutomationElement scope, string fieldLabel)
    {
        var field = FindFieldInput(scope, fieldLabel)
            ?? throw new InvalidOperationException($"Field '{fieldLabel}' not found.");

        return Read(field);
    }

    /// <summary>Set an exact, unambiguous labeled input using its supported accessibility pattern.</summary>
    public bool TrySetValue(AutomationElement scope, string fieldLabel, string newValue, AutomationElement? main = null)
    {
        var field = FindFieldInput(scope, fieldLabel)
            ?? throw new InvalidOperationException($"Field '{fieldLabel}' not found.");

        Set(main ?? scope, field, newValue);
        return true;
    }

    public static string Read(AutomationElement field)
    {
        if (LiveUi.NumericEditor(field) is { } editor) return Read(editor);
        if (field.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle))
            return ((TogglePattern)toggle).Current.ToggleState switch { ToggleState.On => "true", ToggleState.Off => "false", _ => "mixed" };
        if (field.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var item) && field.Current.ControlType == ControlType.RadioButton)
            return ((SelectionItemPattern)item).Current.IsSelected ? "true" : "false";
        if (field.TryGetCurrentPattern(SelectionPattern.Pattern, out var selection))
            return string.Join(", ", ((SelectionPattern)selection).Current.GetSelection().Select(e => e.Current.Name));
        // NumericUpDown's editable child can itself be reported as Spinner. Its
        // ValuePattern is the displayed value; some providers expose a dummy range.
        if (LiveUi.Value(field) is { } text) return text;
        if (field.TryGetCurrentPattern(RangeValuePattern.Pattern, out var range))
            return ((RangeValuePattern)range).Current.Value.ToString("R", CultureInfo.InvariantCulture);
        return LiveUi.Value(field) ?? throw new InvalidOperationException("Control does not expose a readable value.");
    }

    public static bool Equivalent(string expected, string actual) => expected == actual ||
        (bool.TryParse(expected, out var first) && bool.TryParse(actual, out var second) && first == second) ||
        (double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) &&
         double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) &&
         double.IsFinite(a) && double.IsFinite(b) && Math.Abs(a - b) < 1e-9);

    public static void Set(AutomationElement main, AutomationElement field, string value)
    {
        if (LiveUi.NumericEditor(field) is { } editor) field = editor;
        if (!field.Current.IsEnabled || field.Current.IsOffscreen) throw new InvalidOperationException("Control is disabled or hidden.");
        if (Equivalent(value, Read(field))) return;
        Nsp.Automation.Core.OperationContext.Checkpoint("Ready to set native control");
        using var deferredCancellation = Nsp.Automation.Core.OperationContext.DeferCancellation();
        if (field.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle))
        {
            if (!bool.TryParse(value, out var desired)) throw new ArgumentException("Checkbox value must be true or false.");
            var p = (TogglePattern)toggle;
            if (p.Current.ToggleState == ToggleState.Indeterminate) throw new InvalidOperationException("Mixed checkboxes are not supported.");
            if ((p.Current.ToggleState == ToggleState.On) != desired) p.Toggle();
        }
        else if (field.Current.ControlType == ControlType.RadioButton && field.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var radio))
        {
            if (!bool.TryParse(value, out var desired) || !desired) throw new ArgumentException("Select the desired radio button with value=true; a radio cannot be independently cleared.");
            ((SelectionItemPattern)radio).Select();
        }
        else if (field.Current.ControlType == ControlType.ComboBox)
        {
            if (!field.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expand))
                throw new InvalidOperationException("Dropdown does not expose ExpandCollapsePattern.");
            if (!NativeComboBox.Options(field).Contains(value))
                throw new ArgumentException("Value is not an exact native dropdown option.");
            var p = (ExpandCollapsePattern)expand;
            p.Expand();
            try
            {
                Thread.Sleep(150);
                var list = NativeComboBox.List(field);
                var matches = LiveUi.WalkControls(list, 3, 512).Where(e =>
                    e.Current.ControlType == ControlType.ListItem && e.Current.Name == value).ToList();
                if (matches.Count != 1) throw new InvalidOperationException($"Expected one visible dropdown option '{value}', found {matches.Count}.");
                var option = matches[0];
                if (option.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select)) ((SelectionItemPattern)select).Select();
                else LiveUi.Click(main, option);
            }
            finally { if (p.Current.ExpandCollapseState == ExpandCollapseState.Expanded) p.Collapse(); }
        }
        else if (field.TryGetCurrentPattern(ValuePattern.Pattern, out var raw) && raw is ValuePattern vp && !vp.Current.IsReadOnly)
        {
            field.SetFocus();
            vp.SetValue(value);
            MenuNavigator.SendKeysToWindow(AutomationElement.FromHandle(Nsp.Automation.WindowManager.OwnedWindows.Root(LiveUi.Handle(main), LiveUi.Handle(field))), "{TAB}");
        }
        else if (field.TryGetCurrentPattern(RangeValuePattern.Pattern, out var range))
        {
            var p = (RangeValuePattern)range;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) ||
                p.Current.IsReadOnly || number < p.Current.Minimum || number > p.Current.Maximum)
                throw new ArgumentException("Value is invalid, outside the native numeric limits, or read-only.");
            p.SetValue(number);
        }
        else throw new InvalidOperationException("This control has no supported writable accessibility pattern.");
        Thread.Sleep(150);
        var actual = Read(field);
        if (!Equivalent(value, actual)) throw new InvalidOperationException($"Setting readback mismatch: requested '{value}', received '{actual}'.");
    }

    /// <summary>
    /// Confirmed live (2026-10-20): NSP's scalar-field rows (e.g. "1st Gear Ratio") carry the
    /// SAME exact Name on both the Text label and the actual Edit input — a plain Name-only
    /// FindFirst just grabs whichever comes first in document order (the label, which has no
    /// ValuePattern). This prefers an Edit (or anything with ValuePattern) among same-named
    /// matches, falling back to a bare Name match only if nothing more specific exists.
    /// </summary>
    private static AutomationElement? FindFieldInput(AutomationElement scope, string fieldLabel)
    {
        var matches = LiveUi.InputControls(scope).Where(e => e.Current.Name == fieldLabel &&
            (e.Current.ControlType == ControlType.Edit || e.Current.ControlType == ControlType.ComboBox ||
             e.Current.ControlType == ControlType.CheckBox || e.Current.ControlType == ControlType.RadioButton ||
             e.Current.ControlType == ControlType.Spinner)).ToList();
        if (matches.Count > 1) throw new InvalidOperationException($"Field '{fieldLabel}' is ambiguous; use exact scoped IDs with nsp_set_setting.");
        return matches.SingleOrDefault();
    }

    /// <summary>Select row (cyan highlight in NSP's own visual language) then invoke the
    /// commit button — never trust the click/select call's return value alone; the caller
    /// must re-read the downstream assigned value afterward to verify.</summary>
    public bool TrySelectRowAndCommit(AutomationElement listScope, string rowName, AutomationElement commitButton)
    {
        var row = navigator.FindByName(listScope, rowName)
            ?? throw new InvalidOperationException($"Row '{rowName}' not found.");

        if (!row.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern) || pattern is not SelectionItemPattern selection)
        {
            return false;
        }

        selection.Select();
        return navigator.Invoke(commitButton);
    }
}
