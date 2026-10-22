namespace Nsp.TuningTables;

/// <summary>
/// Registry of verified tuning tables. Accepts canonical IDs and unique exact help
/// identities/display names; never resolves partial or fuzzy table names.
/// </summary>
public sealed class TableCatalog
{
    private readonly Dictionary<string, TableDefinition> _tables;

    public TableCatalog(IEnumerable<TableDefinition>? tables = null)
    {
        _tables = (tables ?? []).ToDictionary(t => t.TableId, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<TableDefinition> ListTables() => _tables.Values;

    public TableDefinition? TryGet(string tableId)
    {
        if (string.IsNullOrWhiteSpace(tableId)) return null;
        var key = tableId.Trim();
        if (_tables.TryGetValue(key, out var def)) return def;
        var matches = _tables.Values.Where(t =>
            string.Equals(t.HelpIdentity, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.DisplayName, key, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public void Register(TableDefinition definition) => _tables[definition.TableId] = definition;
}
