using Xunit;

namespace Nsp.Integration.Tests;

/// <summary>
/// End-to-end: an MCP client talking to the running server's SSE endpoint, across the VM's
/// network boundary, driving the full flow from the plan's Phase 6 — get_app_state ->
/// open_map -> toggle a function -> save -> read a table cell -> write a cell with confirm
/// -> get_audit_log. Requires the Windows VM with NSP open and the server running, so it's
/// skipped everywhere else; implement it once Phase 6 picks an MCP client package.
/// See ../../docs/RUNBOOK.md.
/// </summary>
public class EndToEndTests
{
    [Fact(Skip = "Requires the Windows VM with NSP open and the server running — see docs/RUNBOOK.md. Implement in Phase 6.")]
    public void FullFlow_GetStateOpenToggleSaveReadWriteAudit()
    {
    }
}
