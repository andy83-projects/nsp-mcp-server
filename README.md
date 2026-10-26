# Haltech NSP MCP server — work in progress

An experimental Model Context Protocol (MCP) server that lets **Codex or Claude Code inspect and edit Haltech NSP through the Windows desktop UI**. This repository contains the C# source, tests, and PowerShell scripts needed to build and run it. It is an independent community project, not an official Haltech product.

> **WORK IN PROGRESS — CHECK EVERYTHING.** The server can make mistakes, the AI can make mistakes, and a successful tool response does not prove that an ECU calibration is correct or safe. Independently verify every setting, pin, table, unit, and saved result in NSP. Start with an offline, disposable map copy. Never use an AI's assurance as approval to upload a map, run an engine, or energize outputs.

## What it does

- Navigates native NSP pages and inventories settings, functions, dialogs, and connections.
- Reads and changes supported scalar fields, function states, and I/O assignments.
- Reads and edits 16 registered tuning tables, with exact identities, clamp ranges, audit records, and readback checks.
- Supports breakpoint replacement in three specifically validated table/axis directions.
- Exposes operation status, cancellation, and isolated-worker recovery after a timeout.

There is no direct ECU protocol implementation here. The automation operates an installed NSP desktop application. Runtime navigation and editing use native controls; optional screenshots are diagnostic aids.

## What you must not assume

Guardrails reduce some errors; they are not guarantees. A range-clamped value may still be a terrible calibration. Readback can confirm the wrong target if identification fails. An empty validation scan does not establish whole-map validity. A timeout or failed post-action check can follow an edit that already happened. Never blindly replay a timed-out write.

The current checkpoint has 81 passing logic test cases, plus live single-cell and full-row edit/readback/restore checks on an isolated offline map. These include the far-right columns of a clipped 16-column table and a 2D table regression. Those checks cover particular workflows, not every NSP version or every vehicle. Development used Windows 11 ARM64 in VMware Fusion and Nexus R3 NSP maps with firmware versions 1.35.5/1.36.0. Other combinations need their own validation. See [CHANGELOG.md](CHANGELOG.md).

## Start here

Precompiled Windows ARM64 and x86 (32-bit) ZIPs are distributed through [GitHub Releases](https://github.com/andy83-projects/nsp-mcp-server/releases). They bundle the .NET runtime. Extract the whole package and follow its [quick-start guide](packaging/QUICKSTART.md); NSP must be installed separately. Source build instructions follow below.

1. [Deploy on Windows](docs/DEPLOYMENT.md): prerequisites, build from source, interactive desktop, startup, network access, and updates.
2. [Connect Codex or Claude](docs/CLIENTS.md): direct Streamable HTTP setup and first connection checks.
3. [Use and verify edits](docs/USAGE.md): offline workflow, table IDs, examples, recovery, and known limits.
4. [Development and tests](docs/DEVELOPMENT.md): project layout and what automated tests do and do not cover.

Quick build on the **Windows machine running NSP**, with Git and the .NET 10 SDK installed:

```powershell
git clone https://github.com/andy83-projects/nsp-mcp-server.git
cd nsp-mcp-server
dotnet test tests/Nsp.Mcp.Tools.Tests
dotnet publish src/Nsp.Mcp.Server -c Release -o out/server --self-contained false
.\scripts\Start-NspMcp.ps1
```

Keep NSP open on a disposable offline map and the Windows desktop unlocked. The server must run in that same interactive session. Running it directly in SSH, as a service, or in a headless session will not give it the desktop it needs. See the deployment guide before connecting an AI client.

## Connection and network boundary

The endpoint is **`http://127.0.0.1:6274/`**, using Streamable HTTP at `/`, not `/mcp` or `/sse`. The public source configuration binds to loopback by default. For a client on another machine, use an SSH tunnel or explicitly restricted private-network access as described in the guide.

**No authentication or TLS is implemented.** Anyone who can reach the endpoint can request powerful desktop actions. Do not expose it to the internet. Client approval settings and the tools' `confirm` argument are not network authentication. See [SECURITY.md](SECURITY.md).

## Status and limitations

- Experimental desktop automation; native controls, focus, dialogs, and navigation can fail.
- Only registered and independently validated table/axis operations are supported; discovery is not registration.
- Some generic controls and multi-connection layouts still need adapters.
- Whole-map configuration-error enumeration is incomplete; map validity can be reported as unknown.
- Third-axis/slice editing and arbitrary axis resizing are not generally supported.
- The intermittent false failure when opening a settings dialog was not reproduced in the latest checks. Waiting and diagnostics were improved; it is not claimed fixed.
- Integration-test placeholders are skipped. CI does not drive a real NSP installation or validate an engine calibration.

Issues and contributions are welcome; include reproducible offline steps and redact maps, personal paths, logs, network details, and credentials. See [CONTRIBUTING.md](CONTRIBUTING.md).

Haltech and NSP names identify the compatible application. Haltech NSP, ECU firmware, vendor documents, and vehicle maps are not distributed here. No software license has been selected for this repository yet; public visibility alone is not a grant of reuse rights.
