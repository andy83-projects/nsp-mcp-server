using Nsp.TuningTables;

namespace Nsp.Automation.Core;

/// <summary>
/// Facade the MCP tool layer talks to. Forwards application-level calls straight to the
/// configured IAppDriver, and routes table-cell calls to whichever ICellDriver
/// CapabilityRegistry says should handle that table — callers never need to know whether
/// a given region is UIA- or fallback-backed.
/// </summary>
public sealed class NspAppController(
    IAppDriver appDriver,
    IEnumerable<ICellDriver> cellDrivers,
    CapabilityRegistry capabilityRegistry,
    TableCatalog tableCatalog)
{
    public IAppDriver App => appDriver;

    public TableCatalog Tables => tableCatalog;

    public ICellDriver ResolveCellDriver(string tableId)
    {
        var definition = tableCatalog.TryGet(tableId)
            ?? throw new InvalidOperationException($"Unknown table '{tableId}'.");

        var kind = capabilityRegistry.GetDriverKind(definition.TableId);
        var effectiveDefinition = definition with { DriverKind = kind };

        return cellDrivers.FirstOrDefault(d => d.CanHandle(effectiveDefinition))
            ?? throw new InvalidOperationException(
                $"No registered ICellDriver can handle table '{tableId}' with driver kind {kind}.");
    }
}
