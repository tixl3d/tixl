---
id: combine-as-time-clip
title: Combine as Time Clip — selection becomes a timeline clip
added: 2026-08-08
added-in-version: 4.3
scope: graph
prerequisites:
  - A project with an editable (user) package is open, with its own Composition Settings enabled.
related-help:
  - ../.help/docs/using/Timeline.md
---

The "Combine as time clip" checkbox in the Combine dialog now works (it was a silent no-op since
2021). Combining a selection with it checked produces a symbol whose primary `Command` output is a
`TimeClipSlot` — the new operator appears on the timeline as a clip, evaluates only inside its clip
range, and keyframes inside it travel with the clip.

> **Note:** combining clears the undo history (creating a symbol/assembly can't be cleanly undone).
> The dialog hint says so — this is pre-existing behaviour, verify it's still communicated.

## Step: Combine a render chain as a time clip

**Action:**
Build a small chain in your user project (e.g. `[Value]` → some texture generator → `[Layer2d]` →
`[RenderTarget]`/output, anything with a `Command` connection leaving the selection). Select the ops,
right-click → **Combine into new symbol**. Hover the "Combine as time clip" checkbox.

**Expected:**
- A tooltip explains what combining as a time clip means.

**Action:**
Check the box, name the symbol, and Combine.

**Expected:**
- The new operator replaces the selection, with the outgoing `Command` connection intact.
- A clip appears in the timeline's clip area at the playhead, 4 bars long.

## Step: Clip gating works

**Action:**
Play across the clip's range; also scrub before and after it.

**Expected:**
- The combined content renders only while the playhead is inside the clip.
- Outside the clip, its contribution disappears (like any `[TimeClip]`-gated content).
- Dragging and trimming the clip works like any other clip; `Return` renames it.

## Step: Animations inside travel with the clip

**Action:**
Enter the combined symbol (double-click the clip or the op), animate a parameter inside with two
keyframes. Leave, then drag the clip a few bars later. Play.

**Expected:**
- The animation happens at the clip's new position — keys travel with the clip.

## Step: Mixed outputs — texture stays a plain output

**Action:**
Make a selection where both a `Command` connection *and* a `Texture2D` connection leave the
selection (e.g. one branch rendering, one branch exposing a texture). Combine as time clip.

**Expected:**
- The `Command` output is the clip output (the symbol shows on the timeline).
- The `Texture2D` output still works as a normal connection to its previous consumer.

## Step: Selection with no outgoing connections

**Action:**
Select one or two ops whose outputs are not connected to anything outside the selection. Combine as
time clip.

**Expected:**
- The new symbol still has a (unconnected) time-clip output and appears on the timeline as a clip.
- No compile error dialog.

## Step: Unchecked box still produces a plain symbol (regression)

**Action:**
Combine another selection with the checkbox **off**.

**Expected:**
- The result behaves exactly as before: a normal operator, no clip on the timeline.
