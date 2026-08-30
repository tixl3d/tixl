# Slice: Shared selection + per-window pinning (drafted 2026-08-31)

Implements the 2026-08-29 scope decision ([`selection.md`](selection.md) §Selection scope,
[`ui-restructuring-plan.md`](ui-restructuring-plan.md) §A.1): **one `SetupEntitySelection` shared
across all output windows**, with each window carrying only a **pin** that freezes what it displays.
First slice of the A→B→C pivot — small, self-contained, unblocks Phase A (the Parameter window will
read the shared instance directly).

## As-is (verified against the branch, 2026-08-31)

- `SetupEntitySelection` is instantiated in exactly **one** place —
  `OutputSetupModeView._entitySelection` (one per window) — and passed as a parameter everywhere
  else (`SetupPanel`, `SetupOutputView`, `SetupActions`, `EntityItem`). No other construction sites.
- What a window *shows* is derived from its selection (`TryGetShownEntity` → `TryResolve`), which is
  exactly the coupling the pin replaces.
- The graph-focus invariant lives in `TryDrawEditingView`: on a focus *transition*
  (`_lastFocusedId`), a focused sink selects its CONTENT row, any other op clears the selection;
  the same transition auto-opens/closes the side panel.
- Op-instance pinning already exists (`ViewSelectionPinning` + `OutputWindowState.IsPinned/
  PinnedInstancePath/PinnedOutputId`) and persists via whole-object `JsonConvert` — the entity pin
  follows that pattern with two new fields.

## Steps

### 1. Share the instance
- Add `internal static readonly SetupEntitySelection EntitySelection = new();` to
  `OutputSetupHandling` (the project-scoped static hub, as `selection.md` suggests).
- `OutputSetupModeView`: replace `= new()` with `= OutputSetupHandling.EntitySelection`. All
  downstream call sites keep working unchanged — they take the selection as a parameter.
- No clearing plumbing: `TryResolve` already prunes targets against the active setup, so a project
  switch empties the selection lazily. **Accepted edge:** a `ContentSource` target can outlive its
  project via the live-sink check in `ExistsInSetup` (the registry is global); harmless, prune on
  project-switch later if it ever shows.
- Update the xmldocs that say "one instance per OutputWindow" (`SetupEntitySelection`,
  `OutputSetupModeView` header).

### 2. Graph-focus invariant with N windows
- The `_lastFocusedId` transition block stays **per window** (it also drives the panel auto-open,
  which remains per-window). With the shared selection, both windows write the same value on the
  same frame — idempotent; add a comment stating that.
- Accepted behavior change (by design): focusing a non-sink op clears the selection for **all**
  windows — the invariant applies to the one shared set.

### 3. Entity pin per window
- `OutputSetupModeView` gains `_pinnedKind` / `_pinnedId` (`EntityKind.None`/`Guid.Empty` =
  follow selection).
- `TryGetShownEntity` becomes: **pinned →** validate the pin against the setup (expose the existing
  `ExistsInSetup` as `internal static bool Exists(Setup, EntityKind, Guid)` on
  `SetupEntitySelection`); stale → clear the pin and fall through; valid → return the pin.
  **Unpinned →** resolve the shared selection (current behavior).
- The focused-sink fallback branch in `TryDrawEditingView` applies only while unpinned — a pinned
  window short-circuits to its pin.

### 4. Pin UI
- Breadcrumb menu (`OutputWindow.DrawOutputMenuExtras`): `DrawMenuItem` "Pin view to <entity name>"
  (`isChecked: pinned`). Pinning captures the currently shown entity; unpinning clears. Disabled
  while nothing is shown.
- Toolbar indicator: pin icon button next to the panel toggle, `ButtonStates.Activated` while
  pinned, tooltip naming the pinned entity; click = unpin. (Reuse the icon `ViewSelectionPinning`
  uses.)
- The existing op-instance pin is untouched and orthogonal: it pins which *op output* is drawn when
  the window is **not** in setup-editing; the entity pin pins the setup-editing view. They meet in
  the breadcrumb; unification is Phase B's strip-header work, not this slice.

### 5. Persistence
- Two fields on `OutputWindowState` (`// Pinning` block): `PinnedEntityKind` (string via
  `StringEnumConverter`, matching the file's enum style) + `PinnedEntityId : Guid`. Serialization is
  automatic (whole-object). Restore on window init; step 3's validation handles stale ids on first
  resolve.

### 6. Verification (manual test set, same PR)
Extend the output-setup walkthrough (`.tests-manual/`) with a **Two windows** section:
1. Open a second output window → clicking a row in either highlights in both; the shown canvas
   follows in both (unpinned).
2. Pin window A to Output 1 (breadcrumb) → click surfaces/outputs in B → A's canvas stays put;
   A's toolbar pin shows Activated.
3. Delete the pinned output → A falls back to following the selection; pin icon resets.
4. Select a non-sink op in the graph → selection clears in both windows; panels close per the
   existing auto-close rule.
5. Restart → the pin is restored (persisted with the layout).

`.help/`: one line on the output-setup page for the pin menu item.

## Out of scope (explicitly)
- `CanvasSelection` (sub-element plane) extraction — stays ad hoc in `SetupOutputView` until its own
  slice.
- Parameter window reading the shared selection — that *is* Phase A, next slice.
- Breadcrumb redesign / merging the two pin kinds — Phase B strip header.

## Size estimate
~150–250 LOC net across `OutputSetupHandling`, `OutputSetupModeView`, `SetupEntitySelection`,
`OutputWindow(.Toolbar)`, `OutputWindowState`, plus the test-set/help text. One reviewable PR.
