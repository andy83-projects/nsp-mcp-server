using System.Diagnostics;

namespace Nsp.Automation.Core;

/// <summary>
/// Every automation action — menu click, field set, function toggle, cell write — follows
/// the same mandatory precondition -> act -> settle -> verify -> error-check pipeline.
/// This is the single biggest lesson from the manual build logs: a click "succeeding" is
/// not the same as the intended state actually being applied. No subclass may skip straight
/// from Act to "done".
/// </summary>
public abstract class AutomationAction
{
    public async Task<AutomationResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var precondition = await CheckPreconditionAsync(cancellationToken);
        if (!precondition.Success)
        {
            return precondition with { Elapsed = stopwatch.Elapsed };
        }

        await ActAsync(cancellationToken);

        // Bounded poll for redraw completion, never a fixed sleep — "redraws can lag" was
        // observed independent of any host-capture issue, so this still applies in-VM.
        await SettleAsync(cancellationToken);

        var verification = await VerifyAsync(cancellationToken);
        var markers = await DetectValidationMarkersAsync(cancellationToken);

        return verification with
        {
            ValidationMarkers = markers,
            Elapsed = stopwatch.Elapsed,
        };
    }

    protected abstract Task<AutomationResult> CheckPreconditionAsync(CancellationToken cancellationToken);

    protected abstract Task ActAsync(CancellationToken cancellationToken);

    protected virtual Task SettleAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected abstract Task<AutomationResult> VerifyAsync(CancellationToken cancellationToken);

    /// <summary>Scans for validation/dependency banners and red configuration-error markers.
    /// Called every time, success or failure — a click that "succeeds" but triggers a
    /// validation banner must still surface that banner to the caller.</summary>
    protected abstract Task<IReadOnlyList<string>> DetectValidationMarkersAsync(CancellationToken cancellationToken);
}
