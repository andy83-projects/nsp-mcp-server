using System.Windows.Automation;
using Nsp.Automation.Core;

namespace Nsp.Automation.Uia;

public sealed record FunctionState(bool Enabled, IReadOnlyList<string> ValidationMarkers);

/// <summary>
/// Drives the F4 Function Manager: enable/disable a function, and read back its state plus
/// any validation/dependency banner text. The logged Cruise Control case — "Requires Gear
/// Detection or Neutral Switch to be enabled" appearing after a toggle that otherwise looked
/// like it succeeded — is exactly why ReadValidationMarkers is called unconditionally by
/// every caller, not just on failure.
/// </summary>
public sealed class FunctionManagerDriver
{
    public FunctionState GetFunctionState(AutomationElement searchScope, string functionName)
    {
        var functionElement = FindFunctionCheckbox(searchScope, functionName)
            ?? throw new InvalidOperationException($"Function '{functionName}' not found.");

        if (!functionElement.TryGetCurrentPattern(TogglePattern.Pattern, out var togglePattern) || togglePattern is not TogglePattern toggle ||
            toggle.Current.ToggleState == ToggleState.Indeterminate)
            throw new InvalidOperationException($"Function '{functionName}' has no definite readable toggle state.");
        var enabled = toggle.Current.ToggleState == ToggleState.On;

        return new FunctionState(enabled, ReadValidationMarkers(searchScope));
    }

    public FunctionState SetFunctionEnabled(AutomationElement searchScope, string functionName, bool enabled)
    {
        var functionElement = FindFunctionCheckbox(searchScope, functionName)
            ?? throw new InvalidOperationException($"Function '{functionName}' not found.");

        if (!functionElement.Current.IsEnabled || functionElement.Current.IsOffscreen)
            throw new InvalidOperationException($"Function '{functionName}' is disabled or hidden; no toggle sent.");
        if (functionElement.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern) && pattern is TogglePattern toggle &&
            toggle.Current.ToggleState != ToggleState.Indeterminate)
        {
            var currentlyOn = toggle.Current.ToggleState == ToggleState.On;
            if (currentlyOn != enabled)
            {
                toggle.Toggle();
            }
        }
        else
        {
            throw new InvalidOperationException($"Function '{functionName}' has no definite TogglePattern state; no toggle sent.");
        }

        var state = GetFunctionState(searchScope, functionName);
        if (state.Enabled != enabled)
            throw new InvalidOperationException($"Function '{functionName}' readback differs from requested enabled={enabled}; inspect before retrying.");
        return state;
    }

    /// <summary>
    /// Confirmed live (2026-10-20), via a full unfiltered tree dump cross-checked against a
    /// real screenshot: each function row is a CheckBox (TogglePattern, but an EMPTY Name)
    /// sitting as a sibling immediately before a separate Text element that carries the
    /// visible label ("Cruise Control"). A direct Name+ControlType=CheckBox search can never
    /// match, because the checkbox itself has no name — only its sibling label does. The fix
    /// is to find one exact visible label and pair it with its immediately preceding
    /// checkbox. A shared parent may contain several function rows; its first checkbox
    /// can belong to a substring match such as Differential Oil Temperature Sensor.
    ///
    /// Also: the "Available Functions:" panel element OpenFunctionManager returns is not a
    /// reliable search root — multiple elements share that exact name/type in the live tree —
    /// so callers should pass the full app window as searchScope.
    ///
    /// The function list is virtualized: a row only exists in the UIA tree once the list is
    /// filtered down to include it (matching the build log's "tab 3 times from its search
    /// field to Cruise Control toggle" — the search step was never optional).
    /// FilterFunctionList types into the search Edit box via ValuePattern.SetValue, a direct
    /// UIA write that doesn't need foreground focus the way SendKeys does.
    /// </summary>
    private static AutomationElement? FindFunctionCheckbox(AutomationElement searchScope, string functionName)
    {
        foreach (var query in NavigationQueries.WithSingularFallbacks([functionName]))
        {
            var found = FindFilteredCheckbox(searchScope, functionName, query);
            if (found is not null) return found;
        }
        return null;
    }

    private static AutomationElement? FindFilteredCheckbox(AutomationElement searchScope, string functionName, string query)
    {
        FilterFunctionList(searchScope, query);

        var page = LiveUi.Scope(searchScope, "MainPageTab");
        var labels = LiveUi.NativeChildren(page).Where(c => c.Visible && c.Text == functionName &&
            c.ClassName.Contains("WindowsForms10.Static", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (labels.Length == 0) return null;
        if (labels.Length != 1) throw new InvalidOperationException($"Function '{functionName}' has {labels.Length} exact native labels; no toggle sent.");
        var parent = LiveUi.ParentHandle(labels[0].Handle);
        var children = LiveUi.NativeChildren(LiveUi.FromHandle(parent)).Where(c => c.Visible && LiveUi.ParentHandle(c.Handle) == parent).ToArray();
        var entries = children.Select(c => new FunctionRowEntry(c.Text,
            c.ClassName.Contains("WindowsForms10.Static", StringComparison.OrdinalIgnoreCase),
            c.ClassName.Contains("WindowsForms10.Button", StringComparison.OrdinalIgnoreCase))).ToArray();
        var target = LiveUi.FromHandle(children[FunctionRowSelector.CheckboxIndex(entries, functionName)].Handle);
        if (target.Current.ControlType != ControlType.CheckBox)
            throw new InvalidOperationException("The exact native function row is not paired with an accessible checkbox; no toggle sent.");
        return target;
    }

    private static void FilterFunctionList(AutomationElement searchScope, string filterText)
    {
        var page = LiveUi.Scope(searchScope, "MainPageTab");
        var controls = LiveUi.NativeChildren(page);
        if (!controls.Any(c => c.Visible && c.Text == "Available Functions:"))
            throw new InvalidOperationException("Function Manager is not the current native page.");
        var searches = controls.Where(c => c.Visible && c.ClassName.Contains("WindowsForms10.Edit", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (searches.Length != 1) throw new InvalidOperationException("Expected one native Function Manager search; no function selected.");
        Nsp.Automation.Core.OperationContext.Checkpoint("Filtering native Function Manager list");
        LiveUi.SetNativeText(searches[0].Handle, filterText);
        Thread.Sleep(700);
    }

    /// <summary>
    /// Reads any visible red configuration-error marker / dependency-warning text. NSP's
    /// exact control for this is unknown until Phase 0 — this searches broadly by control
    /// type and known keyword fragments as a first pass; refine once the real control is found.
    /// </summary>
    public IReadOnlyList<string> ReadValidationMarkers(AutomationElement scope)
    {
        var markers = new List<string>();
        // Enumerate real native labels only, not virtual table/document children.
        // Custom red icons without accessible text are outside this scan's coverage.
        foreach (var element in LiveUi.NativeChildren(scope).Where(c => c.Visible &&
            c.ClassName.Contains("WindowsForms10.Static", StringComparison.OrdinalIgnoreCase)))
        {
            var text = element.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (text.Contains("require", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("must be assigned", StringComparison.OrdinalIgnoreCase))
            {
                markers.Add(text);
            }
        }

        return markers.Distinct().ToArray();
    }
}
