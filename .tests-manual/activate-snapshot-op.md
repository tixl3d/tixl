---
id: activate-snapshot-op
title: ActivateSnapshot Operator
scope: operators
tags: [snapshots]
added: 2026-06-14
added-in-version: 4.3
prerequisites:
  - A writable user project is open (not a Lib-namespace composition).
  - The Graph and Variations windows are visible.
  - The composition already has at least two snapshots.
related-help:
  - ../.help/docs/using/PresetsAndSnapshots.md
---

Covers the `[ActivateSnapshot]` operator, which activates a single snapshot by index on a trigger. This is an editor-only operator — it has no effect in an exported player.

## Step: Adding and triggering

**Action:**
Inside the composition that owns the snapshots, add `[ActivateSnapshot]` and connect its `Result` into the rendered command chain so it evaluates every frame. Set `Index` to `0`, then pulse `SetTrigger` (off → on).

**Expected:**
- On the rising edge of `SetTrigger`, the first snapshot (reading order) becomes active and is applied — exactly as if its launchpad pad were pressed.
- Holding `SetTrigger` high does nothing further; it only fires on the off→on edge.

## Step: Stepping through with modulo wrap

**Action:**
Increment `Index` (1, 2, 3, …, past the number of snapshots) and pulse `SetTrigger` after each change.

**Expected:**
- Each trigger activates `Index mod snapshotCount` — so with 3 snapshots, `Index = 3` activates the first again, `4` the second, and so on. Negative indices wrap too.
- The active snapshot updates in the Variations window each time.

## Step: No snapshots

**Action:**
In a composition that has snapshot-enabled operators but no saved snapshots, trigger the operator.

**Expected:**
- Nothing happens (no snapshot to activate); no error.
