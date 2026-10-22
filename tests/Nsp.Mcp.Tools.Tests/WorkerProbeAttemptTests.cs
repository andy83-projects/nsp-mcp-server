using System.Text.Json;
using Nsp.Automation.Core;
using Xunit;

namespace Nsp.Mcp.Tools.Tests;
public sealed class WorkerProbeAttemptTests
{
    [Fact] public void ANewAttemptCannotExposeAnOlderPassingProbe()
    {
        using var json = JsonDocument.Parse("{\"success\":true,\"dialogs\":[]}");
        var old = WorkerProbeAttempt.Start().Succeeded(json.RootElement);
        Assert.True(old.Result!.Value.GetProperty("success").GetBoolean());
        var next = WorkerProbeAttempt.Start();
        Assert.Null(next.Result);
        Assert.Null(next.Error);
        var failed = next.Failed("probe timeout");
        Assert.Null(failed.Result);
        Assert.Equal("probe timeout", failed.Error);
        Assert.NotNull(failed.FinishedAt);
    }
}
