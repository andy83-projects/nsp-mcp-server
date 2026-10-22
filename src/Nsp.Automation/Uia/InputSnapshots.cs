using System.Windows.Automation;
using Nsp.Automation.Core;

namespace Nsp.Automation.Uia;

/// <summary>Batch read-only form properties once, without probing every UIA pattern.</summary>
internal static class InputSnapshots
{
    public static AutomationElement[] Capture(AutomationElement scope)
    {
        var cache = new CacheRequest { TreeScope = TreeScope.Element };
        foreach (var property in new[] { AutomationElement.NameProperty, AutomationElement.AutomationIdProperty,
            AutomationElement.ControlTypeProperty, AutomationElement.IsEnabledProperty, AutomationElement.IsOffscreenProperty,
            AutomationElement.NativeWindowHandleProperty, ValuePattern.ValueProperty, TogglePattern.ToggleStateProperty,
            RangeValuePattern.ValueProperty, SelectionItemPattern.IsSelectedProperty, SelectionPattern.SelectionProperty }) cache.Add(property);
        var result = new List<AutomationElement>();
        foreach (var e in LiveUi.InputControls(scope))
        {
            OperationContext.Checkpoint("Reading native input snapshot");
            result.Add(e.GetUpdatedCache(cache));
        }
        return result.ToArray();
    }
    public static string? Read(AutomationElement e)
    {
        object Value(AutomationProperty p) => e.GetCachedPropertyValue(p, true);
        if (e.Cached.ControlType == ControlType.CheckBox && Value(TogglePattern.ToggleStateProperty) is ToggleState toggle)
            return toggle == ToggleState.On ? "true" : toggle == ToggleState.Off ? "false" : "mixed";
        if (e.Cached.ControlType == ControlType.RadioButton && Value(SelectionItemPattern.IsSelectedProperty) is bool selected)
            return selected ? "true" : "false";
        if (Value(ValuePattern.ValueProperty) is string text) return text;
        if (Value(SelectionPattern.SelectionProperty) is AutomationElement[] selection)
            return string.Join(", ", selection.Select(item => item.Current.Name));
        if (Value(RangeValuePattern.ValueProperty) is double number)
            return number.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return null;
    }

}
