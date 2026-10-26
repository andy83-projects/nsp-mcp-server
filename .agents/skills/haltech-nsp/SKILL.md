---
name: haltech-nsp
description: Operate Haltech NSP through the NSP MCP server to inspect maps, edit supported tables and settings, assign I/O, and reconcile interrupted operations. Use for NSP automation requests; general engine-tuning advice or MCP server development alone does not require this skill.
metadata:
  version: "0.1.0-alpha.1"
  server-release: "v0.1.0-alpha.1"
---

# Haltech NSP

Use the connected NSP MCP tools to carry out the user's requested map work. This
skill accompanies server v0.1.0-alpha.1. Inspect the connected tool schemas and live
catalog: tool prefixes, supported controls, and capabilities can differ by setup.
The skill does not install, start, or connect the server by itself.

## Establish the session

Find the connected server by its `nsp_*` tool names; `haltech` is the documented
connection alias, not a required tool prefix. If unavailable, read
[connection.md](references/connection.md). Do not substitute direct file edits,
screen clicks, or an exposed public endpoint for a missing MCP connection.

Start with `nsp_operation_status`, `nsp_get_app_state`, and `nsp_list_tables`, one
call at a time. If status reports an active operation or recovery gate, inspect
status/recovery before requesting normal worker commands. Establish the exact map
title/path when available, dirty state, disconnected ECU state, worker readiness,
and relevant supported capability. Unknown connection state is not disconnected.
If the target identity or connection state is uncertain, resolve that before editing.

Use a backed-up offline map copy for edits, one active NSP window and one AI client
per desktop. NSP and the server must share the same unlocked Windows user session.
Do not open a different map, discard unsaved work, connect/upload to an ECU, energize
outputs, or start an engine as an implied part of editing a map.

## Inspect, change, verify

Preserve the user's scope. A request to inspect or explain authorizes reads; a
request for specific edits authorizes those edits. Use existing authorization
without asking again for each step. Ask for missing target/value information when
it affects the change. `confirm:true` is a tool argument, not evidence of user consent.

Prefer exact structured tools over broad UI input. Resolve the live page/table ID,
field or connection row, units, before value, and requested after value. Narrow
navigation searches to the destination; use returned scope IDs/control keys and
exact dropdown options rather than guessing labels or physical pins.

For writes supporting previews, use `confirm:false` first. A preview may navigate,
change selection, or append audit records even though it does not change calibration
values. Check its actual proposed value and range. If clamping changes the user's
requested value, obtain a decision before applying that different value. Do not
invent a clamp range to make a refused write succeed.

Apply the authorized change with `confirm:true`, then reread the accepted value
and relevant surrounding state. For tables, compare full snapshots including axes
to detect unintended changes. Range writes can partially succeed; inspect every
per-cell result and reconcile failures before continuing. Keep calls sequential.
Read [operations.md](references/operations.md) for tables, breakpoint replacement,
settings dialogs, or I/O assignments.

Treat any timeout, cancellation, transport failure, or post-action verification
failure as a potentially applied edit. **Do not replay the mutation.** Read
[recovery.md](references/recovery.md) and reconcile the exact operation first.
If a structured tool refuses an unsupported target or axis, report the limitation;
do not bypass that refusal with keyboard, screenshot, or generic click tools.

## Finish within scope

Use `nsp_save_map` only when saving is requested or included in the user's established task. Saving is
distinct from ECU upload; this skill does not provide an upload workflow. Check
the map identity, dialogs, and dirty state again after a save. Do not claim a save
failed to apply solely because its response failed.

Report the target, requested/applied values and units, readback evidence, partial
or uncertain outcomes, and whether a save occurred. An empty error scan or a
successful readback does not establish whole-map validity or calibration safety.
Leave independent review in NSP explicit; do not claim engine suitability from
automation checks.
