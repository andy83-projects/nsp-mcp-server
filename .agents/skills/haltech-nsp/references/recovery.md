# Reconcile an interrupted operation

A missing response is not rollback. Preserve the exact requested operation, its
ID, map identity, and before snapshot. Do not replay a write, save, or dialog action
because of a client timeout or cancellation.

1. Call `nsp_operation_status`. If work is active, poll that operation sequentially;
   request its completed result with `operationId` and `includeResult:true`. Do not
   issue an overlapping edit. Cancellation is cooperative and can wait for
   verification/audit after an edit has started.
2. If status calls for worker recovery, use `nsp_recover_worker(confirm:true)` as
   part of reconciling the requested work. This restarts only the isolated worker,
   never NSP, and does not replay the request. Do not recover a healthy active
   worker just to shorten the wait.
3. Review status's retained interrupted intent and current recovery ID. Use
   `nsp_probe_recovery_state` during the gate to inspect responsive window state,
   exact map title, dirty state, dialogs, and connection state. Follow its current
   schema for `includeAutomation:true` when UIA evidence is required. A successful
   native heartbeat does not prove UIA health or establish the edit's outcome.
4. Inspect actual accepted values and audit evidence using the available recovery
   inspection paths. `nsp_get_audit_log` remains available while quarantined, but a
   missing audit record does not prove that no edit occurred. Normal worker tools
   may remain blocked until reconciliation; do not clear the gate merely to obtain
   the evidence needed to clear it. If the available evidence cannot establish the
   outcome, stop and request a focused independent inspection in NSP.
5. For `customerActionRequired`, explain the specific NSP condition and ask the
   user to resolve it. Do not close/restart NSP, discard unsaved work, or repeatedly
   restart the worker while the same condition persists. Recheck with the probe
   after the condition changes.
6. Call `nsp_complete_recovery` only after reconciling the exact interrupted
   operation. Use the current `recoveryId`, `reviewedOperationId`, exact full map
   title, dirty state, substantive evidence-based review notes, and `confirm:true`.
   Its independent check requires fresh UIA evidence of a disconnected ECU and no
   dialogs. Never invent review notes or acknowledge an unknown outcome. Recheck
   status afterward; resume only once ready and within the original task scope.

If `journal_write_failed` explicitly says dispatch never happened, resolve the
storage-access problem and inspect status before making a new request. Errors
after dispatch retain uncertain intent and need the reconciliation above. Preserve
the recovery journal and any pending `.tmp` replacement; do not delete, edit, or
reset them to bypass a gate, including when updating the server.

After a completed failure, make at most one targeted corrective attempt only when
new evidence establishes what failed and rules out duplicating an applied action.
If the same problem persists or outcome remains uncertain, stop that mutation and
report the evidence and specific next investigation. Do not loop through retries,
restart NSP, or use lower-level input as a recovery shortcut.
