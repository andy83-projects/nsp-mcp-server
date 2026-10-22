namespace Nsp.Automation.Core;

public static class NavigationQueries
{
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
        { "Wiring", "Diagnostics", "Settings", "Setup", "Main", "Base", "Tuning", "Sensors", "Engine", "Fuel", "Ignition" };
    public static string[] Build(string search, string? destination)
    {
        search = search.Trim();
        if (string.IsNullOrWhiteSpace(search)) throw new ArgumentException("A nonempty navigation search is required.");
        if (string.IsNullOrWhiteSpace(destination) || search.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 1) return WithSearchFallbacks([search]);
        var parts = destination.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var leaf = parts[^1];
        var queries = new List<string>();
        if (Generic.Contains(leaf) && parts.Length > 1)
        {
            if (!Generic.Contains(parts[^2])) queries.Add(parts[^2]);
            queries.Add(parts[^2] + " " + leaf);
        }
        else
        {
            var words = leaf.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !w.Equals("Tuning", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (words.Length > 1) queries.Add(string.Join(' ', words));
        }
        queries.Add(search);
        return WithSearchFallbacks(queries);
    }

    // NSP sometimes rejects a complete phrase while accepting a prefix of the last
    // word or the first word alone. Broaden searches only; callers still verify the
    // original exact page/table identity before reporting success or editing.
    private static string[] WithSearchFallbacks(IEnumerable<string> queries)
    {
        var exact = WithSingularFallbacks(queries);
        var prefixes = exact.Select(q => q.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(words => words.Length > 1).ToArray();
        return exact
            .Concat(prefixes.Where(words => words[^1].Length > 2)
                .Select(words => string.Join(' ', words[..^1]) + " " + words[^1][..2]))
            .Concat(prefixes.Select(words => words[0]))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // Search fallback only: never change the exact identity used to verify a page/function.
    public static string[] WithSingularFallbacks(IEnumerable<string> queries)
    {
        var original = queries.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var fallbacks = original.Where(q => q.Length > 3 && q.EndsWith("s", StringComparison.OrdinalIgnoreCase) &&
            !q.EndsWith("ss", StringComparison.OrdinalIgnoreCase)).Select(q => q[..^1]);
        return original.Concat(fallbacks).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
