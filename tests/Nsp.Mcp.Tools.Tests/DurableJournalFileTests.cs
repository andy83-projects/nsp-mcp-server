using System.Text.Json;
using Nsp.Automation.Core;
using Xunit;

namespace Nsp.Mcp.Tools.Tests;

public class DurableJournalFileTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransientReplacementFailureRetriesTheFlushedCompleteJournal(bool unauthorized)
    {
        var directory = Path.Combine(Path.GetTempPath(), "nsp-journal-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "journal.json");
            File.WriteAllText(path, "old evidence");
            var attempts = 0;
            DurableJournalFile.Write(path, new { intent = "retained" }, new(), (temp, destination) =>
            {
                Assert.Equal("retained", JsonDocument.Parse(File.ReadAllText(temp)).RootElement.GetProperty("intent").GetString());
                if (++attempts == 1)
                {
                    Assert.Equal("old evidence", File.ReadAllText(destination));
                    if (unauthorized) throw new UnauthorizedAccessException("temporary Windows replacement denial");
                    throw new IOException("temporary sharing violation");
                }
                File.Move(temp, destination, true);
            });
            Assert.Equal(2, attempts);
            Assert.Equal("retained", JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("intent").GetString());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void PermanentFailureIsBoundedAndRetainsTheOldJournalAndPendingEvidence()
    {
        var directory = Path.Combine(Path.GetTempPath(), "nsp-journal-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "journal.json");
            File.WriteAllText(path, "old evidence");
            var attempts = 0;
            Assert.Throws<IOException>(() => DurableJournalFile.Write(path, new { intent = "pending" }, new(), (_, _) =>
            { attempts++; throw new IOException("still locked"); }));
            Assert.Equal(5, attempts);
            Assert.Equal("old evidence", File.ReadAllText(path));
            Assert.Equal("pending", JsonDocument.Parse(File.ReadAllText(path + ".tmp")).RootElement.GetProperty("intent").GetString());
        }
        finally { Directory.Delete(directory, true); }
    }
}
