using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
namespace Nsp.Mcp.Server;

internal sealed class WorkerProxyTool(McpServerTool metadata, WorkerSupervisor supervisor) : DelegatingMcpServerTool(metadata)
{
    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default) =>
        new(supervisor.InvokeAsync(request.Params!));
}
