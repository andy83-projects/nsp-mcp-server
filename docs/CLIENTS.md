# Connect Codex or Claude

First [run the server](DEPLOYMENT.md) on the same interactive Windows desktop as NSP. If the client is on another computer, establish the SSH tunnel described there. These examples assume the endpoint is `http://127.0.0.1:6274/` **as seen by the client**.

This project uses Streamable HTTP. It does not launch through stdio, implement OAuth, or include a third-party bridge. Keep normal client tool approvals enabled. A tool's `confirm=true` parameter is an argument an AI can send, not proof that a human reviewed the edit.

The optional [haltech-nsp skill](SKILL.md) provides operating and recovery instructions for these tools. Install it on the client machine after connecting the server; it works with either server architecture.

## Codex

Register the running HTTP endpoint:

```sh
codex mcp add haltech --url http://127.0.0.1:6274/
codex mcp list
```

For explicit timeouts, edit the existing entry in `~/.codex/config.toml` (do not duplicate it):

```toml
[mcp_servers.haltech]
url = "http://127.0.0.1:6274/"
startup_timeout_sec = 30
tool_timeout_sec = 150
```

The 150-second client deadline gives the server's normal 120-second worker deadline room to return its recovery response. It does not make slow calls safe to replay. In Codex use `/mcp` to inspect the connection. The IDE settings also support adding a Streamable HTTP URL; restart/reconnect after configuration or server schema changes. See [OpenAI's official MCP documentation](https://developers.openai.com/codex/mcp).

## Claude Code

Add the endpoint for your user:

```sh
claude mcp add --transport http --scope user haltech http://127.0.0.1:6274/
claude mcp list
```

In Claude Code, use `/mcp` to check its status. Reconnect after server updates to refresh tool metadata. For a project-scoped alternative, use an entry in `.mcp.json`:

```json
{
  "mcpServers": {
    "haltech": {
      "type": "http",
      "url": "http://127.0.0.1:6274/",
      "timeout": 150000
    }
  }
}
```

Choose one configuration scope rather than adding duplicate servers. The timeout here is milliseconds. See [Anthropic's official Claude Code MCP documentation](https://code.claude.com/docs/en/mcp) for scopes, approvals, timeouts, and reconnection.

## Claude Desktop / claude.ai

The direct setup above is for **Claude Code**. Other Claude products have different connector and transport requirements. This repository does not ship or require a stdio-to-HTTP bridge, and the Windows server should not be exposed publicly to make a cloud connector reach it. Consult the relevant client's current local MCP support; do not paste the HTTP configuration into a stdio-only configuration field.

## First connection: read-only checks

Ask the AI:

> Use the haltech tools to call nsp_get_app_state, nsp_operation_status, and nsp_list_tables. Report the exact open map title, unsaved-change state, ECU connection state, worker phase, and supported table IDs. Do not navigate, edit, save, upload, toggle functions, or send keyboard input.

Compare the report yourself with NSP. Verify it is the disposable offline copy and that the worker is ready. Stop if the identity, connection state, or result is uncertain. Some tools described as reads navigate or temporarily change UI selection; begin with the three checks above.

Then follow the [edit-and-verify workflow](USAGE.md). Do not run two AI clients against the same NSP desktop concurrently. The server rejects overlapping calls, but separate clients can still alternate actions and invalidate each other's assumptions.
