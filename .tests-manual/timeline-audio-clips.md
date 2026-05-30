---
id: timeline-audio-clips
title: Timeline Audio Clips — drop, drag, trim, delete
scope: timeline
tags: [essential, hardware]
prerequisites:
  - A project is open. The composition you'll edit has (or can have) its own Composition Settings enabled.
  - A short audio file (5–30s, .wav / .mp3 / .ogg) is available on disk for drop testing.
  - Audio output is configured and working (mic / loopback isn't required, just speakers).
related-help:
  - ../.help/docs/using/Timeline.md
  - ../.help/docs/using/LivePerformances.md
---

End-to-end verification of the symbol-level `TimelineAudioClip` feature: file drop, rendering with waveform, playback sync, drag / trim / delete with `SourceOffsetSecs` adjustment, cross-type interaction with op-backed `TimeClip`s, and regression checks for the legacy soundtrack path.

## Step: Existing soundtrack still plays (regression)

**Action:**
Open one of the bundled example projects with a project soundtrack (e.g.
`Operators/Examples/user/still/proj-katsumaki/`). Hit play.

**Expected:**
- The soundtrack waveform shows as the background image across the timeline.
- Audio plays in sync with the playhead.
- `[AudioReaction]`-driven operators react to the audio (FFT routing works).
- Scrubbing the playhead re-syncs the audio cleanly.

## Step: Drop a `.wav` onto the layers area

**Action:**
With a composition open that has its own playback settings enabled, drag a
`.wav` file from your file explorer onto the **clip area** of the timeline
(below the playhead ruler, where op-backed clips appear). Drop somewhere that
isn't already occupied.

**Expected:**
- A new audio clip body appears at the drop X position, on a layer derived
  from drop Y.
- The clip body has an audio-tinted fill (different from random per-id colour
  of op-backed clips), a file-audio icon in the top-left, and the filename
  (without extension) as the label.
- The file is copied into the active project's `Assets/audio/` folder if it
  wasn't there already.
- The clip's TimeRange width matches the file's natural duration at the
  current BPM (option β — native rate).

## Step: Waveform image populates

**Action:**
Wait a few seconds after the drop. (BASS analyses the file on a background thread.)

**Expected:**
- The clip body fills with the waveform image of the source file.
- The image is *not* stretched — it represents the full source content within
  the clip's body width.

## Step: Playback and scrubbing

**Action:**
Position the playhead before the clip's TimeRange.Start, hit play, and let it
run past the clip. Then scrub manually back and forth across the clip.

**Expected:**
- Audio starts the moment the playhead enters the clip's TimeRange.
- Audio stops when the playhead leaves the TimeRange (or runs out of source content).
- Scrubbing re-syncs audio to the new playhead position within a frame.
- BPM changes (if you adjust them) don't pitch-shift the audio.

## Step: Click selection and hover tooltip

**Action:**
Click the clip body once. Then hover (without clicking) over the body.

**Expected:**
- A selection border highlights the clip.
- After a brief hover, a tooltip appears showing the filename, full asset
  path, duration in seconds, volume, and (for main-soundtrack-flagged clips
  only) a note about it.

## Step: Drag the clip body horizontally

**Action:**
Click and drag the clip body left and right at a slow, near-zero-velocity pace.

**Expected:**
- The clip moves smoothly with the cursor along the timeline — no per-pixel
  jitter or "stuttering" while the cursor crawls. (The drag excludes the
  SelectionRangeIndicator from snap targets, which would otherwise re-snap to
  the selection's own edge frame-to-frame.)
- Snapping still engages against beat raster lines and other clips' edges.
- Releasing the drag pushes a single undo entry — Ctrl+Z restores the
  original TimeRange.

## Step: Drag the clip body vertically (Y-drag for layer change)

**Action:**
Click and drag the clip body up or down, far enough to cross a full layer
height. Drop both above and below existing rows.

**Expected:**
- The clip snaps cleanly between layer rows (integer LayerIndex changes).
- The clip can extend onto a new layer above or below the existing range.
- Sub-layer-height movements don't cause jitter — only crossing a full row
  triggers the LayerIndex change.
- Undo restores the original layer.

## Step: Mute a clip via the inspector

**Action:**
Select an audible audio clip (one that plays back). In the Parameter Window, tick the
**Muted** checkbox. Hit play and listen. Untick it.

**Expected:**
- With Muted on: the clip body renders noticeably faded compared to its siblings.
  Playback through the clip's TimeRange produces no audio from this clip; other clips
  continue to play normally.
- With Muted off: opacity returns to normal and audio plays again.
- Saving and reopening the project preserves the mute state on the clip.
- Multi-select two clips with mixed mute state; the bulk inspector shows "Muted (mixed)"
  and one click resolves them all to the same state.

## Step: Parameter Window shows clip fields and accepts negative Layer

**Action:**
Click the clip body once to select it (no operator selected on the graph).
Look at the Parameter Window.

**Expected:**
- The Audio Clip inspector renders — asset path, volume, source offset / duration, layer, and the main-soundtrack flag are visible.
- Scrubbing the **Layer** field down past 0 sets the layer to a negative integer
  (the field is no longer clamped at 0). The clip jumps to the corresponding row above the timeline grid origin.
- Selecting multiple audio clips switches the inspector to the bulk-edit view; shifting Layer there applies the same delta to every selected clip and still accepts negative values.

## Step: Trim the start handle (DAW-style)

**Action:**
Click the left-edge resize-EW handle of the clip and drag it to the right.
Then drag it back to the left.

**Expected:**
- The clip's left edge moves with the cursor; the right edge stays put.
- The audio content stays anchored to its original timeline position —
  the part you trimmed off no longer plays, but the remainder still plays
  at the same wall-clock time.
- The waveform image inside the body shows the *later* part of the source
  (the front section was trimmed off).
- Dragging back to the left re-reveals the trimmed portion (audio reappears
  at its original position).
- Once the start-trim reaches the file's beginning, further leftward drag
  is blocked — the clip does **not** extend into silence territory.

## Step: Trim the end handle

**Action:**
Click the right-edge resize-EW handle and drag it to the left.

**Expected:**
- The clip's right edge moves with the cursor; the left edge stays put.
- The waveform image truncates from the right (shows only the audible portion).
- Dragging the end past the source content's natural end is blocked — the
  clip body cannot extend past where audio runs out.
- For an already-stretched clip (loaded from old data), the upper clamp is
  soft: rightward drag is blocked but leftward shrinking works freely, and
  once the clip drops below natural max the ceiling tightens.

## Step: Delete and undo

**Action:**
Select an audio clip and press the `Delete` key. Then Ctrl+Z.

**Expected:**
- The clip disappears from the clip area.
- The corresponding `.wav` file in `Assets/audio/` is **not** deleted.
- Ctrl+Z restores the clip at its original list position.

## Step: Multi-drop creates stacked clips

**Action:**
Drag two or more `.wav` files onto the clip area in a single drop
operation. (If your OS only allows single-file drag, do two drops in quick
succession instead.)

**Expected:**
- Each file becomes its own clip.
- Subsequent clips land on stacked layers so they don't overlap on the same row.
- A single undo reverts all of them at once (when dropped as a batch).

## Step: Single-click between clip types replaces selection

**Action:**
Click an op-backed `[TimeClip]` (no modifier). Then click an audio clip. Then click the
op-backed clip again. Then alternate a few more times.

**Expected:**
- Each click leaves **only** the just-clicked clip selected; the previously-selected
  clip of the OTHER type is cleared.
- The Parameter window switches between op-clip details and audio-clip inspector
  accordingly — never shows a "phantom" selection from the other side.
- Shift-click still extends the selection across both types (standard additive
  behavior preserved by the cross-type drag step further down).

## Step: Cross-type drag (mixed selection)

**Action:**
With at least one op-backed `[TimeClip]` and one audio clip in the same
composition, multi-select both (click one, then Shift+click the other so
both have selection borders). Then drag the body of either clip.

**Expected:**
- Both clips move together along X and Y (LayerIndex changes apply to both).
- A single undo reverses both moves (op-side and audio-side commands push
  as separate undo entries — pressing Ctrl+Z twice undoes both halves).

## Step: Cross-type selection rectangle

**Action:**
Drag a selection fence rectangle over an area containing both an op-backed
clip and an audio clip.

**Expected:**
- Both clip types are selected.
- Shift+drag adds to selection; Alt+drag (or whatever the existing
  Remove-mode modifier is) removes from selection on both sides.

## Step: Cross-type snapping

**Action:**
Drag an audio clip's body near the edge of an op-backed clip. Then drag an
op-backed clip near the edge of an audio clip.

**Expected:**
- The dragged clip snaps to the other type's TimeRange edges when close
  enough (subject to the existing snap threshold).
- Both clip types act as snap anchors for each other.

## Step: Render to video with audio

**Action:**
With a composition that has a main soundtrack (or any audio clip), render
a short segment to a video file via the export window.

**Expected:**
- The rendered video contains the audio track.
- `[AudioReaction]`-driven effects respond correctly in the rendered output.
- Multiple audio clips overlapping in time mix together in the export.

## Step: Legacy project migration

**Action:**
Open an example project whose `.t3` JSON still uses pre-rewrite field names
(`IsSoundtrack`, `StartTime`, `EndTime`, `Bpm`, `DiscardAfterUse`, `FilePath`).
Many of the bundled example projects fit this — check
`Operators/Examples/user/still/synchotron/` or similar.

**Expected:**
- The project loads without warnings about the audio clip data.
- The soundtrack plays as before.
- Saving the project rewrites the JSON with the new field names
  (`IsMainSoundtrack`, `TimeRange`, `AssetPath`). Re-opening still works.
- If the project had `Bpm` on the clip, the value migrated into
  `Playback.Bpm` and the per-clip Bpm field is gone.
