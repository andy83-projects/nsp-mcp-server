using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Nsp.Automation.Core;

namespace Nsp.Mcp.Tools;

public static class OperationFilter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Create(AutomationOperations operations) => next => async (context, token) =>
    {
        var name = context.Params?.Name ?? "unknown";
        if (name is "nsp_operation_status" or "nsp_cancel_operation" or "nsp_get_audit_log" or "nsp_list_tables" or "nsp_recover_worker" or "nsp_complete_recovery")
            return await next(context, token);
        using var lease = operations.TryBegin(name, token);
        if (lease is null)
            return Result(new { success = false, errorCode = "automation_busy", serverInstanceId = operations.ServerInstanceId,
                active = operations.Active, errorMessage = "NSP automation is still running. Query nsp_operation_status; this request was rejected, not queued." });
        try
        {
            OperationContext.Checkpoint("Starting " + name);
            // Cancellation is observed at our safe checkpoints, not by abandoning a
            // synchronous UIA invocation or cancelling a post-input verification/audit.
            var result = await next(context, CancellationToken.None);
            result.Meta ??= new JsonObject();
            result.Meta["nspOperationId"] = lease.Id;
            result.Meta["nspServerInstanceId"] = operations.ServerInstanceId;
            bool? success = result.IsError == true ? false : null;
            var cancelledResult = false;
            foreach (var text in result.Content.OfType<TextContentBlock>())
            {
                try { using var doc = JsonDocument.Parse(text.Text); if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("success", out var s) && s.ValueKind is JsonValueKind.True or JsonValueKind.False) success = s.GetBoolean(); if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("errorCode", out var code) && code.GetString() == "operation_cancelled") cancelledResult = true; }
                catch (JsonException) { }
            }
            if (result.IsError == true) success = false;
            lease.Complete(JsonSerializer.Serialize(result), success, cancelled: cancelledResult);
            return result;
        }
        catch (OperationCanceledException)
        {
            var result = Result(new { success = false, errorCode = "operation_cancelled", operationId = lease.Id,
                errorMessage = "Cancellation observed at a safe boundary. Check operation status/audit and map state; cancellation does not imply rollback." });
            lease.Complete(JsonSerializer.Serialize(result), false, cancelled: true);
            return result;
        }
        catch (Exception ex)
        {
            lease.Complete(null, false, ex.Message);
            throw;
        }
    };
    private static CallToolResult Result(object data) => new() { IsError = true, Content = [new TextContentBlock { Text = JsonSerializer.Serialize(data, JsonOptions) }] };
}
