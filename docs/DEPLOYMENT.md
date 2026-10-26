# Deploy from source

For precompiled, self-contained **Windows ARM64** and **Windows x86 (32-bit)** packages, see [GitHub Releases](https://github.com/andy83-projects/nsp-mcp-server/releases) and the [binary quick-start](../packaging/QUICKSTART.md). These packages do not require Git or a separate .NET installation. The source-build requirements below apply when building yourself.

This is experimental software. First use a disposable offline map, keep the ECU disconnected, and independently check the application after every edit. [Read the verification workflow](USAGE.md) before granting an AI access.

## Requirements

- A Windows desktop with Haltech NSP installed and usable by the logged-in user.
- .NET **10 SDK** to build; a framework-dependent server also needs the matching .NET 10 / ASP.NET Core / Windows Desktop runtimes on the machine where it runs. Keeping the SDK installed is the simplest development setup.
- Git, and an MCP-capable client such as Codex or Claude Code.
- An interactive, unlocked desktop. A Windows VM is fine; the AI client can run on the host.

Windows 11 ARM64 under VMware Fusion was used during development. This is a tested environment, not a promise of compatibility with every NSP version, firmware, architecture, language, theme, or DPI setting. Install the SDK for your Windows architecture and verify `dotnet --info`.

## Build and run

In PowerShell **on Windows**:

```powershell
git clone https://github.com/andy83-projects/nsp-mcp-server.git
cd nsp-mcp-server
dotnet restore
dotnet build
dotnet test tests/Nsp.Mcp.Tools.Tests
dotnet publish src/Nsp.Mcp.Server -c Release -o out/server --self-contained false
```

Open NSP yourself. Open a disposable offline map copy, close extra NSP instances and pending dialogs, and disconnect from the ECU. Then run from the checkout:

```powershell
.\scripts\Start-NspMcp.ps1
```

The console remains attached. Use Ctrl+C to stop **the server**, after checking operation status and reconciling any in-progress edit. Stopping the server does not roll back edits in NSP. Do not terminate or restart NSP to recover a stuck worker unless you have separately reviewed unsaved changes.

The server listens at `http://127.0.0.1:6274/`. A browser GET is not an end-to-end MCP test. Use the client checks in [CLIENTS.md](CLIENTS.md).

You can pin the expected map filename:

```powershell
.\scripts\Start-NspMcp.ps1 -ExpectedOpenFileName 'Example_Offline_Draft.nexmap'
```

An empty name permits the single open NSP window, including a new unsaved map. A configured name limits the window match, but does not replace independent title/path checks. The locator refuses ambiguous matching NSP windows.

## The interactive-session requirement

NSP and both server processes must belong to the **same logged-in desktop session**. Starting the executable in SSH/WinRM, a Windows service, or a scheduled task configured to run when nobody is logged in creates the wrong session for UI automation. SSH is useful for copying files, builds, log inspection, and triggering an already configured interactive task.

For optional startup through Windows Task Scheduler:

1. Create a task for the same Windows user who runs NSP.
2. Select **Run only when user is logged on**. Avoid unnecessary administrator elevation; keep NSP and the server at the same privilege level.
3. Trigger **At log on** for that user.
4. Program: `powershell.exe`. Arguments: `-NoProfile -File "C:\path\to\nsp-mcp-server\scripts\Start-NspMcp.ps1"`.
5. Set **Start in** to the checkout directory. Select **Do not start a new instance** if the task is already running.
6. Run it while logged in, then inspect the task and server logs and perform the MCP checks.

Do not enable password-in-registry auto-logon just to follow this guide. Log in normally. Locking, suspending, or disconnecting the interactive desktop can interrupt automation; inspect and recover before continuing.

## Reach a Windows VM from another computer

The recommended layout leaves the server on loopback and uses an SSH tunnel. Set up Windows OpenSSH and key authentication using [Microsoft's OpenSSH instructions](https://learn.microsoft.com/en-us/windows-server/administration/openssh/openssh_install_firstuse). On the **client computer**:

```sh
ssh -N -L 6274:127.0.0.1:6274 windows-user@windows-vm
```

Keep the tunnel open. Use `http://127.0.0.1:6274/` in Codex/Claude on that client. If local port 6274 is occupied, use `-L 16274:127.0.0.1:6274` and configure the client URL with port 16274. The tunnel is transport access, not a way to start the desktop worker through SSH.

For deliberate private-LAN access instead, bind to the VM's private address:

```powershell
.\scripts\Start-NspMcp.ps1 -BindAddress '192.168.56.10'
```

Replace that illustrative address. Add a Windows firewall inbound rule limited to that port, the Private profile, and the specific client IP. Do not open it to all networks or forward it through an internet router. The server has **no authentication or TLS**; every reachable client can request desktop actions. Do not expose this endpoint publicly for a cloud connector.

## Configuration and persisted state

`src/Nsp.Mcp.Server/appsettings.json` is copied into the publish directory. The start script overrides bind address, port, and expected filename. Other settings can be passed as .NET configuration arguments when launching `dotnet Nsp.Mcp.Server.dll` directly. Important settings include:

| Setting | Purpose |
| --- | --- |
| `Nsp:ProcessNameHint` | NSP process name hint, normally `NSP` |
| `Nsp:ExpectedOpenFileName` | Window-title map filter; empty accepts a single NSP window |
| `Nsp:AuditLogDirectory` | Append-only audit directory |
| `Nsp:ClampRangesPath` | Optional local fallback ranges; does not register new tables |
| `Nsp:WorkerTimeoutSeconds` | Worker deadline, normally 120 seconds |
| `Nsp:RecoveryProbeTimeoutSeconds` | Recovery-probe deadline, normally 12 seconds |
| `Nsp:WorkerJournalPath` | Durable interrupted-operation state |

Relative paths resolve from the server working directory (`out/server` with the script). Back up audit logs and the recovery journal. Do not delete the journal to bypass recovery: an edit might already have happened. `clamp-ranges.example.json` under `src/Nsp.TuningTables` shows the optional configuration format. It is not vehicle tuning advice.

## Update without losing recovery evidence

First inspect operation status, review pending changes, and make a map backup in NSP. Stop the server normally when idle. Back up its publish directory, logs, and recovery journal. Pull the source, rerun build/tests, and publish to a **new temporary directory**. Copy reviewed binaries/configuration into the deployment directory while preserving its logs, journal, and local settings, then restart in the interactive session. Never mix binaries from different builds or silently reset recovery state. Repeat the read-only client checks before edits.

## Troubleshooting

- **No NSP window:** check desktop session, privilege level, process hint, map filter, extra NSP instances, and whether the desktop is unlocked.
- **Connection refused:** check server console, VM connectivity, tunnel, address and port. Use `/` as the endpoint path.
- **Dialog appears despite an opening error:** call `nsp_list_process_windows`; target its exact scope/title. Do not click the opening action again blindly.
- **Timeout:** use operation status and the [recovery procedure](USAGE.md#timeouts-and-recovery). A client timeout is not rollback.
- **Unknown table:** call `nsp_list_tables`, use a listed exact ID/alias, and inspect units. Adding a clamp range alone does not implement an unsupported table.
