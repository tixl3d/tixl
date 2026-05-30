---
id: dataclip-editing
title: DataClip — inspection and editing
scope: timeline
tags: [essential]
prerequisites:
  - A project is open with at least one recorded `.data` file on the timeline (run `recording-io-data` first if you don't have one).
  - The composition's Syncing mode is **Timeline**.
related-help:
  - ../.help/docs/using/Recording.md
---

Verification of the DataClip output preview (clip mode), the in-canvas Remove flow with
undo / write-back, and the timeline-side visualisation of interval events. These steps
exercise the editing surfaces around a recorded clip — not the recording itself.

## Step: Output preview works standalone (no SimulateIoData)

**Action:**
Select a `LoadDataClip` op on the graph that has **nothing connected** to its `Clip`
output. Open the Output Window and pin the `Clip` output.

**Expected:**
- The DataSet view shows the recorded channels and events immediately, on the first
  frame the window draws — no need to wire a `SimulateIoData` op first.
- The horizontal time raster is visible and labelled in source seconds.
- A vertical playhead line sits at the timeline's mapped source position; pausing the
  timeline freezes it, scrubbing the timeline moves it along the source-time axis.

## Step: Clip-boundary overlay matches the TimeClip's SourceRange

**Action:**
Still in the Output Window with the DataClip preview open, look at the canvas
background. Note where the orange-tinted shading begins and ends.

**Expected:**
- The visible canvas area inside `SourceRange.Start … SourceRange.End` (in source
  seconds) is unshaded; everything outside that interval is shaded with a faint
  `StatusAnimated` tint.
- Two thin vertical lines mark the start and end of the source slice — the same palette
  the composition-level TimeClip view uses on `SourceRange` handles.
- Trimming the clip in the timeline updates the overlay live: trim the right edge inward
  and the right boundary line moves to the new edge, the shaded region grows to fill the
  rest.

## Step: Interval events render as bars in the clip body

**Action:**
Look at the data clip on the timeline (not the Output Window). Make sure the recording
includes MIDI note events.

**Expected:**
- Note events render as **horizontal bars** spanning from each note-on to its note-off,
  inside the clip body — not as 2 px ticks at the note-on position.
- CC / OSC channels in the same clip still render as the existing tick density visualisation.
- A note recorded mid-clip but never released (recorder shut down mid-note) stretches to
  the right edge of the clip body instead of disappearing.

## Step: Remove selected channels via the output view

**Action:**
In the Output Window with the DataClip preview open, click one or two channel labels in
the left column to select them. The "Remove" button at the top becomes the active
action. Click **Remove**.

**Expected:**
- The selected channels disappear from both the Output Window and from the clip body
  visualisation on the timeline.
- The `.data` file on disk is rewritten — opening it in a text editor shows the removed
  channels are no longer present.
- The selection is cleared after the action.

## Step: Undo restores removed channels (memory + disk)

**Action:**
Press `Ctrl+Z` immediately after the previous step.

**Expected:**
- The channels reappear in the Output Window and in the clip body, in their original
  positions in the channel list.
- The `.data` file on disk has the channels back as well — undo wrote the restored set
  back, not just the in-memory state.
- `Ctrl+Y` (Redo) reapplies the deletion and rewrites the file again.

## Step: Remove events inside a time range

**Action:**
In the Output Window, click-drag horizontally on the canvas to select a range (the
selection shows as a faint shaded vertical band). Optionally also pick a channel by
clicking its label. Click **Remove**.

**Expected:**
- Only events whose source time falls inside the selected range are removed; events on
  the same channel outside the range stay.
- If no channel is selected, the range deletion applies to **all** channels.
- The `.data` file on disk reflects the deletion.
- Undo restores every removed event back to its original index; redo deletes again.

## Step: Edits propagate to sibling clips referencing the same file

**Action:**
With at least one Remove command already applied, **Cut** the data clip on the timeline
(playhead inside the clip → context menu → **Cut at time**). Look at both halves.

**Expected:**
- Neither half resurrects the channels you deleted — the cut creates a sibling
  `LoadDataClip` op that reads the same `.data` file via the shared cache.
- The deletion is also still reflected on disk (rewritten by the previous Remove).

## Step: Removed events do not fire during replay

**Action:**
Wire the clip into `SimulateIoData` and play through the clip.

**Expected:**
- The events you removed do not fire — neither the deleted channels nor the events
  inside a deleted time range produce dispatches on the bus.
- Remaining events fire at the same source positions as before the edit.

## Step: Inline DataClip edit pane — toggle reveals the pane below the timeline

**Action:**
Click the **AudioFile** icon button on the timeline toolbar (the one next to the record
button). With **no** DataClip selected, look at the timeline area. Then click a DataClip
in the clip area to select it.

**Expected:**
- With the toggle on and nothing selected, the timeline area looks unchanged — no pane
  is reserved (the dope sheet still gets the full height).
- The moment a DataClip becomes selected, a bottom pane appears with the clip's channel
  list and event markers. Selecting an audio clip (or any non-DataClip) makes the pane
  disappear again.
- Clicking the toolbar button again toggles the pane off regardless of selection.

## Step: Inline pane — channel rows align with the timeline ruler above

**Action:**
With the pane visible, look at where the event ticks sit horizontally vs the timeline's
beat lines / clip body in the row above.

**Expected:**
- Event ticks line up vertically with the same events drawn inside the clip body in the
  row above — same X, same density.
- The channel-row order matches the channel list in the Output Window DataSet view.
- Interval (MIDI note) channels render as horizontal bars from note-on to note-off, not
  as single-tick markers.

## Step: Resize splitter

**Action:**
Hover the thin gap between the dope-sheet area and the inline pane. Drag down to shrink
the pane, drag up to grow it.

**Expected:**
- A horizontal cursor (↕) appears on hover; clicking and dragging up/down resizes the
  pane and the dope sheet area complementarily.
- The pane height clamps so neither the dope sheet nor the pane can be squashed below
  ~80 px.
- Closing and re-opening the pane (toolbar toggle) restores the dragged height.

## Step: Mouse wheel zooms the timeline X from inside the pane

**Action:**
With the pane visible, move the mouse cursor inside the pane and scroll the mouse wheel.

**Expected:**
- The timeline ruler above and the events inside the pane zoom together — they stay in
  sync (the pane's X is the timeline's X).
- The zoom centres around the mouse position; events under the cursor stay roughly
  under the cursor as the scale changes.

## Step: Right-mouse drag pans both axes

**Action:**
Inside the pane, press and hold the right mouse button. Drag in any direction.

**Expected:**
- Vertical drag scrolls the channel list inside the pane.
- Horizontal drag pans the **timeline** — the ruler above scrolls in sync, events in
  the pane follow.
- Releasing the right button stops both pan directions cleanly.

## Step: Scroll resets when the pane (re)appears

**Action:**
Open the pane with a clip that has enough channels to require vertical scrolling. Scroll
down inside the pane. Close the pane (toolbar toggle or close button). Re-open it with
the same clip selected.

**Expected:**
- The pane reopens scrolled back to the top (first channel visible), not at the
  scroll position you left it.
- The same reset happens when you switch from one DataClip to another while the pane is
  open.

## Step: Close button hides the pane (and any curve mode)

**Action:**
With the pane visible, click the `×` icon in the upper-right corner of the pane. Also
test the case where curve editing is active on a dope-sheet row in addition to the clip
pane being open.

**Expected:**
- The pane disappears regardless of which mode (clip-editing OR curve-editing) drove it
  open; the AudioFile toggle on the toolbar visually returns to its off state.
- If curve editing was also active, that's cleared too — the pane stays hidden until
  the user explicitly toggles either gate back on.
- The close icon is not occluded by the vertical scrollbar (when the channel list
  overflows and the scrollbar appears, the icon shifts left to remain clickable).

## Step: Background clicks deselect with the pane open

**Action:**
With the pane visible, click on the empty background of the clip area or the dope sheet
above (not on a clip or keyframe).

**Expected:**
- The DataClip selection clears; the pane disappears (same affordance as without the
  pane).
- Keyframe selection clears too.
- Drag-fence selection inside the dope sheet still works — it adds clips / keyframes
  under the fence as expected.

## Step: Cross-type click switches selection cleanly

**Action:**
Click a DataClip (op TimeClip). Then click an audio clip. Then click the DataClip
again.

**Expected:**
- Each click leaves **only** the clicked clip selected — never both types at once.
- The Parameter window shows the inspector for the currently-selected type each step;
  the inline pane appears / disappears in step with whether the selected clip is a
  DataClip.
