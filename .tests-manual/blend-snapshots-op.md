---
id: blend-snapshots-op
title: BlendSnapshots Operator
scope: operators
tags: [snapshots]
added: 2026-06-14
added-in-version: 4.3
prerequisites:
  - A writable user project is open (not a Lib-namespace composition).
  - The Graph, Parameter and Variations windows are visible.
  - The composition already has at least two snapshots (see the "Snapshot Control View" set to create them).
related-help:
  - ../.help/docs/using/PresetsAndSnapshots.md
---

Covers the `[BlendSnapshots]` operator, which drives the editor's snapshot cross-fade procedurally from operator inputs. This is an editor-only operator — it has no effect in an exported player.

## Step: Adding the operator

**Action:**
Inside the composition that owns the snapshots, open the Symbol Browser and add `[BlendSnapshots]`. Connect its `Result` output into the composition's command chain (e.g. into a `[Group]` that is being rendered) so the operator is evaluated every frame.

**Expected:**
- The operator appears with inputs `Enable`, `SnapshotIndices`, `WeightFactors`, and `Mode`.
- With `Enable` off, nothing changes and the operator's status (hover its title / check the Parameter window) reads "Disabled."

## Step: Blending two snapshots

**Action:**
Set `Mode` to `ControllerIndices`. Feed `SnapshotIndices` a list with two of your snapshots' controller indices (e.g. `1, 2`) and `WeightFactors` a matching list (e.g. `1, 0`). Turn `Enable` on. Now animate the weights from `1, 0` towards `0, 1` (e.g. with two `[AnimValue]`s or by editing the list).

**Expected:**
- The output cross-fades from the first snapshot to the second as the weights shift.
- The weights are normalized: `2, 0` looks identical to `1, 0`; only the relative proportions matter.
- In the Variations window the two thumbnails show the live blend weight, and their faders move but are **not** draggable (cursor shows "not allowed", tooltip: "Blend is driven by a [BlendSnapshots] operator").
- The operator status reads "Blending 2 snapshot(s)."

## Step: Releasing control

**Action:**
Turn `Enable` off (or set all weight factors to 0, or delete/disconnect the operator).

**Expected:**
- The blend is released: the composition returns to the active snapshot, and the Variations-window faders become draggable again.
- With `Enable` off the status reads "Disabled"; with all weights zero it reads "All weight factors are zero."

## Step: Mode — ordinal vs controller index

**Action:**
Switch `Mode` to `SnapshotIndices` with `SnapshotIndices = 0, 1`. Then reorder the snapshots in the Variations window and observe. Switch back to `ControllerIndices` and reorder again.

**Expected:**
- In `SnapshotIndices` mode, `0, 1` always addresses the first two snapshots in reading order, so reordering changes which snapshots are blended.
- In `ControllerIndices` mode the same indices keep addressing the same snapshots regardless of reordering.

## Step: Status warnings

**Action:**
Trigger each of these: (a) reference a controller index that no snapshot uses; (b) open a different composition while the operator stays enabled.

**Expected:**
- (a) Status reads "None of the snapshot indices were found." (or a "… N index(es) not found" notice if only some are missing).
- (b) Status reads "This composition is not currently active for snapshots." and the blend is released.
