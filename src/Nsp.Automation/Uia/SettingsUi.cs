using System.Windows.Automation;

namespace Nsp.Automation.Uia;

public sealed record SettingReadback(string ExpectedValue, string ControlType = "Edit", string? AutomationId = null,
    string? ExactName = null, string? ControlKey = null, bool? Enabled = null);

internal static class SettingsUi
{
    public static string Key(AutomationElement e) => string.Join(".", e.GetRuntimeId());
    public static AutomationElement[] Dialogs(AutomationElement main)
    {
        return Nsp.Automation.WindowManager.OwnedWindows.Dialogs(LiveUi.Handle(main))
            .Select(LiveUi.FromHandle).ToArray();
    }
    public static AutomationElement Scope(AutomationElement main, string scopeId, string expectedTitle)
    {
        var scope = LiveUi.Scope(main, scopeId);
        var dialogs = Dialogs(main).Where(e => !e.Current.IsOffscreen).ToArray();
        if (scopeId == "MainPageTab")
        {
            if (dialogs.Length != 0) throw new InvalidOperationException("A dialog is open; background settings cannot be edited.");
            if (!PageNavigation.Identities(scope).Any(n => LiveUi.Normalize(n) == LiveUi.Normalize(expectedTitle)))
                throw new InvalidOperationException("Current page does not match expectedPage.");
        }
        else if (scope.Current.ControlType != ControlType.Window || scope.Current.Name != expectedTitle ||
            !dialogs.Any(d => System.Windows.Automation.Automation.Compare(d, scope)))
            throw new InvalidOperationException("Expected one exact owned settings dialog; scope/title mismatch.");
        return scope;
    }

    public static AutomationElement Input(AutomationElement scope, string? id, string? name, string? type, string? key)
    {
        if (string.IsNullOrEmpty(id) && name is null && key is null) throw new ArgumentException("An exact input selector is required.");
        var matches = LiveUi.InputControls(scope).Where(e =>
            (key is null || Key(e) == key) &&
            (string.IsNullOrEmpty(id) || e.Current.AutomationId == id || LiveUi.ContainerId(e) == id) &&
            (name is null || e.Current.Name == name) &&
            (type is null || e.Current.ControlType.ProgrammaticName == "ControlType." + type) &&
            SettingsTools.IsInput(e)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException($"Expected one matching settings input, found {matches.Length}; inventory again.");
        return matches[0];
    }

    public static AutomationElement Button(AutomationElement scope, string? id, string? name, string? key = null)
    {
        if (string.IsNullOrEmpty(id) && name is null) throw new ArgumentException("An exact button ID/name is required.");
        if (key is not null && name is null) throw new ArgumentException("A runtime button key requires its exact name.");
        var matches = LiveUi.ButtonControls(scope).Where(e => e.Current.ControlType == ControlType.Button &&
            (string.IsNullOrEmpty(id) || e.Current.AutomationId == id) && (name is null || e.Current.Name == name) &&
            (key is null || Key(e) == key)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException($"Expected one matching native button, found {matches.Length}; inventory again.");
        return matches[0];
    }

    public static void RequireSettingsAction(AutomationElement button)
    {
        var name = LiveUi.Normalize(button.Current.Name);
        if (new[] { "save", "upload", "firmware", "import", "export", "loadfromfile", "insertcolumn", "deleteselectedcolumn", "linearise", "connectdisconnect" }
            .Any(name.Contains))
            throw new InvalidOperationException("File/device commands and tuning-grid operations are outside settings actions; use their dedicated tools.");
    }

    public static void CheckMany(AutomationElement scope, SettingReadback[] expected)
    {
        var fields = InputSnapshots.Capture(scope);
        var keys = fields.ToDictionary(Key, e => e);
        foreach (var value in expected)
        {
            Nsp.Automation.Core.OperationContext.Checkpoint("Checking pending dialog value: " + (value.ExactName ?? value.AutomationId ?? value.ControlKey));
            if (value.ControlType is "Text" or "Button") { Check(scope, value); continue; }
            if (value.AutomationId is null && value.ExactName is null && value.ControlKey is null)
                throw new ArgumentException("An exact input selector is required.");
            var candidates = value.ControlKey is null ? fields : keys.TryGetValue(value.ControlKey, out var keyed) ? [keyed] : Array.Empty<AutomationElement>();
            var typed = candidates.Where(e => e.Cached.ControlType.ProgrammaticName == "ControlType." + value.ControlType &&
                (value.ExactName is null || e.Cached.Name == value.ExactName) &&
                (value.Enabled is null || e.Cached.IsEnabled == value.Enabled)).ToArray();
            var matches = typed.Where(e => value.AutomationId is null || e.Cached.AutomationId == value.AutomationId).ToArray();
            if (matches.Length == 0 && value.AutomationId is not null)
                matches = typed.Where(e => LiveUi.ContainerId(e) == value.AutomationId).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Pending input selector is stale or ambiguous; inventory again.");
            var actual = InputSnapshots.Read(matches[0]) ?? FormFieldDriver.Read(matches[0]);
            if (!FormFieldDriver.Equivalent(value.ExpectedValue, actual))
                throw new InvalidOperationException($"Pending readback mismatch for {value.ExactName ?? value.AutomationId}: expected '{value.ExpectedValue}', read '{actual}'.");
        }
    }

    public static object Check(AutomationElement scope, SettingReadback expected, bool allowKey = true)
    {
        if (!allowKey && expected.ControlKey is not null) throw new ArgumentException("Verification after reopening requires a stable ID/name, not a runtime controlKey.");
        AutomationElement target;
        string actual;
        if (expected.ControlType == "Button")
        {
            target = Button(scope, expected.AutomationId, expected.ExactName, expected.ControlKey);
            actual = target.Current.Name;
        }
        else if (expected.ControlType == "Text")
        {
            if (string.IsNullOrEmpty(expected.AutomationId) && expected.ExactName is null) throw new ArgumentException("Text verification requires an exact ID/name.");
            target = LiveUi.Unique(scope, expected.AutomationId, expected.ExactName, "Text", expected.Enabled);
            actual = target.Current.Name;
        }
        else
        {
            target = Input(scope, expected.AutomationId, expected.ExactName, expected.ControlType, expected.ControlKey);
            actual = FormFieldDriver.Read(target);
        }
        if (expected.Enabled is { } enabled && target.Current.IsEnabled != enabled)
            throw new InvalidOperationException("Readback control enabled state differs from the expected selector.");
        if (!FormFieldDriver.Equivalent(expected.ExpectedValue, actual))
            throw new InvalidOperationException($"Readback mismatch for {expected.AutomationId ?? expected.ExactName}: expected '{expected.ExpectedValue}', read '{actual}'.");
        return new { expected.AutomationId, expected.ExactName, expected.ControlType, value = actual };
    }

    public static object[] VerifyAccepted(AutomationElement main, string scopeId, string title, SettingReadback[] expected, string? allowedDialogId = null)
    {
        string? last = null;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var attempt = 0; attempt < 50 && watch.Elapsed < TimeSpan.FromSeconds(8); attempt++)
        {
            try
            {
                Nsp.Automation.Core.OperationContext.Checkpoint("Verifying accepted settings state");
                var scope = LiveUi.Scope(main, scopeId);
                var dialogs = Dialogs(main);
                if (scope.Current.ControlType == ControlType.Window && !dialogs.Any(d => System.Windows.Automation.Automation.Compare(d, scope)))
                    throw new InvalidOperationException("Accepted-state dialog is not owned by the selected NSP window.");
                if (dialogs.Any(d => !d.Current.IsOffscreen && !System.Windows.Automation.Automation.Compare(d, scope) && d.Current.AutomationId != allowedDialogId))
                    throw new InvalidOperationException("An unexpected dialog is still open.");
                if (scope.Current.ControlType == ControlType.Window ? scope.Current.Name != title :
                    !PageNavigation.Identities(scope).Any(n => LiveUi.Normalize(n) == LiveUi.Normalize(title)))
                    throw new InvalidOperationException("Accepted-state page/dialog identity is not yet present.");
                return expected.Select(v => Check(scope, v, false)).ToArray();
            }
            catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException)
            { last = ex.Message; }
            Thread.Sleep(100); // NSP rebuilds some sensor pages after accepting a preset.
        }
        throw new InvalidOperationException("Accepted-state readback did not verify after bounded waiting: " + last);
    }

    public static AutomationElement WaitDialog(AutomationElement main, string id, string title)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        string[] observed = [];
        while (watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            var dialogs = Dialogs(main);
            observed = dialogs.Select(e => e.Current.Name).ToArray();
            var matches = dialogs.Where(e =>
                (id.StartsWith("owned-title:", StringComparison.Ordinal) ? id[12..] == e.Current.Name : e.Current.AutomationId == id) && e.Current.Name == title).ToArray();
            if (matches.Length == 1) return matches[0];
            if (matches.Length > 1) throw new InvalidOperationException("Ambiguous settings dialog.");
            Thread.Sleep(100);
        }
        throw new InvalidOperationException($"Expected settings dialog ID '{id}', title '{title}' was not verified. Open owned titles: {string.Join(" | ", observed)}. The opening click may have succeeded; call nsp_list_process_windows before retrying. No follow-up input sent.");
    }

    public static void OpenPreference(AutomationElement main, string item)
    {
        // Opens preference editors only. Firmware installation, connection and security commands are not settings launchers.
        if (item is not ("Theme Options" or "Units Options" or "Navigation Tree Options" or "Table Text View Options"))
            throw new ArgumentException("Supported preference editors: Theme Options, Units Options, Navigation Tree Options, Table Text View Options.");
        if (LiveUi.RawChildren(main).Any(e => e.Current.ControlType == ControlType.Window))
            throw new InvalidOperationException("Close the current dialog before opening another.");
        var menu = LiveUi.Scope(main, "MainMenuStrip");
        LiveUi.Click(main, LiveUi.Unique(menu, null, "Preferences", "MenuItem"));
        Thread.Sleep(150);
        LiveUi.Click(main, LiveUi.Unique(menu, null, item, "MenuItem"));
    }
}
