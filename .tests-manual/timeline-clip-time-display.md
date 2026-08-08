---
id: timeline-clip-time-display
title: Dope Sheet — keyframes draw and edit in playback time
added: 2026-08-08
added-in-version: 4.3
scope: timeline
prerequisites:
  - A project is open with its own Composition Settings enabled.
  - A short video file (5s or longer) for creating a [VideoClip].
  - Timeline format set to Bars. Snapping on.
related-help:
  - ../.help/docs/using/Timeline.md
---

The dope sheet, keyset strip (dots below the ruler), selection range indicator, snapping, and
time-warp all operate in **playback time**: a keyframe inside a stretched or slipped clip is drawn
where it takes effect, sits under the playhead, and drags/fences land where the mouse is — per row,
so two clips with different stretches coexist on one timeline.

**Setup for all steps:** a `[VideoClip]` with Clip Start/End `0/8` and Source Start/End `0/4`
(50% speed — set via **Edit Clip Times**), with two **Color** keys at content bars 1 and 2
(alpha 1 → 0), i.e. a fade-out playing between bars **2 and 4**. Keep the clip selected so its
rows show.

## Step: Keys draw at playback positions

**Action:**
Look at the Color row and the keyset strip below the ruler.

**Expected:**
- The two keys are drawn at bars **2 and 4** — under where the fade audibly/visibly happens.
- The strip dots sit at the same X positions as the keys in the row.
- Move the playtime to bar `2` with `.` / `,`: playhead, key, dot, and the parameter window's
  keyframe indicator all agree.

## Step: Stretching the clip moves the keys on screen

**Action:**
`Alt`-drag the clip's end handle from bar 8 to bar `4` (100% speed), then back to `8`.

**Expected:**
- At 100%: the keys draw at bars **1 and 2**. Back at 50%: bars **2 and 4** again.
- The keys visibly slide with the handle while dragging — no jump at release.

## Step: Dragging a key lands where the mouse is

**Action:**
1. Drag the key at bar 4 to bar `6` (watch the snap to the raster).
2. Play bars 2–7.

**Expected:**
- The key follows the mouse 1:1 and lands at bar 6 (not at half the distance).
- The fade now ends at playback bar **6**.
- `Ctrl + Z` restores it to bar 4.

## Step: Fence and strip selection match the drawn positions

**Action:**
1. Fence-select (drag on the row background) exactly around the key drawn at bar 2.
2. Then click the strip dot at bar 4.

**Expected:**
- The fence catches the key the rectangle visually covers — and only that one.
- The dot click selects the bar-4 key; its icon fills.

## Step: Cluster drag from the strip

**Action:**
Drag the strip dot at bar 2 to bar `3`.

**Expected:**
- The keys under that dot move to bar 3 — exactly the mouse distance, despite the 50% clip.
- Playback: the fade now starts at bar 3. `Ctrl + Z` restores.

## Step: Mixed rows — an unclipped op alongside

**Action:**
1. Add any op outside the clip (e.g. a `[Value]`), animate it with keys at bars 2 and 4
   (`Alt + click` at each playtime), and select both ops so both rows show.
2. Select all four keys (fence across both rows) and drag the selection one bar right using the
   selection range indicator's middle bar.

**Expected:**
- Both rows' keys sit at bars 2 and 4 before the drag — aligned across rows.
- After the drag, *all* keys draw at bars 3 and 5, and play there: the unclipped row moved 1 bar
  in its curve, the clipped row moved 0.5 bars internally — same on-screen result.
- The selection range indicator spans bars 3–5 (the drawn positions).

## Step: Snapping across differently-mapped rows

**Action:**
Drag the `[Value]` key at bar 3 slowly toward the clip row's key at bar 3... (first restore state
with `Ctrl + Z` if needed so keys differ) — drag any unclipped key toward a clipped key's drawn
position.

**Expected:**
- The dragged key snaps when the two keys visually align on screen.

## Step: Regression — identity clips unchanged

**Action:**
Set the clip back to Clip 0/4, Source 0/4 (100%, identity). Repeat a quick key drag, fence, and
strip click.

**Expected:**
- Everything behaves exactly as before this change.

> **Known remaining raw-time areas (not failures):** the fullscreen **Curve** mode and the inline
> curve pane still display raw curve time; copy/paste and Duplicate offsets are raw as well.
