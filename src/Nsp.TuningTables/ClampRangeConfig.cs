using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nsp.TuningTables;

/// <summary>
/// User-editable local fallback for clamp ranges, loaded from clamp-ranges.json.
/// Ships with schema + example rows only — no real tuning tables are configured in
/// the build yet, so there is deliberately no real range data baked in.
/// </summary>
public sealed class ClampRangeConfig
{
    private readonly Dictionary<string, ClampRangeConfigEntry> _entries;

    private ClampRangeConfig(Dictionary<string, ClampRangeConfigEntry> entries)
    {
        _entries = entries;
    }

    public static ClampRangeConfig Empty() => new(new Dictionary<string, ClampRangeConfigEntry>(StringComparer.OrdinalIgnoreCase));

    public static async Task<ClampRangeConfig> LoadAsync(string jsonFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(jsonFilePath))
        {
            return Empty();
        }

        using var stream = File.OpenRead(jsonFilePath);
        var doc = await JsonSerializer.DeserializeAsync<ClampRangeConfigFile>(stream, cancellationToken: cancellationToken);

        var entries = new Dictionary<string, ClampRangeConfigEntry>(StringComparer.OrdinalIgnoreCase);
        if (doc?.Tables is not null)
        {
            foreach (var (tableId, entry) in doc.Tables)
            {
                entries[tableId] = entry;
            }
        }

        return new ClampRangeConfig(entries);
    }

    public bool TryGetRange(string tableId, out double min, out double max, out string source)
    {
        if (_entries.TryGetValue(tableId, out var entry))
        {
            min = entry.Min;
            max = entry.Max;
            source = entry.Source;
            return true;
        }

        min = max = 0;
        source = "";
        return false;
    }

    private sealed class ClampRangeConfigFile
    {
        [JsonPropertyName("tables")]
        public Dictionary<string, ClampRangeConfigEntry>? Tables { get; set; }
    }

    private sealed class ClampRangeConfigEntry
    {
        [JsonPropertyName("min")]
        public double Min { get; set; }

        [JsonPropertyName("max")]
        public double Max { get; set; }

        /// <summary>e.g. "Haltech published range", "owner-set safety margin", "unverified default — placeholder".</summary>
        [JsonPropertyName("source")]
        public string Source { get; set; } = "local-config";
    }
}
