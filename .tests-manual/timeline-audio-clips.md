---
id: timeline-audio-clips
title: Timeline Audio Clips — drop, drag, trim, delete
added: 2026-05-26
added-in-version: 4.2
scope: timeline
tags: [user, essential, hardware]
prerequisites:
  - A project is open. The composition you'll edit has (or can have) its own Composition Settings enabled.
  - A short audio file (5–30s, .wav / .mp3 / .ogg) is available on disk for drop testing.
  - Audio output is configured and working (mic / loopback isn't required, just speakers).
related-help:
  - ../.help/docs/using/Timeline.md
  - ../.help/docs/using/LivePerformances.md
---

You can drop an audio file straight onto the [ui:Timeline|timeline] and treat it like a clip — move it,
trim it, mute it, stack several, and have them play in sync with everything else. This set
walks through dropping in audio, seeing its waveform, playing it back, editing it, and
making sure it sits and snaps nicely alongside the operator-driven clips that were already
on the timeline.

## Step: Existing soundtrack still plays (regression)

**Action:**
Open one of the bundled example projects with a project soundtrack (e.g.
`Operators/Examples/user/still/proj-katsumaki/`). Hit play.

**Expected:**
- The soundtrack's waveform shows behind the timeline.
- Audio plays in sync with the playhead.
- Operators driven by [AudioReaction] react to the music.
- Scrubbing the playhead keeps the audio in sync.

## Step: Drop a `.wav` onto the layers area

**Action:**
With a composition open that has its own playback settings enabled, drag a
`.wav` file from your file explorer onto the **clip area** of the timeline
(below the playhead ruler, where op-backed clips appear). Drop somewhere that
isn't already occupied.

**Expected:**
- A new audio clip appears where you dropped it, on the row matching the drop height.
- The clip has its own audio-style fill (distinct from the per-operator colours of
  operator-driven clips), a small audio-file icon in the top-left, and the file's name
  (without the extension) as its label.
- If the file wasn't already part of the project, it's copied into the project's audio
  assets so the project stays self-contained.
- The clip's length matches the audio file's real duration at the current BPM.

## Step: Waveform image populates

**Action:**
Wait a few seconds after the drop.

**Expected:**
- The clip fills with the waveform of the audio file.
- The waveform is *not* stretched — it shows the file's full content across the clip's
  width.

## Step: Playback and scrubbing

**Action:**
Position the playhead before the start of the clip, hit play, and let it
run past the clip. Then scrub manually back and forth across the clip.

**Expected:**
- Audio starts the moment the playhead reaches the clip.
- Audio stops when the playhead leaves the clip (or the clip runs out of sound).
- Scrubbing keeps the audio in sync with where you move the playhead.
- Changing the BPM doesn't change the pitch of the audio.

## Step: Click selection and hover tooltip

**Action:**
Click the clip body once. Then hover (without clicking) over the body.

**Expected:**
- A selection border highlights the clip.
- After a brief hover, a tooltip appears showing the file name, its location, the duration
  in seconds, the volume, and (only for a clip marked as the main soundtrack) a note saying
  so.

## Step: Drag the clip body horizontally

**Action:**
Click and drag the clip body left and right at a slow, near-zero-velocity pace.

**Expected:**
- The clip moves smoothly with the cursor along the timeline — no jitter or stuttering
  even while the cursor barely crawls.
- Snapping still engages against the beat lines and other clips' edges.
- Releasing the drag is a single undo step — Ctrl+Z puts the clip back where it started.

## Step: Drag the clip body vertically (Y-drag for layer change)

**Action:**
Click and drag the clip body up or down, far enough to cross a full layer
height. Drop both above and below existing rows.

**Expected:**
- The clip snaps cleanly from one row to the next.
- The clip can move onto a new row above or below the existing ones.
- Small movements within a row don't cause jitter — the clip only jumps rows once you
  cross a full row's height.
- Undo restores the original row.

## Step: Mute a clip via the inspector

**Action:**
Select an audible audio clip (one that plays back). In the Parameter Window, tick the
**Muted** checkbox. Hit play and listen. Untick it.

**Expected:**
- With Muted on: the clip looks noticeably faded next to the others. Playing through it
  produces no sound from this clip; other clips keep playing normally.
- With Muted off: the clip returns to full brightness and plays again.
- Saving and reopening the project keeps the clip's mute state.
- Select two clips with different mute states; the shared inspector shows "Muted (mixed)",
  and one click sets them both to the same state.

## Step: Parameter Window shows clip fields and accepts negative Layer

**Action:**
Click the clip body once to select it (no operator selected on the graph).
Look at the Parameter Window.

**Expected:**
- The Audio Clip inspector appears — the file location, volume, start offset / duration, layer, and the main-soundtrack toggle are all visible.
- Scrubbing the **Layer** field down past 0 goes into negative numbers (it's not stuck at 0). The clip jumps to the matching row above the timeline's origin.
- Selecting several audio clips switches the inspector to the shared-edit view; changing Layer there shifts every selected clip by the same amount and still allows negative values.

## Step: Trim the start handle (DAW-style)

**Action:**
Grab the clip's left edge (the cursor changes to a horizontal resize arrow) and drag it to
the right. Then drag it back to the left.

**Expected:**
- The clip's left edge moves with the cursor; the right edge stays put.
- The audio stays anchored to its place on the timeline — the trimmed-off front no longer
  plays, but the rest still plays at the same moment as before.
- The waveform now shows the *later* part of the audio (the front was trimmed off).
- Dragging back to the left brings the trimmed portion back (the audio returns at its
  original position).
- Once the trim reaches the start of the file, you can't drag further left — the clip
  won't extend into silence.

## Step: Trim the end handle

**Action:**
Grab the clip's right edge and drag it to the left.

**Expected:**
- The clip's right edge moves with the cursor; the left edge stays put.
- The waveform shortens from the right (showing only the part that still plays).
- You can't drag the end past where the audio runs out — the clip won't extend into
  silence.
- For a clip that was already stretched (from an older project), the limit is gentle:
  you can't drag it longer, but you can freely shrink it, and once it drops below its
  natural length the limit tightens up again.

## Step: Loop a clip's source range

**Action:**
Select an audio clip, enable its `Loop` parameter, then drag the clip's end handle
well past the audio file's length.

**Expected:**
- The audio repeats seamlessly for as long as the clip lasts — in playback and in a
  rendered video.
- The clip body shows the waveform tiled per repetition, with a subtle vertical line
  at each loop border.
- Trimming the clip's start changes where each repetition begins.

## Step: Footage extent while trimming

**Action:**
Hover an audio clip, then drag its start or end handle slowly.

**Expected:**
- A thin outline appears showing the full extent of the audio file on the timeline.
- The mouse cursor stays a steady resize cursor for the whole drag — no flicker.
- The trim snaps when a handle reaches the file's first or last moment.
- Dragging the start before the file's beginning leaves that stretch silent during
  playback (the audio doesn't start early).

## Step: Set as Main Soundtrack via context menu

**Action:**
Right-click a single selected audio clip and choose "Set as Main Soundtrack".

**Expected:**
- The clip's `Display` switches to `BackgroundImage`: its image renders behind the
  timeline (immediately, without playing the clip first) and audio-reactive ops
  respond to it.
- The clip's block disappears from the timeline layers — the background image is its
  only representation. It extends to the full source duration, ignoring any trim.
- Any other clip that previously was the main soundtrack loses the designation.
- To reposition or un-designate the soundtrack, set its `Display` parameter back to
  `Clip` (find the op via the graph or Project Settings → Audio → "Select and focus
  Main Soundtrack"): the block reappears as a normal clip.
- Undo reverts the whole designation change at once.

## Step: Delete and undo

**Action:**
Select an audio clip and press the `Delete` key. Then Ctrl+Z.

**Expected:**
- The clip disappears from the timeline.
- The audio file itself stays in the project's assets — it is **not** deleted.
- Ctrl+Z restores the clip in its original place.

## Step: Multi-drop creates stacked clips

**Action:**
Drag two or more audio files onto the clip area in a single drop. (If your system only
lets you drag one file at a time, do two drops in quick succession instead.)

**Expected:**
- Each file becomes its own clip.
- The clips stack onto separate rows so they don't overlap.
- A single undo removes all of them at once (when dropped together).

## Step: Single-click between clip types replaces selection

**Action:**
Click an operator-driven clip such as a [TimeClip] (no modifier). Then click an audio clip.
Then click the operator-driven clip again. Then alternate a few more times.

**Expected:**
- Each click leaves **only** the just-clicked clip selected; whatever you had selected of
  the OTHER type is deselected.
- The Parameter window switches between the operator-clip details and the audio-clip
  inspector to match — it never shows a leftover selection from the other side.
- Shift-click still extends the selection across both types (the additive behaviour is
  preserved, checked in the cross-type drag step further down).

## Step: Cross-type drag (mixed selection)

**Action:**
With at least one operator-driven clip such as a [TimeClip] and one audio clip in the same
composition, select both (click one, then Shift+click the other so both have selection
borders). Then drag the body of either clip.

**Expected:**
- Both clips move together, both sideways and between rows.
- Undo reverses the move — the operator clip and the audio clip undo as separate steps, so
  pressing Ctrl+Z twice puts both back.

## Step: Cross-type selection rectangle

**Action:**
Drag a selection rectangle over an area containing both an operator-driven clip and an
audio clip.

**Expected:**
- Both clip types are selected.
- Shift+drag adds to the selection; the remove-mode modifier (Alt) takes clips back out —
  for both types.

## Step: Cross-type snapping

**Action:**
Drag an audio clip near the edge of an operator-driven clip. Then drag an operator-driven
clip near the edge of an audio clip.

**Expected:**
- The dragged clip snaps to the other clip's edges when it gets close enough.
- Both clip types snap to each other.

## Step: Render to video with audio

**Action:**
With a composition that has a main soundtrack (or any audio clip), render
a short segment to a video file via the export window.

**Expected:**
- The rendered video contains the audio.
- Effects driven by [AudioReaction] respond correctly in the rendered output.
- Where multiple audio clips overlap in time, they mix together in the export.

## Step: Legacy project migration

**Action:**
Start the editor with an editable project whose soundtrack was saved before the
audio-clip rework (a settings-based soundtrack, not an [AudioClip] op). Open its
main composition.

**Expected:**
- The console shows a "Migrated legacy soundtrack entry(s) to visible [AudioClip]
  op(s)" line for the project at startup.
- An [AudioClip] op now exists in the composition; the former main soundtrack has
  `Display` set to `BackgroundImage` and still renders behind the timeline.
- The soundtrack plays just as it did before, at the right tempo.
- Saving, closing and reopening keeps everything working; the saved project file no
  longer carries the old settings-based clip list.
- Opening a *read-only* package (e.g. bundled Examples from an install) does not get
  migrated — it keeps playing via the legacy path.
