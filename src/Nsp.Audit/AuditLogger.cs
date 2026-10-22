using System.Text.Json;

namespace Nsp.Audit;

/// <summary>
/// Append-only JSONL audit log. One line per mutating action, including dry-runs.
/// The file on disk is the source of truth; nothing is held only in memory, so a
/// server restart never loses history.
/// </summary>
public sealed class AuditLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly string _logDirectory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public AuditLogger(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
    }

    private string CurrentLogFilePath =>
        Path.Combine(_logDirectory, $"audit-{DateTimeOffset.UtcNow:yyyyMMdd}.jsonl");

    public async Task AppendAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        var line = JsonSerializer.Serialize(entry, JsonOptions);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(CurrentLogFilePath, line + Environment.NewLine, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Reads entries across all audit log files, newest first, optionally filtered.
    /// Intended for the nsp_get_audit_log tool — a simple linear scan is fine at this scale.
    /// </summary>
    public async Task<IReadOnlyList<AuditLogEntry>> QueryAsync(
        DateTimeOffset? since = null,
        string? toolNameFilter = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_logDirectory))
        {
            return [];
        }

        var results = new List<AuditLogEntry>();

        var files = Directory.GetFiles(_logDirectory, "audit-*.jsonl")
            .OrderByDescending(f => f, StringComparer.Ordinal);

        foreach (var file in files)
        {
            var lines = await File.ReadAllLinesAsync(file, cancellationToken);

            foreach (var line in lines.AsEnumerable().Reverse())
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var entry = JsonSerializer.Deserialize<AuditLogEntry>(line, JsonOptions);
                if (entry is null)
                {
                    continue;
                }

                if (since is not null && entry.Timestamp < since)
                {
                    continue;
                }

                if (toolNameFilter is not null &&
                    !string.Equals(entry.ToolName, toolNameFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                results.Add(entry);

                if (results.Count >= limit)
                {
                    return results;
                }
            }
        }

        return results;
    }
}
