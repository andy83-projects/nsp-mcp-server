# Run the precompiled Windows package

This is an experimental prerelease. It contains the server and its .NET runtime;
you do not need Git, the .NET SDK, or a separate .NET runtime installation.
Haltech NSP must already be installed. NSP and vehicle maps are not included.

1. Choose **win-arm64** for Windows on ARM, or **win-x86** for the 32-bit Intel/AMD
   build. x86 means 32-bit, not x64. The x86 executable can also run on supported
   64-bit Windows through Windows' compatibility support. These labels describe
   the server process; they do not establish compatibility with every NSP version.
2. Extract the entire ZIP to a new, writable folder, such as a folder under your
   user profile. Do not run from inside the ZIP or mix files from different versions.
3. Open NSP on a disposable **offline** map copy, with the ECU disconnected. Keep
   the desktop unlocked. Run the server as the same logged-in user and at the same
   privilege level as NSP.
4. Double-click **Start-NspMcp.cmd**. Leave its console open. The server listens on
   **http://127.0.0.1:6274/** using Streamable HTTP. This unsigned prerelease may
   produce a Windows publisher/reputation warning; verify the download's source
   and checksum before deciding whether to run it.
5. Follow **docs/CLIENTS.md** to connect Codex or Claude Code. Begin with the
   read-only connection checks there before attempting any changes.

An optional **haltech-nsp** companion skill is available on the matching GitHub
release. Install it on the AI client machine following **docs/SKILL.md** from the
updated source or the skill ZIP's **INSTALL.md**. Future server packages also
include the skill under **skills/haltech-nsp**.

For another computer or a VM host, follow the SSH tunnel instructions in
**docs/DEPLOYMENT.md**. Keep the default loopback binding: the server has no
authentication or TLS. Do not expose it to the internet.

## Configuration, shutdown, and updates

Configuration is in **appsettings.json** beside the executable. Logs and the
durable recovery journal live in **logs/** in the extracted folder. The launcher
always uses that folder as its working directory. Never delete the journal to
skip review of an interrupted edit.

Inspect operation status and reconcile pending edits before stopping the server
with Ctrl+C. Stopping it does not undo edits or close NSP. For an update, stop when
idle, preserve the old folder, extract into a new folder, and transfer your reviewed
configuration and the complete **logs/** directory before restarting. Do not run
two versions against the same NSP session. Follow **docs/USAGE.md** for recovery.

**build-info.json** records the version, architecture, and source commit. Each ZIP
has a companion **.sha256** checksum file. Verify with PowerShell:

```powershell
Get-FileHash .\nsp-mcp-server-<version>-win-arm64.zip -Algorithm SHA256
```

Release checks build and start each architecture, connect through MCP, and check
worker status and the table catalog without NSP. They do not validate UI editing
against a real NSP installation on each architecture. See README.md for the scope
of earlier live tests and remaining limitations.
