namespace Nsp.Automation.Core;

public sealed record AutomationOperation(string Id, string ToolName, string State, string Stage,
    DateTimeOffset StartedAt, DateTimeOffset UpdatedAt, DateTimeOffset? FinishedAt = null,
    bool CancellationRequested = false, bool CancellationDeferred = false, bool? ToolSucceeded = null, string? ResultJson = null,
    bool ResultTruncated = false, string? Error = null);

/// <summary>Process-wide admission gate. A disconnected client never releases a running UI operation.</summary>
public sealed class AutomationOperations
{
    private readonly object sync = new();
    private readonly LinkedList<AutomationOperation> history = new();
    private Lease? active;
    public string ServerInstanceId { get; } = Guid.NewGuid().ToString("N");

    public Lease? TryBegin(string toolName, CancellationToken clientCancellation = default)
    {
        lock (sync)
        {
            if (active is not null) return null;
            var now = DateTimeOffset.UtcNow;
            active = new Lease(this, new(Guid.NewGuid().ToString("N"), toolName, "running", "Admitted", now, now), clientCancellation);
            OperationContext.Current.Value = active;
            return active;
        }
    }
    public void ReportWorkerProgress(string id, string stage, DateTimeOffset updatedAt, bool deferred)
    { lock (sync) if (active?.Record.Id == id) active.Record = active.Record with { Stage = "Worker: " + stage, UpdatedAt = updatedAt, CancellationDeferred = deferred }; }
    public AutomationOperation? Active { get { lock (sync) return active?.Snapshot(); } }
    public AutomationOperation? Get(string id) { if (string.IsNullOrWhiteSpace(id)) return null; lock (sync) return active?.Record.Id == id ? active.Snapshot() : history.FirstOrDefault(r => r.Id == id); }
    public AutomationOperation[] Recent() { lock (sync) return history.ToArray(); }
    public bool RequestCancellation(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        Lease? target;
        lock (sync) target = active?.Record.Id == id ? active : null;
        if (target is null) return false;
        try { target.Cancellation.Cancel(); return true; }
        catch (ObjectDisposedException) { return false; }
    }
    public sealed class Lease : IDisposable
    {
        private readonly AutomationOperations owner;
        internal readonly CancellationTokenSource Cancellation;
        internal AutomationOperation Record;
        internal int Deferrals;
        private bool finished;
        internal Lease(AutomationOperations owner, AutomationOperation record, CancellationToken token)
        { this.owner = owner; Record = record; Cancellation = CancellationTokenSource.CreateLinkedTokenSource(token); }
        public string Id => Record.Id;
        internal AutomationOperation Snapshot() => Record with { CancellationRequested = Cancellation.IsCancellationRequested, CancellationDeferred = Record.CancellationDeferred || Volatile.Read(ref Deferrals) > 0 };
        internal void Checkpoint(string stage)
        {
            lock (owner.sync) Record = Record with { Stage = stage, UpdatedAt = DateTimeOffset.UtcNow };
            if (Volatile.Read(ref Deferrals) == 0) Cancellation.Token.ThrowIfCancellationRequested();
        }
        public void Complete(string? resultJson, bool? succeeded, string? error = null, bool cancelled = false)
        {
            lock (owner.sync)
            {
                if (finished) return;
                finished = true;
                var now = DateTimeOffset.UtcNow;
                var truncated = resultJson?.Length > 262144;
                Record = Snapshot() with { State = cancelled ? "cancelled" : error is not null || succeeded == false ? "failed" : "completed",
                    UpdatedAt = now, FinishedAt = now, CancellationDeferred = false, ToolSucceeded = succeeded, Error = error,
                    ResultJson = truncated ? resultJson![..262144] : resultJson, ResultTruncated = truncated };
                owner.history.AddFirst(Record);
                while (owner.history.Count > 32) owner.history.RemoveLast();
                owner.active = null;
            }
        }
        public void Dispose()
        {
            Complete(null, false, "Handler exited without a recorded outcome.");
            if (ReferenceEquals(OperationContext.Current.Value, this)) OperationContext.Current.Value = null;
            Cancellation.Dispose();
        }
    }
}

/// <summary>Cooperative cancellation only at known safe boundaries. Never abort verification after input.</summary>
public static class OperationContext
{
    internal static readonly AsyncLocal<AutomationOperations.Lease?> Current = new();
    public static void Checkpoint(string stage) => Current.Value?.Checkpoint(stage);
    public static IDisposable DeferCancellation() => new Deferral(Current.Value);
    private sealed class Deferral : IDisposable
    {
        private AutomationOperations.Lease? lease;
        public Deferral(AutomationOperations.Lease? lease) { this.lease = lease; if (lease is not null) Interlocked.Increment(ref lease.Deferrals); }
        public void Dispose() { if (lease is not null) { Interlocked.Decrement(ref lease.Deferrals); lease = null; } }
    }
}
