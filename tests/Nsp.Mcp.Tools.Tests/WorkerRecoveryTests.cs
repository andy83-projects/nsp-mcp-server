using Nsp.Automation.Core;
using Xunit;

namespace Nsp.Mcp.Tools.Tests;

public sealed class WorkerRecoveryTests
{
    [Fact] public void RoutineProbeDoesNotQuarantineAnOrdinaryDialog()
    {
        Assert.Equal("ready", WorkerRecoveryPolicy.AfterProbe(new(), true, true).Phase);
        // Also repairs the old probe's false alarm without deleting a recovery journal.
        Assert.Equal("ready", WorkerRecoveryPolicy.AfterProbe(new("customer_action_required"), true, true).Phase);
        Assert.Equal("customer_action_required", WorkerRecoveryPolicy.AfterProbe(new(), false, true).Phase);
        var unknown = WorkerRecoveryPolicy.Quarantine(new(), null, "unknown journal state");
        Assert.Equal("customer_action_required", WorkerRecoveryPolicy.AfterProbe(unknown, true, true).Phase);
        Assert.Equal("customer_action_required", WorkerRecoveryPolicy.AfterProbe(new(InFlight: Intent), true, true).Phase);
    }
    [Theory]
    [InlineData("recovery_required")]
    [InlineData("customer_action_required")]
    [InlineData("review_required")]
    public void DiagnosticProbeRemainsAvailableWithoutUnlockingMutations(string phase)
    {
        var state = new WorkerRecoveryState(phase, "same-recovery-id", Intent);
        Assert.True(WorkerRecoveryPolicy.CanDispatch(state, "nsp_probe_recovery_state"));
        Assert.False(WorkerRecoveryPolicy.CanDispatch(state, "nsp_write_cell"));
        var healthy = WorkerRecoveryPolicy.AfterProbe(state, true, false);
        Assert.Equal("review_required", healthy.Phase);
        Assert.Equal(state.RecoveryId, healthy.RecoveryId);
        Assert.Equal(Intent, healthy.Interrupted);
        Assert.False(WorkerRecoveryPolicy.CanDispatch(healthy, "nsp_write_cell"));
    }
    private static readonly WorkerIntent Intent = new("interrupted-id", "nsp_write_cell", "{\"value\":1.5}", DateTimeOffset.UtcNow);
    [Fact] public void JournalRoundTripRetainsUncertainOperationUntilExplicitReview()
    {
        var blocked = WorkerRecoveryPolicy.AfterProbe(WorkerRecoveryPolicy.Quarantine(new(), Intent, "sleep/wake timeout"), false, false);
        var restored = System.Text.Json.JsonSerializer.Deserialize<WorkerRecoveryState>(System.Text.Json.JsonSerializer.Serialize(blocked))!;
        var checkedState = WorkerRecoveryPolicy.AfterProbe(restored, true, false);
        Assert.Equal(blocked.RecoveryId, checkedState.RecoveryId);
        Assert.Equal(Intent, checkedState.Interrupted);
        Assert.Equal("review_required", checkedState.Phase);
        Assert.False(WorkerRecoveryPolicy.CanDispatch(checkedState, "nsp_set_io_assignment"));
    }
    [Fact] public void InterruptQuarantinesAndPreservesOriginalIntentAcrossFurtherFailures()
    {
        var state = WorkerRecoveryPolicy.Quarantine(new(InFlight: Intent), Intent, "deadline");
        Assert.False(WorkerRecoveryPolicy.CanDispatch(state, "nsp_write_cell"));
        Assert.False(WorkerRecoveryPolicy.CanDispatch(state, "nsp_read_cell"));
        var again = WorkerRecoveryPolicy.Quarantine(state, Intent with { Id = "later-read" }, "probe failed");
        Assert.Equal(Intent, again.Interrupted);
        Assert.Equal(state.RecoveryId, again.RecoveryId);
    }
    [Fact] public void HealthyWorkerRestartDoesNotAuthorizeReplayOfUncertainWrite()
    {
        var state = WorkerRecoveryPolicy.AfterProbe(WorkerRecoveryPolicy.Quarantine(new(), Intent, "crash"), true, false);
        Assert.Equal("review_required", state.Phase);
        Assert.True(WorkerRecoveryPolicy.CanDispatch(state, "nsp_read_table_snapshot"));
        Assert.True(WorkerRecoveryPolicy.CanDispatch(state, "nsp_list_setting_choices"));
        foreach (var tool in new[] { "nsp_open_settings_dialog", "nsp_finish_settings_dialog", "nsp_select_setting_choice", "nsp_activate_setting_action" })
            Assert.False(WorkerRecoveryPolicy.CanDispatch(state, tool));
        Assert.False(WorkerRecoveryPolicy.CanDispatch(state, "nsp_write_cell"));
        Assert.False(WorkerRecoveryPolicy.CanDispatch(state, "nsp_send_keyboard_shortcut"));
        Assert.Throws<InvalidOperationException>(() => WorkerRecoveryPolicy.Acknowledge(state, "stale-id", Intent.Id, true, "verified"));
        Assert.Throws<InvalidOperationException>(() => WorkerRecoveryPolicy.Acknowledge(state, state.RecoveryId!, "wrong-op", true, "verified"));
        Assert.Throws<InvalidOperationException>(() => WorkerRecoveryPolicy.Acknowledge(state, state.RecoveryId!, Intent.Id, false, "verified"));
        Assert.Throws<InvalidOperationException>(() => WorkerRecoveryPolicy.Acknowledge(state, state.RecoveryId!, Intent.Id, true, ""));
        var ready = WorkerRecoveryPolicy.Acknowledge(state, state.RecoveryId!, Intent.Id, true, "Readback and audit reviewed");
        Assert.Equal("ready", ready.Phase);
    }
    [Fact] public void FrozenAppOrPendingDialogRequiresCustomerRatherThanAnUnlock()
    {
        var state = WorkerRecoveryPolicy.Quarantine(new(), Intent, "crash");
        foreach (var blocked in new[] { WorkerRecoveryPolicy.AfterProbe(state, false, false), WorkerRecoveryPolicy.AfterProbe(state, true, true) })
        {
            Assert.Equal("customer_action_required", blocked.Phase);
            Assert.Contains("customer", blocked.Reason!);
            Assert.False(WorkerRecoveryPolicy.CanDispatch(blocked, "nsp_write_cell"));
            Assert.Throws<InvalidOperationException>(() => WorkerRecoveryPolicy.Acknowledge(blocked, blocked.RecoveryId!, Intent.Id, true, "ignore freeze"));
        }
    }
    [Fact] public void IdleHealthyRestartCanResumeWithoutInventingAnInterruptedWrite()
    {
        Assert.Equal("ready", WorkerRecoveryPolicy.AfterProbe(new(), true, false).Phase);
        Assert.True(WorkerRecoveryPolicy.CanDispatch(new(), "nsp_write_cell"));
    }
}
