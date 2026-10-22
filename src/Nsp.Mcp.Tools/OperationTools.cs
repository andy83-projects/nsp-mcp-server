using System.ComponentModel;
using ModelContextProtocol.Server;
using Nsp.Automation.Core;

namespace Nsp.Mcp.Tools;

[McpServerToolType]
public sealed class OperationTools(AutomationOperations operations)
{
    [McpServerTool(Name = "nsp_operation_status", ReadOnly = true)]
    [Description("Nonblocking status that does not access NSP/UIA. Use after an HTTP timeout before retrying. Reports the active operation, stage, cancellation request and recent outcomes. Set operationId and includeResult=true to retrieve its completed MCP result. History is bounded and resets on server restart; check serverInstanceId. A running operation is never safe to replay merely because its client timed out.")]
    public object Status(string? operationId = null, bool includeResult = false)
    {
        AutomationOperation? Trim(AutomationOperation? r) => includeResult ? r : r is null ? null : r with { ResultJson = null };
        return new { serverInstanceId = operations.ServerInstanceId, active = operations.Active is { } active ? active with { ResultJson = null } : null,
            operation = operationId is null ? null : Trim(operations.Get(operationId)),
            recent = operations.Recent().Select(r => r with { ResultJson = null }).ToArray() };
    }
    [McpServerTool(Name = "nsp_cancel_operation", Destructive = true)]
    [Description("Request cooperative cancellation of one exact active operation. Requires confirm=true. Does not force-unlock, kill NSP, roll back changes or interrupt a blocked UIA call. The admission gate remains held until the handler actually finishes. Edits already started finish verification/audit; check status and readback afterward.")]
    public object Cancel(string operationId, bool confirm)
    {
        var requested = confirm && operations.RequestCancellation(operationId);
        return new { dryRun = !confirm, cancellationRequested = requested, operation = operations.Get(operationId) };
    }
}
