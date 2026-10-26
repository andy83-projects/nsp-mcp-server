# Supported operations

Use the connected tool schemas for argument names and required verification fields.
Tool results and the live catalog take precedence over these release-specific notes.

## Tables and axes

`nsp_list_tables` lists the registered table IDs, units, and row/column editing
capabilities without accessing NSP. Discovery is not registration: a newly found
table is not automatically writable. Adding clamp configuration does not implement
a table adapter.

Use `nsp_get_table_axes` and `nsp_read_table_snapshot` to establish the live row and
column order, values, and dimensions. Cell row/column indices are zero-based; bind
them to the returned breakpoints and units, not guessed screen positions. Axis-free
scalar tables have one cell. Keep the before snapshot for comparison.

Preview `nsp_write_cell` or `nsp_write_table_range` with `confirm:false`. Check
`wouldApplyValue`, `wasClamped`, current value, and range provenance where returned.
Do not treat the allowed range as tuning advice. A range request is a sequence of
cell writes, not a transaction; an error can follow earlier successful cells.
Read each result and a fresh full snapshot. Compare all cell values and both axes,
including addresses outside the intended change. If unrelated cells changed, stop
and report the exact discrepancy; do not attempt an unrequested bulk restoration.

NSP can report clipped table cells as visible. The structured driver checks native
hit targets and navigates from an exposed anchor; do not work around failures by
clicking the reported logical bounds.

`axes_unavailable` on an axis read or range preflight may occur for a disabled Boost
Control table whose cells remain readable. Inspect the refusal's `valuesWritten`
and per-cell results. Do not enable Boost or fabricate axes to complete a table edit.

For `nsp_write_axis_breakpoint`, use a supported catalog direction (`row` or
`column`), a current `expectedValue`, and the returned display-order index. This
replaces one breakpoint without resizing. Preview predicted interpolation; set
`allowInterpolation:true` only when those resulting cell changes are within the
user's authorized scope. Verify both axes and the entire grid afterward.
The release validates dead-time voltage columns, coolant-bias RPM rows, and
cranking-ignition temperature columns only. Arbitrary resizing and third-axis/slice
editing are not generally supported.

## Settings, functions, I/O, and dialogs

Use `nsp_navigate_to_page` with the exact expected destination, then
`nsp_list_settings` for that scope. Use `nsp_list_setting_options` for exact dropdown
values. Preserve returned control keys to distinguish duplicate names and compound
fields. Apply supported fields through `nsp_set_setting`; verify the accepted page
state, not just text still pending in an editor.

Function state changes and `nsp_set_io_assignment` require their own explicit target.
Resolve the exact function and connection field/row. Refused occupied or ambiguous
resources are not permission to steal a pin. Pin assignment alone does not set edge,
bias, pull-up, button mode, or related function settings. Read back those separately
when included in the task.

If a dialog-opening call reports failure, inventory `nsp_list_process_windows`
before retrying: the dialog may already be open. Use its exact returned scope/title.
For `nsp_finish_settings_dialog`, supply the required expected identity, action,
button, and verification values from the live schema. Accept/cancel only the
identified dialog within the requested task; never dismiss an unknown dialog just
to clear a timeout. Verify state after the dialog closes. Inventory/option reads
can navigate or change temporary selection even when marked read-only.
