using System.ComponentModel;
using System.Text.Json;
using System.Windows.Automation;
using ModelContextProtocol.Server;
using Nsp.Audit;
using Nsp.Automation.WindowManager;

namespace Nsp.Automation.Uia;

[McpServerToolType]
public sealed class SettingsTools(NspWindowLocator locator, NspAppDriverOptions options, AuditLogger audit)
{
    private AutomationElement Main => locator.Locate(options.ExpectedOpenFileName).Window;

    [McpServerTool(Name = "nsp_navigate_to_page", Destructive = true)]
    [Description("Navigate using native tree messages and verify an exact settings-page heading. Uses native control bounds for the tree reset; no images, calibration edits or save. Requires confirm=true. expectedPage is the full visible heading; searchText is NSP's tree search.")]
    public object NavigateToPage(string searchText, string expectedPage, bool confirm, CancellationToken cancellationToken)
    {
        if (!confirm) return new { dryRun = true, searchText, expectedPage };
        lock (MenuNavigator.InteractionLock)
        {
            var main = Main;
            var observed = new HashSet<string>(StringComparer.Ordinal);
            bool Match()
            {
                foreach (var heading in PageNavigation.Identities(LiveUi.Scope(main, "MainPageTab"))) observed.Add(heading);
                return PageNavigation.Matches(main, expectedPage);
            }
            try
            {
                var navigation = PageNavigation.Navigate(main, searchText, Match, cancellationToken: cancellationToken, destinationIdentity: expectedPage);
                return new { success = true, expectedPage, navigation, identities = PageNavigation.Identities(LiveUi.Scope(main, "MainPageTab")) };
            }
            catch (InvalidOperationException ex)
            { return new { success = false, expectedPage, errorMessage = ex.Message, observedPages = observed.ToArray(),
                nextAction = "Use an exact observed heading, or nsp_discover_pages to discover headings without guessing. A failed navigation does not authorize a settings write." }; }
        }
    }

    [McpServerTool(Name = "nsp_discover_pages", ReadOnly = true)]
    [Description("Discover exact settings-page headings for a navigation search without knowing a destination in advance. Traverses native filtered results; returns observed headings and whether traversal reached its bound. May change current page, never edits calibration, saves or uploads. Use the returned full heading with nsp_navigate_to_page and nsp_set_setting. No screenshots/OCR. Search can include ancestor/setting text, so unrelated pages may also be present.")]
    public object DiscoverPages(string searchText, int maxSteps = 40, CancellationToken cancellationToken = default)
    {
        lock (MenuNavigator.InteractionLock)
        {
            var main = Main;
            var pages = new HashSet<string>(StringComparer.Ordinal);
            string? stopped = null;
            var samples = 0;
            bool Observe()
            {
                foreach (var heading in PageNavigation.Identities(LiveUi.Scope(main, "MainPageTab"))) pages.Add(heading);
                samples++;
                return false;
            }
            try { PageNavigation.Navigate(main, searchText, Observe, Math.Clamp(maxSteps, 1, 60), cancellationToken, forceSearch: true); }
            catch (InvalidOperationException ex) { stopped = ex.Message; }
            return new { searchText, pages = pages.ToArray(), samples, complete = false, stopped,
                nextAction = "Use an exact returned heading. This is a bounded survey, not a complete function catalog; refine search if needed." };
        }
    }

    [McpServerTool(Name = "nsp_list_settings", ReadOnly = true)]
    [Description("List current-page or exact-dialog input controls, exact labels/IDs, values and enabled state. Use containerId as automationId with nsp_set_setting for compound fields, including firing-order Edit cells and cylinder-bank ComboBoxes. context provides adjacent labels; actions lists native buttons; connectionFields provides exact connectionField names for I/O tools. Adjacent labels/container IDs require includeContext=true; default fast inventory omits those optional reads. Excludes tuning-grid cells.")]
    public object ListSettings(string scopeId = "MainPageTab", bool includeContext = false)
    {
        lock (MenuNavigator.InteractionLock)
        {
            Nsp.Automation.Core.OperationContext.Checkpoint("Settings inventory: resolving scope");
            var scope = LiveUi.Scope(Main, scopeId);
            Nsp.Automation.Core.OperationContext.Checkpoint("Settings inventory: enumerating native inputs");
            // Fetch each native control's properties in one provider request. Repeated
            // .Current/GetSupportedPatterns calls are expensive on NSP's dialog provider.
            var snapshots = InputSnapshots.Capture(scope);
            var controls = snapshots.Where(e => IsInputType(e.Cached.ControlType)).Select(e =>
            {
                Nsp.Automation.Core.OperationContext.Checkpoint("Settings inventory: reading " + e.Cached.Name);
                var value = InputSnapshots.Read(e);
                return new { controlKey = Key(e), id = e.Cached.AutomationId, containerId = includeContext ? LiveUi.ContainerId(e) : null,
                    context = includeContext ? LiveUi.NativeContext(e) : [], name = e.Cached.Name, type = e.Cached.ControlType.ProgrammaticName,
                    value, error = value is null ? "Control does not expose a cached readable value." : null,
                    enabled = e.Cached.IsEnabled, offscreen = e.Cached.IsOffscreen, valueReadable = value is not null };
            }).ToArray();
            var actions = snapshots.Where(e => e.Cached.ControlType == ControlType.Button)
                .Select(e => new { controlKey = Key(e), id = e.Cached.AutomationId, name = e.Cached.Name,
                    enabled = e.Cached.IsEnabled, offscreen = e.Cached.IsOffscreen }).ToArray();
            var connectionFields = LiveUi.NativeChildren(scope).Where(c => c.Visible && c.ClassName.Contains("WindowsForms10.Static", StringComparison.OrdinalIgnoreCase))
                .Select(c => AutomationElement.FromHandle(c.Handle)).Where(e => e.Current.AutomationId == "descLabel")
                .Select(e => e.Current.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToArray();
            return new { scopeId, title = scope.Current.Name, identities = PageNavigation.Identities(scope), controls, actions, connectionFields,
                commitStatus = scope.Current.ControlType == ControlType.Window ? "Dialog values may be pending; use nsp_finish_settings_dialog with explicit verification." : "Page controls commit on edit." };
        }
    }

    [McpServerTool(Name = "nsp_set_setting", Destructive = true)]
    [Description("Set one exact scoped Edit, ComboBox, CheckBox, RadioButton or numeric control, commit and verify readback. Requires an exact expectedPage heading (or exact dialog title), explicit confirm=true and disconnected NSP. Dropdown values are exact option names; checkbox values are true/false. A dialog readback is pending until its explicit commit and accepted-state verification with nsp_finish_settings_dialog. Does not save or upload. Every request is audited.")]
    public async Task<object> SetSetting(string expectedPage, string value, bool confirm, string scopeId = "MainPageTab",
        string? automationId = null, string? exactName = null, string? controlType = null, string? controlKey = null, string? expectedValue = null)
    {
        string? before = null, after = null, error = null;
        var actionAttempted = false;
        try
        {
            lock (MenuNavigator.InteractionLock)
            {
                var main = Main;
                var scope = SettingsUi.Scope(main, scopeId, expectedPage);
                AutomationElement target;
                if (controlKey is not null)
                {
                    if (exactName is null || controlType is null || expectedValue is null)
                        throw new ArgumentException("controlKey requires exactName, controlType and expectedValue from a fresh inventory.");
                    target = LiveUi.InputControls(scope).SingleOrDefault(e => Key(e) == controlKey &&
                        e.Current.Name == exactName && e.Current.ControlType.ProgrammaticName == "ControlType." + controlType)
                        ?? throw new InvalidOperationException("Control key is stale or its label/type has changed; list settings again.");
                }
                else target = SettingsUi.Input(scope, automationId, exactName, controlType, null);
                if (!IsInput(target)) throw new InvalidOperationException("Target is not a supported settings input.");
                before = FormFieldDriver.Read(target);
                if (expectedValue is not null && !FormFieldDriver.Equivalent(expectedValue, before))
                    throw new InvalidOperationException("Current value differs from expectedValue; no write performed.");
                if (confirm)
                {
                    LiveUi.RequireOffline(main);
                    actionAttempted = true;
                    FormFieldDriver.Set(main, target, value);
                    after = FormFieldDriver.Read(target);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ElementNotAvailableException or System.Runtime.InteropServices.COMException)
        { error = ex.Message; }
        await audit.AppendAsync(new AuditLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow, ToolName = "nsp_set_setting", DryRun = !confirm, Success = error is null,
            DriverUsed = "uia", InputsJson = JsonSerializer.Serialize(new { expectedPage, scopeId, automationId, exactName, controlType, controlKey, expectedValue, value }),
            BeforeValueJson = JsonSerializer.Serialize(before), AfterValueJson = JsonSerializer.Serialize(after), ErrorMessage = error,
        });
        return new { success = error is null, dryRun = !confirm, before, after, requestedValue = value, errorMessage = error, actionAttempted,
            nextAction = error is not null && actionAttempted ? "Inspect current settings and audit before retrying; the value may already have changed." : null };
    }

    private static string Key(AutomationElement e) => string.Join(".", e.GetRuntimeId());

    [McpServerTool(Name = "nsp_list_setting_options", ReadOnly = true)]
    [Description("Read native dropdown option names without opening the dropdown. Exact label must be unique, or supply a fresh inventory controlKey for repeated labels.")]
    public object ListSettingOptions(string exactName, string scopeId = "MainPageTab", string? controlKey = null)
    {
        lock (MenuNavigator.InteractionLock)
        {
            var matches = LiveUi.InputControls(LiveUi.Scope(Main, scopeId)).Where(e =>
                e.Current.ControlType == ControlType.ComboBox && e.Current.Name == exactName &&
                (controlKey is null || Key(e) == controlKey)).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException($"Expected one dropdown, found {matches.Length}.");
            return new { exactName, currentValue = FormFieldDriver.Read(matches[0]), options = NativeComboBox.Options(matches[0]) };
        }
    }

    private static bool IsInputType(ControlType type) => type == ControlType.Edit || type == ControlType.ComboBox ||
        type == ControlType.CheckBox || type == ControlType.RadioButton || type == ControlType.Spinner;
    internal static bool IsInput(AutomationElement e) => IsInputType(e.Current.ControlType);
}
