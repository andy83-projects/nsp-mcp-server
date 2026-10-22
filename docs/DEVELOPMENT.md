# Development and verification

This source repository starts from the experimental implementation through the Phase 17 checkpoint. Private vehicle handoffs, native maps, screenshots, raw UI captures, deployed binaries, keys, and machine-specific session logs are intentionally not included.

| Project | Purpose |
| --- | --- |
| `Nsp.Automation.Abstractions` | Cross-platform interfaces, operations, recovery policy, native-caption logic |
| `Nsp.TuningTables` | Registered table definitions, exact identities, clamp and verification logic |
| `Nsp.Audit` | Append-only JSONL audit records |
| `Nsp.Mcp.Tools` | MCP tool definitions and routing |
| `Nsp.Automation` | Windows UI Automation, native input, settings, dialogs and table adapters |
| `Nsp.Mcp.Server` | Streamable HTTP supervisor plus isolated desktop worker |

The public server supervises a separate worker process with a private loopback endpoint and generation token. Desktop calls execute in that worker; durable intent is recorded before dispatch. Deadlines quarantine uncertain requests rather than treating them as cancelled edits. Worker recovery preserves that intent for explicit reconciliation.

## Automated checks

On Windows with the .NET 10 SDK:

```powershell
dotnet build
dotnet test tests/Nsp.Mcp.Tools.Tests
dotnet test tests/Nsp.Automation.Tests
```

Cross-platform logic tests can run on macOS/Linux:

```sh
dotnet test tests/Nsp.Mcp.Tools.Tests
```

The imported checkpoint had 67 passing cross-platform logic cases. Tests cover range resolution, exact table identities/aliases, verification, audit, navigation budgets, native input layout, operation ownership, and recovery logic. The Windows automation project has a pure coordinate-math test and a skipped live-window placeholder. The integration suite contains a skipped live-flow placeholder. Do not interpret a green CI build as a real-NSP or vehicle test.

GitHub Actions builds on Windows and runs the noninteractive tests. It deliberately does not launch NSP, edit a map, contact an ECU, or run bench scripts.

## Optional bench scripts

These require an interactive Windows desktop and a separate test setup. Read the script first. Publish to `out/server` before running them. Never run them against a production map or while another AI client is automating NSP.

- `scripts/Settings-Fixture.ps1`: standalone native-controls fixture.
- `scripts/Test-SettingsControls.ps1`: launches the isolated fixture and a test server on port 6278; writes reports under ignored `logs/`.
- `scripts/Test-WorkerRecovery.ps1 -OriginalMap 'C:\Maps\Disposable.nexmap'`: fault-injection/recovery checks against the open disposable map, using a separate test server on port 6275. Requires the same interactive desktop and an unused test port. It deletes only its own test journal between scenarios; this is not a recipe for deleting a deployment journal.
- `scripts/Restore-TestSession.ps1`: explicit recovery of a known disposable test session with supplied original map, directory, expected hash, and report path. The optional termination switch can force-close a verified disposable NSP session; review the code and arguments before use.

The bench scripts are source tools for development, not part of ordinary deployment and not automatically run by the server.

## Extending support

Use an isolated offline map. Establish exact native control/page/table identity, prove unit and axis handling, then test read/edit/readback/restore and negative cases before registering a capability. Add regression tests for actual failure modes. Do not enable arbitrary tables merely because a grid was discoverable. Keep audit and interrupted-operation evidence intact even if the client disconnects.

Some source comments reference earlier development phases. They describe implementation history; the current public operating instructions are this guide, [DEPLOYMENT.md](DEPLOYMENT.md), [CLIENTS.md](CLIENTS.md), and [USAGE.md](USAGE.md).
