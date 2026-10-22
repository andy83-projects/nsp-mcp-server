using Nsp.Automation.Core;

namespace Nsp.Mcp.Tools;

/// <summary>Shared shape every tool returns, so MCP clients get a consistent envelope
/// regardless of which tool/driver produced it.</summary>
internal static class ToolResultJson
{
    public static object From(AutomationResult result) => new
    {
        result.Success,
        driverUsed = result.DriverUsed,
        verifiedValue = result.VerifiedValue,
        validationMarkers = result.ValidationMarkers,
        errorMessage = result.ErrorMessage,
        elapsedMs = result.Elapsed.TotalMilliseconds,
    };
}
