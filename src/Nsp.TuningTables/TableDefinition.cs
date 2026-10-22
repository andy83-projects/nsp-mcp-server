namespace Nsp.TuningTables;

/// <summary>
/// Describes one NSP tuning table (fuel/VE, ignition, boost, etc.) — its axes and
/// how its cells are addressed by the automation layer.
/// </summary>
public sealed record TableDefinition
{
    /// <summary>Stable id used in MCP tool calls, e.g. "fuel_ve_table".</summary>
    public required string TableId { get; init; }

    /// <summary>Human-readable name as shown in NSP's UI.</summary>
    public required string DisplayName { get; init; }

    public required string RowAxisLabel { get; init; }
    public required string ColumnAxisLabel { get; init; }
    public required string CellUnit { get; init; }

    /// <summary>
    /// Which automation driver currently handles this table — set by the Phase 0
    /// probe's findings (see docs/PHASE0_FINDINGS.md), overridable here.
    /// </summary>
    public required TableDriverKind DriverKind { get; init; }

    /// <summary>
    /// UIA AutomationId of the grid root, when DriverKind is Uia. Null otherwise.
    /// </summary>
    public string? UiaGridAutomationId { get; init; }
    public string? NavigationSearchText { get; init; }
    public string? HelpIdentity { get; init; }
    public int CellDecimalPlaces { get; init; } = 3;
    /// <summary>Verified one-cell tuning grid with neither axis enabled. Both live header sets must remain empty.</summary>
    public bool HasNoAxes { get; init; }
    /// <summary>Verified display order for tables whose UIA row headers omit numeric values.</summary>
    public bool ReadDescendingRowsFromAxisSetup { get; init; }
    public bool SupportsSliceAxis { get; init; }
    /// <summary>Explicitly enabled only after native edit/interpolation/restore validation.
    /// Display units can be nonlinear; numeric headers alone are insufficient.</summary>
    public bool SupportsRowBreakpointEdits { get; init; }
    public bool SupportsColumnBreakpointEdits { get; init; }

    public bool MatchesHelpDocument(string helpText)
    {
        static string Normalize(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var identity = helpText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return !string.IsNullOrWhiteSpace(HelpIdentity) && identity is not null && Normalize(identity) == Normalize(HelpIdentity);
    }
}

public enum TableDriverKind
{
    Uia,
    Fallback,
}
