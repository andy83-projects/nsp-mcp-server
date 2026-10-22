using System.Text.Json;

namespace Nsp.Automation.Core;

public sealed record WorkerProbeAttempt(DateTimeOffset StartedAt, DateTimeOffset? FinishedAt = null,
    JsonElement? Result = null, string? Error = null)
{
    public static WorkerProbeAttempt Start() => new(DateTimeOffset.UtcNow);
    public WorkerProbeAttempt Succeeded(JsonElement result) => this with { FinishedAt = DateTimeOffset.UtcNow, Result = result.Clone(), Error = null };
    public WorkerProbeAttempt Failed(string error) => this with { FinishedAt = DateTimeOffset.UtcNow, Result = null, Error = error };
}
