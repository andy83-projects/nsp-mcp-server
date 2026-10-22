using Nsp.TuningTables;

namespace Nsp.Automation.Core;

/// <summary>
/// Implemented by both the UIA and fallback tuning-grid drivers so the MCP tool layer
/// never needs to know which one is actually handling a given table — that routing
/// decision belongs to CapabilityRegistry alone, seeded by the Phase 0 probe.
/// </summary>
public interface ICellDriver
{
    bool CanHandle(TableDefinition table);

    Task<AutomationResult> ReadCellAsync(TableDefinition table, int row, int col, CancellationToken cancellationToken = default);

    Task<AutomationResult> WriteCellAsync(TableDefinition table, int row, int col, double appliedValue, CancellationToken cancellationToken = default);

    Task<AutomationResult> ReadTableSnapshotAsync(TableDefinition table, CancellationToken cancellationToken = default);

    Task<(double[] RowBreakpoints, double[] ColumnBreakpoints)> GetAxesAsync(TableDefinition table, CancellationToken cancellationToken = default);
}
