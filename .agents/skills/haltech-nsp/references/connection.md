# Connection and compatibility

The matching public server release is **v0.1.0-alpha.1**:
https://github.com/andy83-projects/nsp-mcp-server/releases/tag/v0.1.0-alpha.1

The server runs on Windows alongside Haltech NSP. Choose the server ZIP for the
Windows machine: ARM64 or x86 (32-bit). The skill itself is architecture independent
and belongs on the machine running the AI client, which may be a Mac or Linux host.
Extract the entire server ZIP, follow its QUICKSTART.md, and run Start-NspMcp.cmd
from the same interactive, unlocked user session and privilege level as NSP.
NSP is installed separately. The binary packages include the .NET runtime.

The endpoint is Streamable HTTP at `http://127.0.0.1:6274/`, including the root
path; it is not `/mcp`, `/sse`, or a stdio command. Register an absent connection
only when the user's task includes setup. Reuse an existing configuration instead
of creating a duplicate. Examples for supported command-line clients:

```sh
codex mcp add haltech --url http://127.0.0.1:6274/
claude mcp add --transport http --scope user haltech http://127.0.0.1:6274/
```

For Codex, the documented client timeout is 150 seconds, allowing the normal
120-second worker deadline to return its recovery response. Reconnect after a
server/schema update. Consult the installed client's current settings before
changing them; the skill does not grant permission to alter unrelated configuration.

For a remote Windows desktop or VM, keep the server bound to its loopback interface
and forward a client-side port through an existing authorized SSH connection:

```sh
ssh -N -L 6274:127.0.0.1:6274 windows-user@windows-vm
```

Substitute the actual user/host, and choose another local port if occupied. The AI
client connects to the client-side forwarded port. SSH tunnels transport requests;
starting the server in an SSH session does not provide NSP's interactive desktop.
Do not expose the unauthenticated, non-TLS server to the internet for a cloud client.

If no NSP tools are available, identify the missing connection or stopped server
and give the specific next setup step. A browser GET is not an MCP handshake test.
Once connected, return to the three initial checks in SKILL.md.
