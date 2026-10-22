namespace Nsp.Automation.Core;

public sealed record WorkerIntent(string Id, string ToolName, string ArgumentsJson, DateTimeOffset StartedAt);
public sealed record WorkerRecoveryState(string Phase = "ready", string? RecoveryId = null,
    WorkerIntent? Interrupted = null, WorkerIntent? InFlight = null, string? Reason = null);

/// <summary>Fail closed across worker/supervisor restarts; recovery never replays an interrupted request.</summary>
public static class WorkerRecoveryPolicy
{
    private static readonly HashSet<string> InspectionTools = new(StringComparer.Ordinal) {
        "nsp_get_app_state", "nsp_probe_recovery_state", "nsp_read_cell", "nsp_read_table_snapshot",
        "nsp_get_table_axes", "nsp_list_settings", "nsp_list_setting_options", "nsp_list_setting_choices", "nsp_find_ui_element",
        "nsp_inspect_region", "nsp_native_region"
    };
    public static bool CanDispatch(WorkerRecoveryState state, string name) => name == "nsp_probe_recovery_state" || state.Phase == "ready" ||
        state.Phase == "review_required" && InspectionTools.Contains(name);
    public static WorkerRecoveryState Quarantine(WorkerRecoveryState state, WorkerIntent? intent, string reason) =>
        state with { Phase = "recovery_required", RecoveryId = state.RecoveryId ?? Guid.NewGuid().ToString("N"),
            Interrupted = state.Interrupted ?? intent, InFlight = null, Reason = reason };
    public static WorkerRecoveryState AfterProbe(WorkerRecoveryState state, bool healthy, bool dialogs) => !healthy ||
        dialogs && (state.RecoveryId is not null || state.Interrupted is not null || state.InFlight is not null)
        ? state with { Phase = "customer_action_required", Reason = dialogs ? "NSP has a pending dialog; ask the customer to check NSP." : "NSP did not pass the responsiveness probe; ask the customer to check or restart NSP." }
        : state.Interrupted is not null
            ? state with { Phase = "review_required", Reason = "Inspect interrupted operation outcome and explicitly complete recovery; never blindly replay it." }
            : new();
    public static WorkerRecoveryState Acknowledge(WorkerRecoveryState state, string recoveryId, string? reviewedOperationId, bool freshProbeMatches, string reviewNotes)
    {
        if (state.Phase != "review_required" || state.RecoveryId != recoveryId || state.Interrupted?.Id != reviewedOperationId ||
            !freshProbeMatches || string.IsNullOrWhiteSpace(reviewNotes))
            throw new InvalidOperationException("Recovery acknowledgement is stale, incomplete, or NSP has not passed the fresh state checks.");
        return new();
    }
}
