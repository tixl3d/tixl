---
id: keyframe-copy-paste
title: Keyframe Copy and Paste Between Parameters
added: 2026-08-17
added-in-version: 4.3
scope: timeline
tags: [user]
prerequisites:
  - A project with one op that has an animated Vector3 parameter (e.g. a [Transform] with animated Translation, 3 keyframes between bar 1 and bar 3).
  - Three [Value] ops with animated Value parameters (one keyframe each is enough).
  - The Timeline window is visible in DopeView mode.
related-help: []
---

Keyframes copied with `Ctrl+C` in the dope sheet can be pasted onto other
parameters with `Ctrl+V`. Pasting matches curves with decreasing precision:
same parameter first, then the same input on another op of the same type, then
by component index. When none of these match — e.g. a Vector3 pasted onto
several scalar parameters — the copied curves are distributed over the target
curves in order.

## Step: Paste onto the same parameter at the playhead

**Action:**
Select the op with the animated Vector3 so its rows show in the dope sheet.
Select all its keyframes (drag a rubber band around them) and press `Ctrl+C`.
Move the playhead to bar 5 and press `Ctrl+V`.

**Expected:**
- A copy of all keyframes appears on the same parameter, starting exactly at
  bar 5 with the original spacing preserved (a 2-bar span stays a 2-bar span).
- The pasted keyframes are selected; the originals are not.
- A single undo removes all pasted keyframes.

## Step: Distribute a Vector3 onto scalar parameters

**Action:**
With the Vector3 keyframes still copied, select the three [Value] ops so their
Value rows show in the dope sheet. Move the playhead to bar 1 and press
`Ctrl+V`.

**Expected:**
- Each of the three Value parameters receives one component's keyframes: the
  first row in the dope sheet gets X, the second Y, the third Z.
- The earliest pasted keyframe sits at bar 1 on every row.
- A single undo removes the pasted keyframes from all three parameters.

## Step: Distribute scalars onto a Vector3

**Action:**
Select two of the [Value] ops, rubber-band their keyframes, and press `Ctrl+C`.
Then select only the Vector3 op and press `Ctrl+V`.

**Expected:**
- The first copied Value curve lands on the X component, the second on Y; Z
  receives nothing.

## Step: A single component keeps its lane on same-type targets

**Action:**
Add a second op of the same type as the Vector3 op (e.g. another [Transform])
and animate its Vector3 parameter with one keyframe. On the first op, select
only the Y-row keyframes, press `Ctrl+C`, then select the second op and press
`Ctrl+V`.

**Expected:**
- The keyframes land on the second op's Y component — not on X.
