using System.Text.Json;

namespace Nsp.Automation.Core;

/// <summary>Flush a complete replacement before publishing it. Windows readers/scanners
/// can temporarily deny replacement even after the temp stream has been closed.</summary>
public static class DurableJournalFile
{
    public static void Write<T>(string path, T value, JsonSerializerOptions options,
        Action<string, string>? replace = null)
    {
        replace ??= (source, destination) => File.Move(source, destination, true);
        var temp = path + ".tmp";
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                { JsonSerializer.Serialize(stream, value, options); stream.Flush(true); }
                replace(temp, path);
                return;
            }
            catch (Exception ex) when (attempt < 4 && ex is IOException or UnauthorizedAccessException)
            { Thread.Sleep(100); }
        }
    }
}
