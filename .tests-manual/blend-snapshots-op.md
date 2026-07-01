---
id: blend-snapshots-op
title: BlendSnapshots Operator
scope: operators
tags: [user, snapshots]
added: 2026-06-14
added-in-version: 4.3
prerequisites:
  - A writable user project is open (not a Lib-namespace composition).
  - The Graph, Parameter and Variations windows are visible.
  - The composition already has at least two snapshots (see the "Snapshot Control View" set to create them).
related-help:
  - ../.help/docs/using/PresetsAndSnapshots.md
---

The `[BlendSnapshots]` operator lets you cross-fade between snapshots automatically — instead of dragging the blend faders by hand, you feed it which snapshots to mix and how much weight each gets, and it does the blending for you. It only works inside the editor while you're building; it has no effect once a project is exported.

## Step: Adding the operator

**Action:**
Inside the composition that holds the snapshots, open the [ui:SymbolBrowser|Symbol Browser] and add `[BlendSnapshots]`. Connect its `Result` output into what's being rendered (for example into a `[Group]` that's on screen) so the operator stays live.

**Expected:**
- The operator appears with inputs `Enable`, `SnapshotIndices`, `WeightFactors`, and `Mode`.
- With `Enable` off, nothing changes and the operator's status (hover its title, or look in the [ui:ParameterWindow|Parameter window]) reads "Disabled."

## Step: Blending two snapshots

**Action:**
Set `Mode` to `ControllerIndices`. Give `SnapshotIndices` a list with two of your snapshots' controller numbers (e.g. `1, 2`) and `WeightFactors` a matching list (e.g. `1, 0`). Turn `Enable` on. Now animate the weights from `1, 0` towards `0, 1` (for example with two `[AnimValue]`s, or by editing the list by hand).

**Expected:**
- The output cross-fades from the first snapshot to the second as the weights shift.
- Only the proportions between the weights matter, not their size: `2, 0` looks exactly the same as `1, 0`.
- In the [ui:VariationWindow|Variations window] the two thumbnails show the live blend amount, and their faders move on their own but you **can't** drag them (cursor shows "not allowed", tooltip: "Blend is driven by a [BlendSnapshots] operator").
- The operator status reads "Blending 2 snapshot(s)."

## Step: Releasing control

**Action:**
Turn `Enable` off (or set all weight factors to 0, or delete or disconnect the operator).

**Expected:**
- The blend lets go: the composition returns to the active snapshot, and you can drag the Variations-window faders by hand again.
- With `Enable` off the status reads "Disabled"; with all weights zero it reads "All weight factors are zero."

## Step: Mode — position vs controller number

**Action:**
Switch `Mode` to `SnapshotIndices` with `SnapshotIndices = 0, 1`. Then reorder the snapshots in the Variations window and watch. Switch back to `ControllerIndices` and reorder again.

**Expected:**
- In `SnapshotIndices` mode, `0, 1` always means the first two snapshots in the current order, so reordering changes which snapshots get blended.
- In `ControllerIndices` mode the same numbers keep pointing at the same snapshots no matter how you reorder them.

## Step: Status warnings

**Action:**
Try each of these: (a) point at a controller number that none of your snapshots uses; (b) open a different composition while the operator stays enabled.

**Expected:**
- (a) Status reads "None of the snapshot indices were found." (or a "… N index(es) not found" notice if only some are missing).
- (b) Status reads "This composition is not currently active for snapshots." and the blend lets go.
