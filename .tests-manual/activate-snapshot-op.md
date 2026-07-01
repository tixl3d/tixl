---
id: activate-snapshot-op
title: ActivateSnapshot Operator
scope: operators
tags: [user, snapshots]
added: 2026-06-14
added-in-version: 4.3
prerequisites:
  - A writable user project is open (not a Lib-namespace composition).
  - The Graph and Variations windows are visible.
  - The composition already has at least two snapshots.
related-help:
  - ../.help/docs/using/PresetsAndSnapshots.md
---

The `[ActivateSnapshot]` operator lets your project switch to a chosen snapshot on cue — pick a snapshot by its number and fire a trigger, and it activates just as if you'd pressed that pad. It only works inside the editor while you're building; it has no effect once a project is exported.

## Step: Adding and triggering

**Action:**
Inside the composition that holds the snapshots, add `[ActivateSnapshot]` and connect its `Result` output into what's being rendered so it stays live. Set `Index` to `0`, then flip `SetTrigger` from off to on.

**Expected:**
- The moment `SetTrigger` turns on, the first snapshot becomes active and is applied — exactly as if you'd pressed its pad.
- Leaving `SetTrigger` on does nothing further; it only fires the moment it switches from off to on.

## Step: Stepping through with modulo wrap

**Action:**
Raise `Index` step by step (1, 2, 3, … and on past the number of snapshots you have), flipping `SetTrigger` off→on after each change.

**Expected:**
- The numbering wraps around: with 3 snapshots, `Index = 3` activates the first one again, `4` the second, and so on. Negative numbers wrap too.
- The active snapshot updates in the [ui:VariationWindow|Variations window] each time.

## Step: No snapshots

**Action:**
In a composition that has operators enabled for snapshots but no saved snapshots yet, fire the trigger.

**Expected:**
- Nothing happens (there's no snapshot to activate) and nothing breaks.
