using System.ComponentModel;
using System.Text.Json;
using System.Windows.Automation;
using ModelContextProtocol.Server;
using Nsp.Audit;
using Nsp.Automation.Core;
using Nsp.Automation.WindowManager;

namespace Nsp.Automation.Uia;

[McpServerToolType]
public sealed class SettingsDialogTools(NspWindowLocator locator, NspAppDriverOptions options, AuditLogger audit)
{
    private AutomationElement Main => locator.Locate(options.ExpectedOpenFileName).Window;

    [McpServerTool(Name = "nsp_open_settings_dialog", Destructive = true)]
    [Description("Open an exact settings dialog from a supported Preferences editor or one exact native Button. sourceScopeId defaults to MainPageTab; for a nested selector use the exact parent dialog scope and its title as expectedPage. Specify expected dialog ID/title, and either preferenceItem or expectedPage plus button ID/name. Verifies the owned dialog appears; no calibration save/upload. Discover page actions with nsp_list_settings. confirm=false sends no input.")]
    public Task<object> Open(string dialogId, string expectedTitle, bool confirm, string? preferenceItem = null,
        string? expectedPage = null, string? buttonAutomationId = null, string? buttonName = null, string? buttonControlKey = null, string sourceScopeId = "MainPageTab") => Run("nsp_open_settings_dialog",
        new { dialogId, expectedTitle, confirm, preferenceItem, expectedPage, buttonAutomationId, buttonName, buttonControlKey, sourceScopeId }, confirm, trace =>
        {
            var main = Main;
            if (preferenceItem is not null) SettingsUi.OpenPreference(main, preferenceItem);
            else
            {
                if (expectedPage is null) throw new ArgumentException("expectedPage is required for a page action.");
                var page = SettingsUi.Scope(main, sourceScopeId, expectedPage);
                LiveUi.RequireOffline(main);
                var button = SettingsUi.Button(page, buttonAutomationId, buttonName, buttonControlKey);
                SettingsUi.RequireSettingsAction(button);
                trace.ActionAttempted = true;
                LiveUi.Click(main, button);
            }
            var dialog = SettingsUi.WaitDialog(main, dialogId, expectedTitle);
            return new { success = true, scopeId = dialogId, title = dialog.Current.Name };
        });

    [McpServerTool(Name = "nsp_activate_setting_action", Destructive = true)]
    [Description("Activate one exact current-page native settings Button (for example Show Settings/Use Custom Settings), then verify the expected page and explicit accepted control/text values. Repeated button labels require a fresh controlKey and exact name from nsp_list_settings. Does not handle modal OK; use the dialog tools for that. Requires offline NSP and confirm=true; no save/upload. A failed post-action readback may follow an applied change; inspect before retrying.")]
    public Task<object> Activate(string expectedPage, SettingReadback[] verificationValues, bool confirm,
        string? buttonAutomationId = null, string? buttonName = null, string? buttonControlKey = null, string? verificationPage = null) =>
        Run("nsp_activate_setting_action", new { expectedPage, verificationValues, confirm, buttonAutomationId, buttonName, buttonControlKey, verificationPage }, confirm, trace =>
        {
            if (verificationValues.Length == 0 || verificationValues.Any(v => v.ControlKey is not null))
                throw new ArgumentException("Action verification requires stable accepted-value selectors.");
            var main = Main;
            var page = SettingsUi.Scope(main, "MainPageTab", expectedPage);
            LiveUi.RequireOffline(main);
            var button = SettingsUi.Button(page, buttonAutomationId, buttonName, buttonControlKey);
            SettingsUi.RequireSettingsAction(button);
            OperationContext.Checkpoint("Ready to activate settings action");
            using var deferred = OperationContext.DeferCancellation();
            trace.ActionAttempted = true;
            LiveUi.Click(main, button);
            Thread.Sleep(350);
            var verified = SettingsUi.VerifyAccepted(main, "MainPageTab", verificationPage ?? expectedPage, verificationValues);
            return new { success = true, verified, mapSaved = false };
        });

    [McpServerTool(Name = "nsp_finish_settings_dialog", Destructive = true)]
    [Description("Finish one exact owned settings dialog using OK, Apply, Close or Cancel. Commit requires expectedValues (all pending control/text readbacks) and verificationValues (independent accepted-state readbacks). For OK/Close, reopenPreference can reopen a supported Preferences editor and verify persistence; otherwise verificationScopeId/verificationPage must identify downstream main-page values. Apply must verify separate downstream values, never just its pending dialog fields. Cancel verifies closure only and does not claim rollback. If NSP opens a nested Confirmation, the response returns its complete text; review it and supply confirmationText exactly to continue the existing confirmation. Preset commits require exact setup name/version metadata. A post-click failure means the outcome may already be applied: inspect it before retrying. Never saves/uploads a map.")]
    public Task<object> Finish(string scopeId, string expectedTitle, string action, string buttonAutomationId, bool confirm,
        SettingReadback[]? expectedValues = null, SettingReadback[]? verificationValues = null,
        string verificationScopeId = "MainPageTab", string? verificationPage = null, string? reopenPreference = null, string? confirmationText = null) =>
        Run("nsp_finish_settings_dialog", new { scopeId, expectedTitle, action, buttonAutomationId, confirm, expectedValues,
            verificationValues, verificationScopeId, verificationPage, reopenPreference, confirmationText }, confirm, trace =>
        {
            if (action is not ("OK" or "Apply" or "Close" or "Cancel")) throw new ArgumentException("action must be OK, Apply, Close or Cancel.");
            var main = Main;
            var dialog = SettingsUi.Scope(main, scopeId, expectedTitle);
            if (action != "Cancel")
            {
                LiveUi.RequireOffline(main);
                if (expectedValues is not { Length: > 0 } || verificationValues is not { Length: > 0 })
                    throw new ArgumentException("Commit requires pending and independent verification readbacks.");
                if (verificationValues.Any(v => v.ControlKey is not null)) throw new ArgumentException("Post-commit readbacks require stable selectors.");
                if (reopenPreference is null && (verificationScopeId == scopeId || verificationPage is null))
                    throw new ArgumentException("Verify accepted values on a distinct page, or reopen a supported preference editor.");
                if (reopenPreference is not null && (action == "Apply" || verificationScopeId != scopeId || verificationPage != expectedTitle))
                    throw new ArgumentException("Preference verification requires OK/Close and the same exact dialog ID/title.");
                if (reopenPreference is not null) ValidatePreference(reopenPreference);
                if (scopeId == "PartialMapSelectionForm")
                {
                    foreach (var id in new[] { "FileVersionLabel", "PartialMapLongNameLabel" })
                        if (!expectedValues.Any(v => v.AutomationId == id && v.ControlType == "Text"))
                            throw new ArgumentException("Preset commit requires exact selected setup name and version readbacks.");
                    var selected = Items(ChoiceList(dialog, "PartialMapsListView")).Count(e =>
                        e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var raw) && ((SelectionItemPattern)raw).Current.IsSelected);
                    if (selected != 1) throw new InvalidOperationException("Preset commit requires exactly one selected native row.");
                }
                SettingsUi.CheckMany(dialog, expectedValues);
            }
            var pendingConfirmation = Confirmation(main);
            var button = pendingConfirmation is null ? LiveUi.Unique(dialog, buttonAutomationId, action, "Button") : null;
            if (pendingConfirmation is not null && action == "Cancel")
                throw new InvalidOperationException("Cancel the exact pending confirmation dialog first; no background click sent.");
            OperationContext.Checkpoint("Ready to finish settings dialog");
            using var deferred = OperationContext.DeferCancellation();
            trace.ActionAttempted = true;
            if (button is not null) LiveUi.Click(main, button);
            if (action != "Cancel")
            {
                for (var i = 0; i < 30 && pendingConfirmation is null && NativeDialogs.IsOpen(dialog); i++)
                {
                    pendingConfirmation = Confirmation(main);
                    if (pendingConfirmation is null) Thread.Sleep(100);
                    if (action == "Apply" && i >= 4) break;
                }
                if (pendingConfirmation is not null)
                {
                    var texts = LiveUi.RawChildren(pendingConfirmation).Where(e => e.Current.ControlType == ControlType.Text &&
                        !string.IsNullOrWhiteSpace(e.Current.Name)).Select(e => e.Current.Name).ToArray();
                    var message = string.Join("\n", texts);
                    if (confirmationText is null)
                        return new { success = false, errorCode = "settings_confirmation_required", actionAttempted = true,
                            confirmation = new { scopeId = "ThemedMessageBox", title = pendingConfirmation.Current.Name, text = message },
                            nextAction = "Review these exact changes. Call nsp_finish_settings_dialog with the same pending/accepted readbacks and confirmationText to accept this existing confirmation; do not resend the initial selector action." };
                    if (message != confirmationText) throw new InvalidOperationException("Pending confirmation text differs from confirmationText; no confirmation click sent.");
                    LiveUi.RequireOffline(main);
                    LiveUi.Click(main, SettingsUi.Button(pendingConfirmation, null, "OK"));
                    NativeDialogs.WaitClosed(LiveUi.Handle(pendingConfirmation));
                }
            }
            if (action != "Apply") NativeDialogs.WaitClosed(LiveUi.Handle(dialog));
            if (action == "Cancel") return new { success = true, action, closed = true, rollbackVerified = false };
            if (reopenPreference is not null)
            {
                SettingsUi.OpenPreference(main, reopenPreference);
                SettingsUi.WaitDialog(main, scopeId, expectedTitle);
            }
            var verified = SettingsUi.VerifyAccepted(main, verificationScopeId, verificationPage!, verificationValues!,
                reopenPreference is not null || action == "Apply" ? scopeId : null);
            // Leave a reopened preference editor available for inspection or restoration.
            return new { success = true, action, verification = reopenPreference is null ? "downstream_readback" : "reopened_readback",
                verified, dialogReopened = reopenPreference is not null, mapSaved = false };
        });

    private static AutomationElement? Confirmation(AutomationElement main)
    {
        // This exact NSP message-box class/title is the observed preset-change confirmation.
        // Its entire text is returned and must be supplied exactly before accepting it.
        var matches = SettingsUi.Dialogs(main).Where(e =>
            e.Current.AutomationId == "ThemedMessageBox" && e.Current.Name == "Confirmation").ToArray();
        if (matches.Length > 1) throw new InvalidOperationException("Ambiguous settings confirmations.");
        return matches.SingleOrDefault();
    }

    [McpServerTool(Name = "nsp_list_setting_choices", ReadOnly = true)]
    [Description("Inventory a native settings selector's exact ListItem names/IDs, selection state and text columns. Scope must be one exact owned dialog and listAutomationId must identify its list. Full inventory refuses truncation at 256 children; supply exactName to query matches in a single native ListView bounded at 4096 rows (channel search may scroll without filtering). No table-cell edits or image matching.")]
    public object Choices(string scopeId, string expectedTitle, string listAutomationId, string? exactName = null)
    {
        lock (MenuNavigator.InteractionLock)
        {
            var list = ChoiceList(SettingsUi.Scope(Main, scopeId, expectedTitle), listAutomationId);
            return new { scopeId, listAutomationId, choices = (exactName is null ? Items(list) : ExactItems(list, exactName)).Select(e => new {
                id = e.Current.AutomationId, name = e.Current.Name,
                selected = e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p) && ((SelectionItemPattern)p).Current.IsSelected,
                columns = LiveUi.RawChildren(e).Select(c => c.Current.Name).ToArray(),
                patterns = e.GetSupportedPatterns().Select(p => p.ProgrammaticName).ToArray() }).ToArray() };
        }
    }

    [McpServerTool(Name = "nsp_select_setting_choice", Destructive = true)]
    [Description("Select ONE exact named ListItem in an exact settings dialog/list, and verify native selected state. This stages a proprietary preset/selector choice; it DOES NOT commit the dialog. Inspect resulting metadata and use nsp_finish_settings_dialog for guarded OK plus independent downstream verification. Requires disconnected NSP. Duplicate names are refused unless an exact item ID is supplied from fresh inventory.")]
    public Task<object> SelectChoice(string scopeId, string expectedTitle, string listAutomationId, string exactName,
        bool confirm, string? itemAutomationId = null) => Run("nsp_select_setting_choice",
        new { scopeId, expectedTitle, listAutomationId, exactName, confirm, itemAutomationId }, confirm, trace =>
        {
            var main = Main;
            LiveUi.RequireOffline(main);
            var dialog = SettingsUi.Scope(main, scopeId, expectedTitle);
            var matches = ExactItems(ChoiceList(dialog, listAutomationId), exactName).Where(e =>
                (itemAutomationId is null || e.Current.AutomationId == itemAutomationId)).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException($"Expected one exact selector item, found {matches.Length}.");
            var item = matches[0];
            if (!item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var raw)) throw new InvalidOperationException("Selector item has no supported native selection pattern.");
            OperationContext.Checkpoint("Ready to select settings choice");
            using var deferred = OperationContext.DeferCancellation();
            trace.ActionAttempted = true;
            ((SelectionItemPattern)raw).Select();
            Thread.Sleep(250);
            if (!((SelectionItemPattern)raw).Current.IsSelected || item.Current.Name != exactName)
                throw new InvalidOperationException("Native selector did not retain the exact requested selection.");
            return new { success = true, selected = exactName, committed = false,
                metadata = LiveUi.NativeChildren(dialog).Where(c => c.Visible && c.ClassName.Contains("Static") && !string.IsNullOrWhiteSpace(c.Text))
                    .Select(c => new { text = c.Text }).ToArray() };
        });

    private static AutomationElement ChoiceList(AutomationElement dialog, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("An exact list ID is required.");
        var list = LiveUi.Scope(dialog, id);
        if (list.Current.ControlType != ControlType.List && list.Current.ControlType != ControlType.Table)
            throw new InvalidOperationException("Only native List/Table selectors are supported here; tuning grids use table tools.");
        return list;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr result);
    private static AutomationElement[] ExactItems(AutomationElement list, string exactName)
    {
        // Available Channels' search scrolls to matches; it does not remove the other
        // native ListView rows. Query an exact name in that single bounded native list
        // instead of truncating at the first 256 rows or scanning unrelated UIA trees.
        if (!list.Current.ClassName.Contains("SysListView32", StringComparison.OrdinalIgnoreCase))
            return Items(list).Where(e => e.Current.Name == exactName).ToArray();
        if (SendMessageTimeout(LiveUi.Handle(list), 0x1004, IntPtr.Zero, IntPtr.Zero, 2, 1500, out var count) == IntPtr.Zero ||
            count.ToInt64() < 0 || count.ToInt64() > 4096)
            throw new InvalidOperationException("Native selector is unresponsive or exceeds the 4096-row exact-query bound.");
        return list.FindAll(TreeScope.Children, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
            new PropertyCondition(AutomationElement.NameProperty, exactName))).Cast<AutomationElement>().ToArray();
    }
    private static AutomationElement[] Items(AutomationElement list)
    {
        var children = LiveUi.RawChildren(list).ToArray();
        if (children.Length >= 256) throw new InvalidOperationException("Selector exceeds bounded native inventory; narrow its search first.");
        return children.Where(e => e.Current.ControlType == ControlType.ListItem).ToArray();
    }
    private static void ValidatePreference(string item)
    {
        if (item is not ("Theme Options" or "Units Options" or "Navigation Tree Options" or "Table Text View Options"))
            throw new ArgumentException("Unsupported preference editor.");
    }
    private sealed class ActionTrace { public bool ActionAttempted; }
    private async Task<object> Run(string tool, object inputs, bool confirm, Func<ActionTrace, object> work)
    {
        object? result = null; string? error = null; var trace = new ActionTrace();
        try { if (confirm) { lock (MenuNavigator.InteractionLock) result = work(trace); } }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ElementNotAvailableException or NspWindowNotFoundException or AmbiguousNspWindowException or System.Runtime.InteropServices.COMException)
        { error = ex.Message; }
        await audit.AppendAsync(new AuditLogEntry { Timestamp = DateTimeOffset.UtcNow, ToolName = tool, DryRun = !confirm,
            Success = error is null, DriverUsed = "uia", InputsJson = JsonSerializer.Serialize(inputs),
            AfterValueJson = JsonSerializer.Serialize(result ?? new { trace.ActionAttempted, error }), ErrorMessage = error });
        return result ?? new { success = error is null, dryRun = !confirm, errorMessage = error, actionAttempted = trace.ActionAttempted,
            nextAction = trace.ActionAttempted ? "Inspect current accepted settings and audit before retrying; the action may already have applied." : null };
    }
}
