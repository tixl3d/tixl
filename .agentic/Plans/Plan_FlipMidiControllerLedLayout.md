# Canonical 0-based snapshot index across all MIDI controllers

Ticket: #1081 — https://github.com/tixl3d/tixl/issues/1081
Milestone: v4.2 (plan predates the snapshot-ordering rework; updated 2026-06-17)

## Decided architecture
The snapshot **activation index is canonical: 0-based, with 0 at the top-left, reading order**
(left→right, top→bottom). This is what snapshots store, what the editor's controller grid shows, and what
the picker list sorts by. Each MIDI controller is responsible — *in its own implementation* — for translating
that canonical index to whatever physical note/LED makes its own top-left pad show index 0. No per-device
"editor layout" divergence: the editor always shows reading order; the device hides its physical quirks.

(Index base 0, not 1, by decision — leaves the top-left slot usable and avoids a "squeeze in front of 1"
problem.)

## Why this replaces the original draft
The previous draft proposed a horizontal column mirror routed through per-device editor layouts. That was the
wrong transform (the APC Mini's mismatch is vertical — it numbers pads row-major *from the bottom*) and the
wrong layer (editor display vs. the device's hardware mapping). The device should own the flip; the rest of the
app stays canonical.

## Done — Stage 1 (2026-06-17): canonical order + APC Mini
- `ControllerGridLayout.ReadingOrder` is now **0-based** (`row*8 + col`) — the canonical order.
- `ButtonRange` gained an optional **`mapToIndex`** transform (position-in-range → activation index), applied
  in `GetMappedIndex`, so it flows through **both** LED output (`UpdateRangeLeds`) and button input
  (`CommandTriggerCombination`) — one place keeps lights and presses in sync.
- **APC Mini**: its clip-grid `ButtonRange` carries the row-flip `position => (8-1 - position/8)*8 + position%8`,
  so the physical top-left pad is index 0. Its bottom-up editor `GridLayout` override was removed (the editor
  now just uses reading order).
- New `+` snapshot with none active takes the **lowest free index** (so index 0 / top-left is used).

Build verified. **Needs hardware testing on the APC Mini** (build ≠ correct): LEDs must match the editor's
reading-order grid, and pressing a pad must activate the snapshot shown in that grid cell. Also re-check the
Save / Delete / Blend modes (they all route through the same range, so should follow automatically).

## Remaining stages
- **Stage 2 — APC40 Mk1/Mk2 + ApcMini Mk2.** Apply the same row-flip in their LED-out / button-in paths
  (`SendColor`/`SendLedState` + `ConvertNoteToButtonId`; these decompose row/col already, so the flip slots in
  there rather than via `ButtonRange`). Remove their editor `GridLayout` overrides. Hardware-test each.
- **Stage 3 — remove the now-vestigial editor layout selection.** Once no device exposes a `GridLayout`, the
  pluggable-layout machinery is dead weight: `CompatibleMidiDevice.GridLayout`, the reflection in
  `ControllerGridLayouts.Collect`, the layout list in the controller-grid settings menu, and
  `UserSettings.Config.SnapshotControllerLayout` / `ResolveLayoutIndex`. The editor grid then just uses
  `ControllerGridLayouts.ReadingOrder`.
- **Docs/tests.** `.help` controller section + the manual test set; note the data-behavior change in the PR.

## Risks / notes
- **Bidirectional**: LED-out and button-in must flip together. The `ButtonRange.mapToIndex` approach guarantees
  this for the APC Mini (single source); the APC40s need both their out- and in-paths edited consistently.
- **Existing user data**: snapshots stored by index now light a *different* physical pad (mirrored vertically).
  The ticket accepts this; re-adjusting is now trivial via the controller-grid drag-reassign + list reorder.
- **Per-device duplication** (Mk1/Mk2/Mini/MiniMk2) — do them all in Stage 2; easy to fix one and miss another.
- **Not build-verifiable** — every stage needs the physical hardware to confirm both directions.
