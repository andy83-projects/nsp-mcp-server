using Nsp.TuningTables;

namespace Nsp.Automation.Core;

/// <summary>
/// Per-table UIA-vs-fallback routing. Seeded from docs/PHASE0_FINDINGS.md once the probe
/// runs; overridable per table without touching code, since it's plausible some tables
/// are UIA-addressable and others (e.g. a custom-rendered 3D map) are not.
/// </summary>
public sealed class CapabilityRegistry(TableCatalog catalog)
{
    private readonly Dictionary<string, TableDriverKind> _overrides = new(StringComparer.OrdinalIgnoreCase);

    public TableDriverKind GetDriverKind(string tableId)
    {
        if (_overrides.TryGetValue(tableId, out var overridden))
        {
            return overridden;
        }

        var definition = catalog.TryGet(tableId)
            ?? throw new InvalidOperationException($"Unknown table '{tableId}' — register it in TableCatalog first.");

        return definition.DriverKind;
    }

    public void Override(string tableId, TableDriverKind kind) => _overrides[tableId] = kind;
}
