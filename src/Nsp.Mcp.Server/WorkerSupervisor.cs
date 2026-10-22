using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Nsp.Automation.Core;

namespace Nsp.Mcp.Server;

public sealed class WorkerSupervisor : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object sync = new();
    private readonly SemaphoreSlim recoveryLock = new(1, 1);
    private readonly IConfiguration configuration;
    private readonly AutomationOperations operations;
    private readonly string journalPath;
    private readonly int timeoutSeconds;
    private readonly int probeTimeoutSeconds;
    private WorkerRecoveryState state;
    private Worker? worker;
    private bool recovering;
    private WorkerProbeAttempt? probeAttempt;
    private sealed record Worker(Process Process, WorkerJob Job, McpClient Client, string Generation, string ReadyFile);

    public WorkerSupervisor(IConfiguration configuration, AutomationOperations operations)
    {
        this.configuration = configuration; this.operations = operations;
        timeoutSeconds = Math.Clamp(configuration.GetValue("Nsp:WorkerTimeoutSeconds", 120), 1, 600);
        probeTimeoutSeconds = Math.Clamp(configuration.GetValue("Nsp:RecoveryProbeTimeoutSeconds", 12), 2, 30);
        journalPath = Path.GetFullPath(configuration["Nsp:WorkerJournalPath"] ?? "logs/worker-recovery.json");
        Directory.CreateDirectory(Path.GetDirectoryName(journalPath)!);
        if (File.Exists(journalPath))
        {
            try { state = JsonSerializer.Deserialize<WorkerRecoveryState>(File.ReadAllText(journalPath), JsonOptions) ?? throw new JsonException(); }
            catch (JsonException) { state = WorkerRecoveryPolicy.Quarantine(new(), new WorkerIntent(Guid.NewGuid().ToString("N"), "unknown_prior_operation", "{}", DateTimeOffset.UtcNow), "Recovery journal cannot be read; review map state before resuming."); }
        }
        else state = new();
        if (state.InFlight is not null) state = WorkerRecoveryPolicy.Quarantine(state, state.InFlight, "Supervisor stopped before the operation outcome was recorded.");
        Persist();
    }
    public void RecordStartupFailure(string reason)
    { lock (sync) { state = WorkerRecoveryPolicy.Quarantine(state, null, "Worker startup failed: " + reason); Persist(); } }
    private void Persist()
    {
        DurableJournalFile.Write(journalPath, state, JsonOptions);
    }
    public object Status()
    {
        lock (sync) return new { phase = recovering ? "restarting_worker" : state.Phase, state.RecoveryId, state.Interrupted,
            inFlight = state.InFlight, workerPid = worker?.Process.Id, workerAlive = worker is not null && !worker.Process.HasExited, generation = worker?.Generation, timeoutSeconds,
            probeTimeoutSeconds, customerActionRequired = state.Phase == "customer_action_required", nextAction = NextAction(), state.Reason,
            probe = probeAttempt?.Result, probeStartedAt = probeAttempt?.StartedAt, probeFinishedAt = probeAttempt?.FinishedAt, probeError = probeAttempt?.Error };
    }
    private string NextAction() => recovering ? "Worker recovery is in progress; poll nsp_operation_status." : (worker is null || worker.Process.HasExited) && state.Phase == "ready" ? "Call nsp_recover_worker(confirm=true); the worker is not running." : state.Phase switch {
        "ready" => "Use normal tools; never replay a timed-out write without checking its outcome.",
        "review_required" => "Use inspection/readback tools and audit to establish the interrupted outcome, then call nsp_complete_recovery. Do not blindly replay the interrupted request.",
        "customer_action_required" => "Ask the customer to check NSP or resolve its dialog. Use nsp_probe_recovery_state to recheck a live worker without clearing interrupted intent; if the worker is absent call nsp_recover_worker(confirm=true). Native heartbeat success does not prove UIA health. The MCP will not close or restart NSP.",
        _ => "Call nsp_recover_worker(confirm=true). It restarts only the automation worker, never NSP."
    };
    public async Task StartAsync()
    {
        var generation = Guid.NewGuid().ToString("N");
        var ready = Path.Combine(Path.GetDirectoryName(journalPath)!, "worker-" + generation + ".ready");
        var exe = Environment.ProcessPath!;
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Environment.CurrentDirectory,
            RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        info.ArgumentList.Add("--Nsp:Worker=true"); info.ArgumentList.Add("--Nsp:WorkerReadyFile=" + ready);
        info.ArgumentList.Add("--Nsp:WorkerToken=" + generation);
        // Only the child's endpoint differs. Keep absolute source/config paths and map identity.
        foreach (var key in new[] { "Nsp:ExpectedOpenFileName", "Nsp:ProcessNameHint", "Nsp:AuditLogDirectory", "Nsp:ClampRangesPath" })
            if (configuration[key] is { } value) info.ArgumentList.Add("--" + key + "=" + value);
        if (configuration["environment"] == "RecoveryTest")
        {
            info.ArgumentList.Add("--environment=RecoveryTest");
            foreach (var key in new[] { "Nsp:RecoveryTestHangTool", "Nsp:RecoveryTestFrozenProbe" })
                if (configuration[key] is { } value) info.ArgumentList.Add("--" + key + "=" + value);
        }
        var process = Process.Start(info) ?? throw new InvalidOperationException("Worker did not start.");
        WorkerJob? job = null;
        try
        {
            job = new WorkerJob(process);
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine("[WORKER] " + e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine("[WORKER] " + e.Data); };
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!File.Exists(ready)) { if (process.HasExited) throw new InvalidOperationException("Worker exited during startup."); await Task.Delay(100, deadline.Token); }
            var uri = new Uri((await File.ReadAllTextAsync(ready, deadline.Token)).Trim());
            if (!uri.IsLoopback || uri.Port == 6274) throw new InvalidOperationException("Worker endpoint is not isolated.");
            var client = await McpClient.CreateAsync(new HttpClientTransport(new() { Endpoint = uri, MaxReconnectionAttempts = 0, AdditionalHeaders = new Dictionary<string, string> { ["X-Nsp-Worker-Generation"] = generation } }), cancellationToken: deadline.Token);
            lock (sync) worker = new(process, job, client, generation, ready);
        }
        catch { job?.Dispose(); if (!process.HasExited) process.Kill(); process.Dispose(); throw; }
    }
    private async Task StopWorkerAsync()
    {
        Worker? old; lock (sync) old = worker;
        if (old is null) return;
        // No image/UIA calls and no NSP process termination. Prove the exact owned worker exited.
        if (!old.Process.HasExited) old.Process.Kill(entireProcessTree: false);
        await old.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        old.Job.Dispose();
        lock (sync) if (ReferenceEquals(worker, old)) worker = null;
        try { await old.Client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        if (File.Exists(old.ReadyFile)) File.Delete(old.ReadyFile);
        old.Process.Dispose();
    }
    public async Task<CallToolResult> InvokeAsync(CallToolRequestParams request)
    {
        // Diagnostic probes must not be rejected by the very recovery latch they
        // diagnose. Serialize independently, retaining the original interrupted intent.
        if (request.Name == "nsp_probe_recovery_state")
            return await ProbeOnlyAsync(request.Arguments?.TryGetValue("includeAutomation", out var include) == true && include.ValueKind == JsonValueKind.True);
        Worker current; WorkerIntent intent;
        lock (sync)
        {
            if (recovering || !WorkerRecoveryPolicy.CanDispatch(state, request.Name)) return Error("recovery_required", NextAction());
            if (worker is null || worker.Process.HasExited)
            { state = WorkerRecoveryPolicy.Quarantine(state, null, "Worker is not running."); Persist(); return Error("worker_unavailable", NextAction()); }
            if (state.InFlight is not null) return Error("automation_busy", "Another worker request is still owned; poll operation status.");
            current = worker;
            intent = new(operations.Active?.Id ?? Guid.NewGuid().ToString("N"), request.Name, JsonSerializer.Serialize(request.Arguments), DateTimeOffset.UtcNow);
            var previous = state;
            state = state with { InFlight = intent };
            try { Persist(); } // durable intent before dispatch
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nothing has been sent to the worker. A failed journal write must
                // not strand a phantom owner and block recovery/normal requests.
                state = previous;
                return Error("journal_write_failed", "Could not persist the request intent; no worker request or UI action was dispatched. Resolve journal file access before retrying. " + ex.Message);
            }
        }
        Task<CallToolResult>? call = null;
        using var monitorStop = new CancellationTokenSource();
        Task? monitor = null;
        try
        {
            call = current.Client.CallToolAsync(request, CancellationToken.None).AsTask();
            monitor = MonitorAsync(current, intent, call, monitorStop.Token);
            // A deadline quarantines rather than pretending the provider call stopped.
            var deadline = request.Name is "nsp_find_ui_element" or "nsp_inspect_region" or "nsp_probe_ui_tree"
                ? Math.Min(timeoutSeconds, 20) : timeoutSeconds;
            var result = await call.WaitAsync(TimeSpan.FromSeconds(deadline));
            lock (sync)
            {
                if (!ReferenceEquals(worker, current) || state.InFlight?.Id != intent.Id) return Error("worker_replaced", "Interrupted outcome is uncertain; inspect recovery status before any retry.");
                state = state with { InFlight = null };
                Persist();
            }
            return result;
        }
        catch (Exception ex)
        {
            _ = call?.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            lock (sync)
            {
                if (ReferenceEquals(worker, current))
                {
                    state = WorkerRecoveryPolicy.Quarantine(state, intent, ex is TimeoutException ? "Worker call exceeded its deadline; it may still be running." : "Worker call or outcome recording failed: " + ex.Message);
                    try { Persist(); }
                    catch (Exception storage) when (storage is IOException or UnauthorizedAccessException)
                    {
                        // The last durable in-flight intent remains on disk. Keep
                        // the in-memory gate closed too; never turn this into ready.
                        state = state with { Reason = state.Reason + " Recovery state could not be persisted: " + storage.Message + ". Retain the existing journal and inspect the interrupted outcome." };
                    }
                }
            }
            return Error(ex is TimeoutException ? "worker_timeout" : "worker_transport_failed", NextAction());
        }
        finally { monitorStop.Cancel(); if (monitor is not null) { try { await monitor; } catch (OperationCanceledException) { } } }
    }
    private async Task MonitorAsync(Worker current, WorkerIntent intent, Task call, CancellationToken stop)
    {
        var cancellationForwarded = false;
        while (!call.IsCompleted && !stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(250, stop);
                lock (sync) if (!ReferenceEquals(worker, current) || state.InFlight?.Id != intent.Id) return;
                var status = Decode(await CallAsync(current, "nsp_operation_status", new { }, 2));
                if (!status.TryGetProperty("active", out var active) || active.GetProperty("toolName").GetString() != intent.ToolName) continue;
                lock (sync) if (!ReferenceEquals(worker, current) || state.InFlight?.Id != intent.Id) return;
                var parent = operations.Active;
                if (parent?.Id != intent.Id) return;
                operations.ReportWorkerProgress(intent.Id, active.GetProperty("stage").GetString()!, active.GetProperty("updatedAt").GetDateTimeOffset(),
                    active.TryGetProperty("cancellationDeferred", out var deferred) && deferred.GetBoolean());
                if (parent.CancellationRequested && !cancellationForwarded)
                {
                    await CallAsync(current, "nsp_cancel_operation", new { operationId = active.GetProperty("id").GetString(), confirm = true }, 2);
                    cancellationForwarded = true;
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch { /* worker deadline remains authoritative if its status channel is also stuck */ }
        }
    }
    public async Task<object> CancelAsync(string operationId, bool confirm)
    {
        var requested = confirm && operations.RequestCancellation(operationId);
        Worker? current; lock (sync) current = state.InFlight?.Id == operationId ? worker : null;
        if (requested && current is not null)
        {
            try
            {
                var status = Decode(await CallAsync(current, "nsp_operation_status", new { }, 3));
                if (status.TryGetProperty("active", out var active))
                    await CallAsync(current, "nsp_cancel_operation", new { operationId = active.GetProperty("id").GetString(), confirm = true }, 3);
            }
            catch { /* status remains authoritative; cancellation never releases ownership */ }
        }
        return new { dryRun = !confirm, cancellationRequested = requested, operation = operations.Get(operationId), worker = Status() };
    }
    private async Task<JsonElement> ProbeAsync(bool includeAutomation = false)
    {
        Worker current; lock (sync) current = worker ?? throw new InvalidOperationException("Worker is not running.");
        lock (sync) probeAttempt = WorkerProbeAttempt.Start(); // discard stale passing evidence before starting
        try
        {
            var result = Decode(await CallAsync(current, "nsp_probe_recovery_state", new { includeAutomation }, probeTimeoutSeconds));
            lock (sync) probeAttempt = probeAttempt!.Succeeded(result);
            return result;
        }
        catch (Exception ex)
        {
            lock (sync) probeAttempt = probeAttempt!.Failed(ex.Message);
            throw;
        }
    }

    private async Task<CallToolResult> ProbeOnlyAsync(bool includeAutomation)
    {
        if (!await recoveryLock.WaitAsync(0)) return Error("recovery_busy", "Recovery/probe is already in progress; poll status.");
        try
        {
            lock (sync)
            {
                if (state.InFlight is not null) return Error("automation_busy", "A worker request still owns NSP; wait for its deadline or cancellation outcome.");
                if (worker is null || worker.Process.HasExited) return Error("worker_unavailable", "Call nsp_recover_worker(confirm=true) to start an isolated worker; interrupted intent is preserved.");
                recovering = true;
            }
            var probe = await ProbeAsync(includeAutomation);
            lock (sync)
            {
                state = WorkerRecoveryPolicy.AfterProbe(state, ProbeHealthy(probe), probe.TryGetProperty("dialogs", out var dialogs) && dialogs.GetArrayLength() != 0);
                Persist();
            }
            lock (sync) recovering = false;
            var response = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(probe.GetRawText())!;
            response["worker"] = JsonSerializer.SerializeToElement(Status(), JsonOptions);
            return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(response, JsonOptions) }] };
        }
        catch (Exception ex)
        {
            await StopWorkerAsync();
            lock (sync)
            {
                state = WorkerRecoveryPolicy.AfterProbe(state, false, false) with { Reason = "Diagnostic probe failed: " + ex.Message + ". Interrupted operation retained; ask the customer to check NSP." };
                Persist(); recovering = false;
            }
            return Error("recovery_probe_failed", NextAction());
        }
        finally { lock (sync) recovering = false; recoveryLock.Release(); }
    }
    public async Task<object> RecoverAsync(bool confirm)
    {
        if (!confirm) return new { dryRun = true, worker = Status() };
        if (!await recoveryLock.WaitAsync(0)) return new { success = false, errorCode = "recovery_busy", worker = Status() };
        try
        {
            lock (sync)
            {
                if (state.InFlight is not null) return new { success = false, errorCode = "operation_running", message = "Request cancellation or wait for the worker deadline before restarting it.", worker = Status() };
                recovering = true;
            }
            await StopWorkerAsync(); await StartAsync();
            try
            {
                var probe = await ProbeAsync();
                lock (sync)
                {
                    state = WorkerRecoveryPolicy.AfterProbe(state, ProbeHealthy(probe), probe.TryGetProperty("dialogs", out var dialogs) && dialogs.GetArrayLength() != 0);
                    Persist();
                }
            }
            catch (Exception ex)
            {
                await StopWorkerAsync();
                lock (sync) { state = WorkerRecoveryPolicy.AfterProbe(state, false, false) with { Reason = "Fresh worker could not probe NSP: " + ex.Message + ". Ask the customer to check or restart NSP." }; Persist(); }
            }
            lock (sync) recovering = false;
            return new { success = state.Phase != "customer_action_required", workerRestarted = true, ready = state.Phase == "ready", customerActionRequired = state.Phase == "customer_action_required", nextAction = NextAction(), worker = Status() };
        }
        catch (Exception ex)
        {
            lock (sync) { state = WorkerRecoveryPolicy.Quarantine(state, null, "Worker recovery failed: " + ex.Message); Persist(); recovering = false; }
            return new { success = false, worker = Status() };
        }
        finally { lock (sync) recovering = false; recoveryLock.Release(); }
    }
    public async Task<object> CompleteRecoveryAsync(string recoveryId, string reviewedOperationId, string expectedMapTitle, bool expectedUnsavedChanges, string reviewNotes, bool confirm)
    {
        if (!confirm) return new { dryRun = true, worker = Status() };
        if (!await recoveryLock.WaitAsync(0)) return new { success = false, errorCode = "recovery_busy", worker = Status() };
        try
        {
            lock (sync) { if (state.InFlight is not null || operations.Active is not null) return new { success = false, errorCode = "operation_running", worker = Status() }; recovering = true; }
            lock (sync)
                if (state.Phase != "review_required" || state.RecoveryId != recoveryId || state.Interrupted?.Id != reviewedOperationId || string.IsNullOrWhiteSpace(reviewNotes))
                { recovering = false; return new { success = false, errorCode = "stale_recovery_acknowledgement", worker = Status() }; }
            var probe = await ProbeAsync(includeAutomation: true);
            if (!ProbeHealthy(probe) || probe.GetProperty("dialogs").GetArrayLength() != 0)
            {
                lock (sync) { state = WorkerRecoveryPolicy.AfterProbe(state, ProbeHealthy(probe), probe.GetProperty("dialogs").GetArrayLength() != 0); Persist(); recovering = false; }
                return new { success = false, customerActionRequired = true, worker = Status() };
            }
            var app = probe.GetProperty("appState");
            var matches = ProbeHealthy(probe) && probe.GetProperty("dialogs").GetArrayLength() == 0 &&
                app.GetProperty("openTitle").GetString() == expectedMapTitle && app.GetProperty("hasUnsavedChanges").GetBoolean() == expectedUnsavedChanges &&
                app.TryGetProperty("ecuConnected", out var connected) && connected.ValueKind == JsonValueKind.False;
            lock (sync)
            {
                var cleared = WorkerRecoveryPolicy.Acknowledge(state, recoveryId, reviewedOperationId, matches, reviewNotes);
                using (var stream = new FileStream(Path.Combine(Path.GetDirectoryName(journalPath)!, "worker-recovery-events.jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    var line = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, recoveryId, interrupted = state.Interrupted, reviewNotes, probe }, JsonOptions) + "\n");
                    stream.Write(line); stream.Flush(true);
                }
                state = cleared; Persist(); recovering = false;
            }
            return new { success = true, worker = Status() };
        }
        catch (TimeoutException ex)
        {
            await StopWorkerAsync();
            lock (sync) { state = WorkerRecoveryPolicy.AfterProbe(state, false, false) with { Reason = "Fresh NSP probe timed out: " + ex.Message + ". Ask the customer to check or restart NSP." }; Persist(); recovering = false; }
            return new { success = false, customerActionRequired = true, worker = Status() };
        }
        catch (Exception ex) { lock (sync) recovering = false; return new { success = false, errorMessage = ex.Message, worker = Status() }; }
        finally { lock (sync) recovering = false; recoveryLock.Release(); }
    }
    private static bool ProbeHealthy(JsonElement p) => p.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
    private static async Task<CallToolResult> CallAsync(Worker w, string name, object args, int seconds)
    {
        var call = w.Client.CallToolAsync(new CallToolRequestParams { Name = name, Arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(args)) }, CancellationToken.None).AsTask();
        try { return await call.WaitAsync(TimeSpan.FromSeconds(seconds)); }
        catch { _ = call.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); throw; }
    }
    private static JsonElement Decode(CallToolResult r) { using var doc = JsonDocument.Parse(r.Content.OfType<TextContentBlock>().First().Text); return doc.RootElement.Clone(); }
    private CallToolResult Error(string code, string message) => new() { IsError = true, Content = [new TextContentBlock { Text = JsonSerializer.Serialize(new { success = false, errorCode = code, message, worker = Status() }, JsonOptions) }] };
    public async ValueTask DisposeAsync() { await StopWorkerAsync(); recoveryLock.Dispose(); }
}
