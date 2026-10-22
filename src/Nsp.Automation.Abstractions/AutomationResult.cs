namespace Nsp.Automation.Core;

/// <summary>
/// Outcome of a single automation action. "Success" always means a post-action readback
/// matched the intended state — never just "the call didn't throw". This directly codifies
/// the build log's "selected but not confirmed applied" lesson (the Fan-1-to-E2 move whose
/// OK click failed with noWindowsAvailable) and the silently-ignored Cruise Control
/// dependency banner: a result is only Success when verification actually ran and matched.
/// </summary>
public sealed record AutomationResult
{
    public required bool Success { get; init; }

    /// <summary>"uia" or "fallback" — which driver actually executed this action.</summary>
    public required string DriverUsed { get; init; }

    public object? VerifiedValue { get; init; }
    public object? BeforeValue { get; init; }

    /// <summary>Any validation/dependency-warning text observed, success or not — e.g. the
    /// logged "Requires Gear Detection or Neutral Switch to be enabled" banner.</summary>
    public IReadOnlyList<string> ValidationMarkers { get; init; } = [];

    public string? ErrorMessage { get; init; }

    public TimeSpan Elapsed { get; init; }

    public static AutomationResult Succeeded(string driverUsed, object? verifiedValue, IReadOnlyList<string>? validationMarkers = null) =>
        new()
        {
            Success = true,
            DriverUsed = driverUsed,
            VerifiedValue = verifiedValue,
            ValidationMarkers = validationMarkers ?? [],
        };

    public static AutomationResult Failed(string driverUsed, string errorMessage, IReadOnlyList<string>? validationMarkers = null) =>
        new()
        {
            Success = false,
            DriverUsed = driverUsed,
            ErrorMessage = errorMessage,
            ValidationMarkers = validationMarkers ?? [],
        };
}
