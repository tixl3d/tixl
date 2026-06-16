# Horizontally flip LED layout for APCmini / APC40 controllers

Ticket: #1081 — https://github.com/tixl3d/tixl/issues/1081
Size: —   Milestone: v4.2

## Problem
APCmini/APC40 hardware numbers buttons/LEDs from the BOTTOM-LEFT, which makes controller indices
confusing in the editor. Rather than supporting flipped editing layouts, flip the *representation sent
to the controllers* so indices map sensibly. Expected snapshot impact is small (the feature isn't widely
used and layouts can be re-adjusted with the new ControllerGrid editor).

## Affected code
Mapping lives per device under `Editor/Gui/Interaction/Midi/CompatibleDevices/`:
- `ApcMini.cs:66-69` — `GridLayout = new("APC Mini", 8, 8, (row, column) => (8-1-row)*8 + column);`
  (rows already inverted to match hardware; columns are *not* flipped).
- `Apc40Mk1.cs` — outbound `SendColor()` ~366-379 computes `row/col` then note/channel; inbound
  `ConvertNoteToButtonId()` ~403-432.
- `Apc40Mk2.cs` — outbound `SendLedState()` ~414-431; inbound `ConvertNoteToButtonId()` ~488-497.
- `ApcMiniMk2.cs` — `SendColor()` ~112-129 (simple note mapping; no row/col decomposition exposed).
- Base helpers: `CompatibleMidiDevice.cs:341` (`UpdateRangeLeds`), `ControllerGridLayout.cs`.

## Proposed approach
Apply a horizontal (column) mirror — `col → (columns-1 - col)` — consistently in three coupled places per
device so hardware LEDs and button presses stay in sync with stored indices:
1. The `GridLayout` lambda / index→cell mapping (UI + LED out).
2. `SendColor()` / `SendLedState()` outbound column computation.
3. `ConvertNoteToButtonId()` inbound column computation.
Centralize the mirror (e.g. a flag/helper on `ControllerGridLayout` or the base device) instead of editing
each Mk1/Mk2 copy independently, since the Mk1/Mk2 logic is duplicated.

## Risks / side-effects
- **Bidirectional mapping = the main risk.** Outbound (index→LED) and inbound (button→index) must be flipped
  *together*; flipping one side desyncs LEDs from presses.
- **Existing saved snapshots** are stored by activation index. Flipping changes which physical button maps to
  an index, so previously-saved layouts will appear mirrored. The ticket accepts this (low usage), but it's a
  behavior change on existing user data — call it out in the PR and the changelog; no data migration is
  planned (re-adjust via the ControllerGrid editor).
- Per-device duplication (Mk1/Mk2) makes it easy to fix one and miss another — do all four, plus ApcMiniMk2.

## Open questions
- Centralize the flip in `ControllerGridLayout` (one place, all devices) vs. per-device edits?
- Confirm ApcMiniMk2's simple note path needs the same flip and where.
- Is a one-time opt-in/migration for existing snapshots wanted, or is "re-adjust manually" acceptable
  (as the ticket implies)?
