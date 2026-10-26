# Companion Haltech NSP skill

The **haltech-nsp** skill teaches an AI how to operate this MCP's supported tools:
initial map/worker checks, exact table and setting selection, edit previews,
readback, partial failures, saving, and interrupted-operation recovery. It contains
instructions and references, with no executables or calibration files.

Download **haltech-nsp-skill-0.1.0-alpha.1.zip** and its checksum from the
[matching server release](https://github.com/andy83-projects/nsp-mcp-server/releases/tag/v0.1.0-alpha.1).
The same skill works with the ARM64 and x86 server packages. Install it on the
**AI client machine**, even if NSP runs in a separate Windows VM.

## Install

Extract the ZIP. Copy its **haltech-nsp** folder, including all references and
metadata, into one location for your client:

| Client | Personal skill folder | Project alternative |
| --- | --- | --- |
| Codex | `~/.agents/skills/haltech-nsp/` | `.agents/skills/haltech-nsp/` in your project |
| Claude Code | `~/.claude/skills/haltech-nsp/` | `.claude/skills/haltech-nsp/` in your project |

`~` means your user home folder (`$HOME` in PowerShell as well). The final path
must end in **haltech-nsp/SKILL.md**, not an extra nested haltech-nsp directory.
Review any existing installation before replacing it. Choose one scope per client
to avoid duplicate skills. Reload/restart the client if the skill does not appear.
The repository already contains the Codex project-scoped copy at
`.agents/skills/haltech-nsp`; users working in this checkout need no second copy.
Future server ZIPs built from this source also include it under `skills/haltech-nsp`.

The skill does not install or connect the MCP server. Start the server on Windows
and follow the server's `docs/CLIENTS.md` to register its Streamable HTTP endpoint.
Local Claude Code instructions do not imply support for Claude's cloud connectors.

## Invoke

In Codex:

```text
Use $haltech-nsp to report the current map, ECU connection, worker status,
and supported tables. Do not edit or save anything.
```

In Claude Code, invoke `/haltech-nsp` with the same request. Both clients can also
select the skill automatically for relevant NSP automation requests.

For edits, specify the map, exact setting/table and target values/units, and whether
to save. The skill uses the connected schemas and live catalog rather than
promising unsupported tables or axes. It does not supply vehicle tuning values.

## Version and validation

This companion targets **server v0.1.0-alpha.1**. The release's server binaries
remain unchanged; the standalone skill is an additional release asset from the
subsequent skill source commit on `main`. Its SHA-256 file identifies the archive.
Check future server/schema changes before reusing its release-specific guidance.

Validation covers skill metadata, local reference links, documented tool names,
ZIP contents/checksum, and independent offline scenarios for clamping, interrupted
range writes, unavailable axes, and authorized edit/save. No live calibration was
edited to test the skill. Skill instructions improve operation; they do not prove
that a calibration is correct or safe. Independently inspect changes in NSP.

Official installation references:
[Codex skills](https://learn.chatgpt.com/docs/build-skills) and
[Claude Code skills](https://code.claude.com/docs/en/skills).
