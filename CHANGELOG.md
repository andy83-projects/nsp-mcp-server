# Changes

## 2026-10-22 — clipped table cells and unavailable axes

Fixed a reproduced case where selecting a clipped column of a wide 1D table clicked outside the visible grid and sent the requested value to the previously selected cell. NSP's UIA provider reported unclipped logical bounds and `IsOffscreen=false`. Grid selection now checks the native hit target and, when necessary, navigates from an exposed anchor cell using bounded native arrows. Focus is established before keyboard input. Grid discovery also respects native parent clipping instead of relying on cell (0,0) always being visible.

Whole-table/axis verification remains mandatory after each write. Diagnostics now distinguish a mismatch at the requested target from an unintended change elsewhere, with explicit coordinates for both where relevant.

Axis-editor failures return a structured `axes_unavailable` response, including during range-write preflight. This handles disabled Boost Control tables whose values remain readable while their native axis editor cannot open. No function is automatically enabled and no axis values are guessed. Axis-dialog lookup is bounded and successful reads cancel the dialog using its verified native close path.

Map status now refreshes native window identity/title instead of reusing a cached title while the HWND is alive. Save verification polls for up to five seconds rather than checking once after 300 ms, checks that map/window identity remains the same, and reports pending dialogs or uncertain outcomes without replaying the save.

Journal replacement tolerates brief Windows access/sharing failures with bounded retries. If persisting intent still fails **before dispatch**, the server returns `journal_write_failed` and restores the previous in-memory ownership state; the request is not sent to the worker. A failure recording an already-dispatched outcome remains quarantined and retains interrupted intent. The original journal and any pending replacement are preserved rather than deleted as a recovery workaround.

Validation: Windows build with zero warnings/errors and 81 passing logic tests. Live offline checks covered individual columns 14/15, an entire 16-cell row write and restore with complete snapshots, an enabled table's axes, a 2D table edit and exact restoration, and both disabled boost tables returning structured axes refusal while cell reads still worked. A forced Windows journal lock returned a structured no-dispatch failure with no stuck owner; normal reads resumed after unlocking without a restart. Map dirty/clean status changed correctly across an edit/save, and saving after opening Function Manager succeeded. Boost remained disabled. The original saved map was reopened clean/offline and its file hash was unchanged. These checks are limited evidence, not a guarantee of calibration safety or compatibility with other NSP versions.

## 2026-10-22 — initial public source import

Published the full implementation through the Phase 17 development checkpoint, with source, tests, portable deployment scripts, Codex/Claude Code setup, explicit work-in-progress warnings, and loopback network defaults. The original 67-case logic suite and a clean Windows build/publish passed. Private maps, session captures, and machine-specific logs were excluded.
