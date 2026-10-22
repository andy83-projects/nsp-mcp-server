# Use it carefully

Both the MCP and the AI are fallible. Test results, readback, clamps, confirmations, and audit records are supporting evidence, not a guarantee of a valid or safe map. A competent person must check every change and the overall calibration in NSP.

## Offline workflow

1. Back up the native map. Make a disposable offline copy and keep the ECU disconnected. Have one client and one NSP window active.
2. Check `nsp_get_app_state`, `nsp_operation_status`, and `nsp_list_tables`. Independently compare the map path/title, dirty state, and connection state with NSP.
3. Navigate to one precise page. Inspect its exact field/table identity, existing value, units, connection row, and intended new value. Do not guess names or physical pins from adjacent controls.
4. Request a dry run where supported (`confirm:false`). Review the target, before value, units, range, and any proposed clamping. Never assume a tool's range is a sensible tuning range for your engine.
5. Apply one reviewed change with `confirm:true`, then independently read it back and inspect the visible result in NSP. Check related settings and errors too. I/O pin assignment does not configure edge, bias, or button mode.
6. Review the audit record and save deliberately in NSP or with the save tool only when you intend to persist the edits. Saving a map is distinct from uploading it. Independently review the full map and normal commissioning process before any separate ECU upload or output/engine test.

The server's confirmation flag is software input, not a substitute for your review. Generic keyboard/click tools can perform broad UI actions; do not authorize unrestricted input just because a structured tool refused something.

## Table example

`nsp_list_tables` is the authority for supported IDs and capabilities. All 16 registered definitions accept their canonical ID, exact help identity, or unique exact display name. Discovery can find additional tables; that does not make them writable.

For example, on a test map with the relevant table available:

```text
nsp_get_table_axes(tableId:"cranking_fuel")
nsp_read_cell(tableId:"Fuel Tuning - Cranking", row:0, col:0)
nsp_write_cell(tableId:"Fuel Tuning - Cranking", row:0, col:0,
               value:350, confirm:false)
```

**350 is an automation example, not a recommended fuel setting.** Inspect the actual units and before value. Do not change a real calibration to match this example. Dry runs read the UI and may navigate to the table. A confirmed write requires a live/local clamp range; missing ranges refuse. Out-of-range values are clamped and reported, which still requires review.

I/O tools require an exact connection field. The picker adapter uses actual native rows and explicit unassigned markers; it refuses ambiguous or occupied resources instead of silently stealing a pin. Check final pin/resource readback and all per-row options yourself.

NSP can report a table cell as visible even when its bounds are clipped behind another panel. Cell selection now checks the actual native window hit. For clipped cells it uses a visible grid cell as an anchor and bounded native arrows to select the requested logical address. Every write still verifies the entire table and axes. A target mismatch names the requested cell; collateral-change diagnostics identify both addresses. This does not eliminate the need to check the result yourself.

If a table's native axis editor is unavailable, `nsp_get_table_axes` returns `success:false`, `errorCode:axes_unavailable`, and `valuesWritten:false`. Range writes refuse during axis preflight too. In particular, disabled Boost Control tables can remain cell-readable while their axis editor is unavailable. The server does not enable the function or invent axes to work around that restriction.

## Timeouts and recovery

A disconnected client, Ctrl+D, cancelled request, or failed result does not prove that an edit stopped or was rolled back.

`journal_write_failed` explicitly stating that no request was dispatched means intent could not be durably recorded before worker dispatch. Resolve the file-access problem and check operation status. A storage error after dispatch is different: it retains the recovery gate and uncertain intent. Do not delete either the journal or its pending `.tmp` replacement to bypass the problem.

1. Call `nsp_operation_status`. If an operation is still running, poll its status rather than issuing the mutation again. Retrieve its completed result with the operation ID and `includeResult:true`.
2. If a worker deadline/failure quarantined the session, call `nsp_recover_worker(confirm:true)`. This restarts only the isolated automation worker, not NSP; it does not replay the request.
3. Inspect the retained interrupted request, native map/title/dirty state, dialogs, actual accepted settings, and audit. `nsp_probe_recovery_state` is available even when the recovery gate blocks normal commands. A native heartbeat alone does not prove UIA health or settle the edit's outcome.
4. If recovery asks for human action, resolve NSP's condition yourself. Do not delete `worker-recovery.json` to force a ready state or have the AI blindly close unknown dialogs.
5. Only after reconciling the exact interrupted operation, use `nsp_complete_recovery` with the current recovery/operation IDs, exact expected map title, dirty state, and honest review notes. Stale acknowledgements are refused. If the outcome remains uncertain, stop and investigate.

Cancellation is cooperative. Once an edit has started, verification/audit may finish before cancellation releases ownership. A failed post-action check can mean the change was applied but verification failed. The journal intentionally survives server restarts.

## Known limits

Native controls, focus, layout, and window ownership can still cause incorrect selection or timeouts. Narrow navigation searches help; use exact returned page identities. If opening a dialog reports failure, inventory `nsp_list_process_windows` before repeating the action: the dialog may already exist. The intermittent opening false-negative is not proven fixed.

Only dead-time voltage columns, coolant-bias RPM rows, and cranking-ignition temperature columns have enabled same-size breakpoint replacement. Other directions, arbitrary resizing, and third-axis editing need additional validation. An empty validation/error scan is not a whole-map acceptance test; `mapValidity` may be `unknown`.
