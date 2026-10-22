using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Nsp.Audit;
using Nsp.Automation.Core;

namespace Nsp.Mcp.Tools;

/// <summary>
/// Application-level NSP commands — open/save, Function Manager, I/O assignments, generic
/// scalar fields, validation-error listing. Every mutating tool requires confirm=true to
/// actually act; without it, it returns a dry-run preview only and still writes an audit
/// entry (tagged as a dry run).
/// </summary>
[McpServerToolType]
public sealed class AppCommandTools(NspAppController controller, AuditLogger auditLogger)
{
    [McpServerTool(Name = "nsp_get_app_state", ReadOnly = true)]
    [Description("Reports whether the NSP window was found, the open file path, unsaved-changes state, and any visible validation/dependency warnings. Call this before issuing a batch of commands.")]
    public async Task<object> GetAppState(CancellationToken cancellationToken) =>
        ToolResultJson.From(await controller.App.GetAppStateAsync(cancellationToken));

    [McpServerTool(Name = "nsp_open_map")]
    [Description("Opens the given .nexmap file in NSP, or verifies it is already open and matching.")]
    public async Task<object> OpenMap(
        [Description("Full path to the .nexmap file inside the Windows guest, e.g. C:\\Maps\\Example_Offline_Draft.nexmap")]
        string filePath,
        CancellationToken cancellationToken)
    {
        var result = await controller.App.OpenMapAsync(filePath, cancellationToken);
        await LogAsync("nsp_open_map", new { filePath }, dryRun: false, result, cancellationToken);
        return ToolResultJson.From(result);
    }

    [McpServerTool(Name = "nsp_save_map", Destructive = true)]
    [Description("Saves the currently open map (Ctrl+S equivalent) and verifies the title's unsaved-changes asterisk clears. Requires confirm=true; without it, returns a dry-run preview.")]
    public async Task<object> SaveMap(
        [Description("Must be true to actually save.")] bool confirm,
        CancellationToken cancellationToken)
    {
        var result = await controller.App.SaveMapAsync(confirm, cancellationToken);
        await LogAsync("nsp_save_map", new { confirm }, dryRun: !confirm, result, cancellationToken);
        return ToolResultJson.From(result);
    }

    [McpServerTool(Name = "nsp_open_function_manager", ReadOnly = true)]
    [Description("Opens NSP's F4 Function Manager and returns the list of functions with their enabled/disabled state.")]
    public async Task<object> OpenFunctionManager(CancellationToken cancellationToken) =>
        ToolResultJson.From(await controller.App.OpenFunctionManagerAsync(cancellationToken));

    [McpServerTool(Name = "nsp_set_function_enabled", Destructive = true)]
    [Description("Enables or disables a named function in the Function Manager, then reports any validation/dependency warning that appears afterward (e.g. \"Requires Gear Detection or Neutral Switch to be enabled\"). Requires confirm=true.")]
    public async Task<object> SetFunctionEnabled(
        string functionName,
        bool enabled,
        [Description("Must be true to actually act.")] bool confirm,
        CancellationToken cancellationToken)
    {
        var result = await controller.App.SetFunctionEnabledAsync(functionName, enabled, confirm, cancellationToken);
        await LogAsync("nsp_set_function_enabled", new { functionName, enabled, confirm }, dryRun: !confirm, result, cancellationToken);
        return ToolResultJson.From(result);
    }

    [McpServerTool(Name = "nsp_get_function_state", ReadOnly = true)]
    [Description("Opens the Function Manager and filters its native function list to read the exact checkbox and validation text. Changes the current view/search; this is a UI lookup, not a cheap cached property read. Does not toggle a function or save a map.")]
    public async Task<object> GetFunctionState(string functionName, CancellationToken cancellationToken) =>
        ToolResultJson.From(await controller.App.GetFunctionStateAsync(functionName, cancellationToken));

    [McpServerTool(Name = "nsp_set_io_assignment", Destructive = true)]
    [Description("Assigns only a physical pin/resource to one exact connection field, with independent assignment readback. Does not configure Edge Select, Pull Up, Button Mode or other row settings; configure those separately. Searches native resource captions without the picker's function-text filter. Occupied/ambiguous resources are refused. Requires confirm=true; no save/upload.")]
    public async Task<object> SetIoAssignment(
        string functionName,
        string connectionField,
        string pinOrResourceId,
        [Description("Must be true to actually act.")] bool confirm,
        CancellationToken cancellationToken)
    {
        var result = await controller.App.SetIoAssignmentAsync(functionName, connectionField, pinOrResourceId, confirm, cancellationToken);
        await LogAsync("nsp_set_io_assignment", new { functionName, connectionField, pinOrResourceId, confirm }, dryRun: !confirm, result, cancellationToken);
        return ToolResultJson.From(result);
    }

    [McpServerTool(Name = "nsp_get_io_assignment", ReadOnly = true)]
    [Description("Current pin/resource assignment for a function's connection field.")]
    public async Task<object> GetIoAssignment(string functionName, string connectionField, CancellationToken cancellationToken) =>
        ToolResultJson.From(await controller.App.GetIoAssignmentAsync(functionName, connectionField, cancellationToken));

    [McpServerTool(Name = "nsp_set_field_value", Destructive = true)]
    [Description("Sets a generic labeled scalar field's value — gear ratios, soft-start duration, dwell points, thresholds, etc. Not for tuning-table cells; use nsp_write_cell for those. Requires confirm=true.")]
    public async Task<object> SetFieldValue(
        string dialogPath,
        string fieldLabel,
        string value,
        [Description("Must be true to actually act.")] bool confirm,
        CancellationToken cancellationToken)
    {
        var result = await controller.App.SetFieldValueAsync(dialogPath, fieldLabel, value, confirm, cancellationToken);
        await LogAsync("nsp_set_field_value", new { dialogPath, fieldLabel, value, confirm }, dryRun: !confirm, result, cancellationToken);
        return ToolResultJson.From(result);
    }

    [McpServerTool(Name = "nsp_get_field_value", ReadOnly = true)]
    [Description("Current value of a generic labeled scalar field.")]
    public async Task<object> GetFieldValue(string dialogPath, string fieldLabel, CancellationToken cancellationToken) =>
        ToolResultJson.From(await controller.App.GetFieldValueAsync(dialogPath, fieldLabel, cancellationToken));

    [McpServerTool(Name = "nsp_list_validation_errors", ReadOnly = true)]
    [Description("Read validation-like accessible text in the current UI only. Does not enumerate custom red icons or the whole map's configuration errors. An empty result does NOT establish that the map is valid or complete; mapValidity is unknown. No edits/save/upload.")]
    public async Task<object> ListValidationErrors(CancellationToken cancellationToken) =>
        ToolResultJson.From(await controller.App.ListValidationErrorsAsync(cancellationToken));

    private async Task LogAsync(string toolName, object inputs, bool dryRun, AutomationResult result, CancellationToken cancellationToken)
    {
        await auditLogger.AppendAsync(new AuditLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            ToolName = toolName,
            InputsJson = JsonSerializer.Serialize(inputs),
            DryRun = dryRun,
            Success = result.Success,
            DriverUsed = result.DriverUsed,
            AfterValueJson = result.VerifiedValue is null ? null : JsonSerializer.Serialize(result.VerifiedValue),
            ValidationMarkersObserved = result.ValidationMarkers,
            ErrorMessage = result.ErrorMessage,
        }, CancellationToken.None);
    }
}
