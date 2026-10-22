using Nsp.Automation.Core;
using Xunit;

namespace Nsp.Mcp.Tools.Tests;

public sealed class AutomationOperationTests
{
    [Fact] public void CancellationDoesNotReleaseRunningOperationAndResultRemainsPollable()
    {
        var operations = new AutomationOperations();
        using var client = new CancellationTokenSource();
        using var lease = operations.TryBegin("slow-read", client.Token)!;
        client.Cancel();
        Assert.Null(operations.TryBegin("write"));
        Assert.True(operations.Active!.CancellationRequested);
        Assert.Throws<OperationCanceledException>(() => OperationContext.Checkpoint("safe boundary"));
        lease.Complete("verified-result", true);
        Assert.Null(operations.Active);
        Assert.Equal("verified-result", operations.Get(lease.Id)!.ResultJson);
        Assert.Equal("completed", operations.Get(lease.Id)!.State);
    }
    [Fact] public void StartedEditCanFinishVerificationBeforeCancellationIsObserved()
    {
        var operations = new AutomationOperations();
        using var lease = operations.TryBegin("write")!;
        using (OperationContext.DeferCancellation())
        {
            Assert.True(operations.RequestCancellation(lease.Id));
            Assert.True(operations.Active!.CancellationDeferred);
            OperationContext.Checkpoint("verifying committed edit");
            Assert.Null(operations.TryBegin("second write"));
        }
        Assert.Throws<OperationCanceledException>(() => OperationContext.Checkpoint("next safe boundary"));
        Assert.False(operations.RequestCancellation("wrong-id"));
    }
    [Fact] public void AbandonedHandlerReleasesGateWithFailureAndHistoryIsBounded()
    {
        var operations = new AutomationOperations();
        string id;
        using (var lease = operations.TryBegin("fault")!) { id = lease.Id; }
        Assert.Equal("failed", operations.Get(id)!.State);
        for (var i = 0; i < 35; i++) { using var lease = operations.TryBegin("read")!; lease.Complete("{}", true); }
        Assert.Equal(32, operations.Recent().Length);
        Assert.Null(operations.Get(id));
    }
    [Fact] public void OnlyOneConcurrentCallerIsAdmitted()
    {
        var operations = new AutomationOperations();
        var leases = Enumerable.Range(0, 32).AsParallel().Select(_ => operations.TryBegin("read")).Where(l => l is not null).ToArray();
        Assert.Single(leases);
        leases[0]!.Dispose();
    }
    [Fact] public void BroadQueriesNarrowFirstAndRetainCallerFallback()
    {
        Assert.Equal(new[] { "Fuel Pressure", "Fuel Pressure Diagnostics", "Fuel", "Fuel Pressure Diagnostic", "Fuel Pr", "Fuel Pressure Di" }, NavigationQueries.Build("Fuel", "Sensors - Fuel Pressure - Diagnostics"));
        Assert.Equal(new[] { "Base Fuel", "Base", "Base Fu" }, NavigationQueries.Build("Base", "Base Fuel Tuning"));
        Assert.Equal(new[] { "Dead Time", "Dead Ti", "Dead" }, NavigationQueries.Build("Dead Time", "Injection Stage 1 Dead Time"));
        Assert.Throws<ArgumentException>(() => NavigationQueries.Build(" ", "anything"));
    }
}
