using System.Text.Json.Serialization;

namespace Nsp.Audit;

/// <summary>
/// One immutable record of a mutating (or dry-run) action taken against NSP.
/// Serialized as a single line of a JSONL audit log — this file is the durable
/// source of truth, never an in-memory structure.
/// </summary>
public sealed record AuditLogEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string ToolName { get; init; }
    public required string InputsJson { get; init; }
    public required bool DryRun { get; init; }
    public required bool Success { get; init; }

    /// <summary>"uia" or "fallback" — which automation driver actually executed this action.</summary>
    public string? DriverUsed { get; init; }

    /// <summary>For table-cell writes: where the clamp range came from ("live-ui", "local-config", or "rejected-no-range").</summary>
    public string? ClampProvenance { get; init; }

    public double? ClampMin { get; init; }
    public double? ClampMax { get; init; }

    public string? BeforeValueJson { get; init; }
    public string? RequestedValueJson { get; init; }
    public string? AppliedValueJson { get; init; }
    public string? AfterValueJson { get; init; }

    public bool WasClamped { get; init; }

    public IReadOnlyList<string> ValidationMarkersObserved { get; init; } = [];

    public string? ErrorMessage { get; init; }
}
