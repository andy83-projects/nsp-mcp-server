using System.ComponentModel;
using ModelContextProtocol.Server;
using Nsp.Audit;
using Nsp.Automation.Core;
using Nsp.TuningTables;

namespace Nsp.Mcp.Server;

[McpServerToolType]
public sealed class SupervisorTools(WorkerSupervisor supervisor, AutomationOperations operations, AuditLogger audit, TableCatalog catalog)
{
    [McpServerTool(Name = "nsp_operation_status", ReadOnly = true)]
    [Description("Nonblocking supervisor status, available even if UIA/NSP is stuck. Reports active/recent calls plus worker phase, recovery ID, interrupted intent and nextAction. After worker_timeout/recovery_required call nsp_recover_worker, not the interrupted mutation. includeResult=true with operationId retrieves a completed result. Worker journal survives supervisor restarts; recent result history does not.")]
    public object Status(string? operationId = null, bool includeResult = false) => new {
        serverInstanceId = operations.ServerInstanceId, active = operations.Active,
        operation = operationId is null ? null : operations.Get(operationId) is { } r ? includeResult ? r : r with { ResultJson = null } : null,
        recent = operations.Recent().Select(r => r with { ResultJson = null }).ToArray(), worker = supervisor.Status() };

    [McpServerTool(Name = "nsp_cancel_operation", Destructive = true)]
    [Description("Request cooperative cancellation of an exact active operation and forward it to the worker. Does not release ownership, roll back edits, kill NSP or interrupt a blocked provider call. If the worker deadline is exceeded, use nsp_recover_worker instead of replaying the request.")]
    public Task<object> Cancel(string operationId, bool confirm) => supervisor.CancelAsync(operationId, confirm);

    [McpServerTool(Name = "nsp_recover_worker", Destructive = true)]
    [Description("Restart ONLY the isolated automation worker after worker_timeout/worker failure, or while idle. Requires confirm=true. Never closes/restarts NSP or replays a request. Proves old worker exit and runs a native probe from a fresh worker, without UIA. Native success does not prove accessibility health. nsp_probe_recovery_state remains available during recovery to recheck a live worker; use includeAutomation=true for a bounded fresh UIA check. If customerActionRequired, ask the customer to check NSP or resolve its dialog. If review_required, inspect interrupted outcome and call nsp_complete_recovery, which independently requires fresh UIA verification of disconnected ECU and no dialogs. Journal intent survives server restarts; never delete it to bypass review.")]
    public Task<object> Recover(bool confirm) => supervisor.RecoverAsync(confirm);

    [McpServerTool(Name = "nsp_complete_recovery", Destructive = true)]
    [Description("Re-enable normal commands ONLY after reviewing the exact interrupted operation with inspection/readback and durable audit. Requires current recoveryId, reviewedOperationId from status, exact current full map title, expected dirty state, substantive reviewNotes and confirm=true. Fresh probe must find responsive NSP, no owned dialogs and disconnected ECU. This acknowledges review, not rollback. Never use this to blindly replay an uncertain mutation.")]
    public Task<object> Complete(string recoveryId, string reviewedOperationId, string expectedMapTitle, bool expectedUnsavedChanges, string reviewNotes, bool confirm) =>
        supervisor.CompleteRecoveryAsync(recoveryId, reviewedOperationId, expectedMapTitle, expectedUnsavedChanges, reviewNotes, confirm);

    [McpServerTool(Name = "nsp_list_tables", ReadOnly = true)]
    [Description("List known tables and validated editing capabilities without accessing the worker or NSP.")]
    public object Tables() => new { tables = catalog.ListTables().Select(t => new { t.TableId, t.DisplayName, t.RowAxisLabel, t.ColumnAxisLabel, t.CellUnit,
        t.CellDecimalPlaces, t.HasNoAxes, t.HelpIdentity, t.NavigationSearchText, t.SupportsSliceAxis, t.SupportsRowBreakpointEdits, t.SupportsColumnBreakpointEdits, driverKind = t.DriverKind.ToString() }) };

    [McpServerTool(Name = "nsp_get_audit_log", ReadOnly = true)]
    [Description("Read durable audit even while worker/NSP is stuck. An interrupted worker may have changed NSP before appending its outcome; missing audit is not proof that no edit happened.")]
    public async Task<object> Audit(string? since = null, string? toolNameFilter = null, int limit = 100, CancellationToken cancellationToken = default) =>
        new { entries = await audit.QueryAsync(since is null ? null : DateTimeOffset.Parse(since), toolNameFilter, Math.Clamp(limit, 1, 1000), cancellationToken) };
}
