---
id: video-clip-player-wired
title: Video Clip Player — Wired Clips
added: 2026-06-07
added-in-version: 4.2
scope: operators
tags: [user, essential]
prerequisites:
  - A scratch project is open with an empty Graph Window.
  - Two short test videos. The bundled `Operators/examples/Assets/videos/test-720p.mp4` works; a second, visually distinct clip makes the cut and overlap checks unambiguous. Clips with burned-in frame numbers help.
related-help:
  - ../.help/docs/operators/lib/io/video/VideoClipPlayer.md
---

This checks that `[VideoClipPlayer]` combines the `[VideoClip]`s you connect to it into one continuous
image: only clips that sit under the playhead are shown, they stack by `LayerIndex`, gaps are
transparent, each clip's `Color` (tint and opacity) blends where clips overlap, a cut-in doesn't blink,
and rendering to a file stays frame-exact. (Auto-collecting clips without connecting them is covered by
a separate set — this one is the connected workflow only.)

## Step: A single wired clip plays through the player

**Action:**
Place a `[VideoClipPlayer]` and a `[VideoClip]`. Set the VideoClip's `Path` to your first test video — it
appears as a clip on the timeline; leave it spanning a few seconds near the start. Drag from the
VideoClip's `Texture` output into the player's `VideoClips` input. Select the player so the [ui:OutputWindow|Output Window]
shows its image, position the playhead inside the clip's range, and play.

**Expected:**
- The video is visible in the Output Window while the playhead is within the clip's range on the timeline.
- Playback advances smoothly and the editor stays responsive.

## Step: Outside the clip's range the output is transparent

**Action:**
Move the playhead to before the clip's start, then to past its end.

**Expected:**
- With the playhead outside the clip's range, the player shows nothing / transparent — it does not
  freeze on the clip's first or last frame, and it is not solid black. (Put a colored background or
  checkerboard behind the output if you need to see the transparency.)

## Step: Two adjacent clips cut cleanly

**Action:**
Add a second `[VideoClip]` with a different `Path`. Position the two clips back-to-back on the timeline
(clip A then clip B, no overlap). Drag clip B's `Texture` into the player's `VideoClips` input as well.
Scrub the playhead slowly across the cut.

**Expected:**
- Only the clip whose range contains the playhead is shown.
- At the boundary the image switches straight from A to B — never both at once, never a transparent
  frame between them.

## Step: Overlapping clips composite, lowest LayerIndex on top

**Action:**
Drag the two clips so their ranges overlap for a second or two, and put them on different timeline layers.
Scrub the playhead into the overlap.

**Expected:**
- In the overlap both clips contribute to the image.
- The clip on the **lower** `LayerIndex` is drawn on top; the higher-layer clip is behind it.
- Outside the overlap, only the single active clip shows.

## Step: Per-clip Color crossfades and tints at the overlap

**Action:**
With the playhead in the overlap, select the clip that is on top and lower the **alpha** of its `Color`
input (e.g. toward 0.5, then toward 0). Then set its `Color` RGB to a strong hue such as red.

**Expected:**
- Lowering alpha makes the top clip see-through, revealing the clip behind it; at alpha 0 it disappears
  entirely and only the lower clip shows.
- The RGB of `Color` tints the clip (red `Color` → red-tinted clip).

## Step: Cut-in has no transparent blink (preroll)

**Action:**
Set the timeline playing and let it run forward across the A→B cut at normal speed — play through it,
don't scrub.

**Expected:**
- Clip B appears immediately at the cut. There is no one- or two-frame transparent flash before B's
  first frame.

## Step: Render-to-file is frame-exact across the cut

**Action:**
Connect the player's output to an output and render a range that spans the A→B cut via the Render/Export
window. Step through the rendered frames around the cut.

**Expected:**
- Every rendered frame is the correct source frame for its time — the right clip on each side of the cut,
  with no stale, duplicated, skipped, or out-of-order frames at the boundary.
- Two renders of the same range produce identical results.
