---
id: dataclip-editing
title: DataClip — inspection and editing
added: 2026-05-31
added-in-version: 4.2
scope: timeline
tags: [user, essential]
prerequisites:
  - A project is open with at least one recorded data clip on the timeline (run `recording-io-data` first if you don't have one).
  - The composition's Syncing mode is **Timeline**.
related-help:
  - ../.help/docs/using/Recording.md
---

Once you've recorded a take of control data, you can look inside the data clip and tidy it
up — preview what was captured, see notes as bars on the [ui:Timeline|timeline], and remove channels or
stretches of events you don't want, with full undo. This set covers viewing and editing a
recorded data clip, not the recording itself.

## Step: Output preview works standalone (no SimulateIoData)

**Action:**
Select a [LoadDataClip] op on the graph that has **nothing connected** to its `Clip`
output. Open the [ui:OutputWindow|Output Window] and pin the `Clip` output.

**Expected:**
- The preview shows the recorded channels and events right away — you don't need to wire
  up a [SimulateIoData] op first.
- A time scale runs across the bottom, labelled in seconds.
- A playhead line sits where the timeline currently is; pausing the timeline freezes it,
  and scrubbing the timeline moves it across the data.

## Step: Clip-boundary overlay matches the TimeClip's SourceRange

**Action:**
Still in the Output Window with the DataClip preview open, look at the canvas
background. Note where the orange-tinted shading begins and ends.

**Expected:**
- The stretch of the recording the clip actually uses is left clear; everything outside
  it is dimmed with a faint orange tint.
- Two thin vertical lines mark the start and end of the used slice.
- Trimming the clip on the timeline updates this live: trim the right edge inward and the
  right line moves to the new edge while the dimmed area grows to fill the rest.

## Step: Interval events render as bars in the clip body

**Action:**
Look at the data clip on the timeline (not the Output Window). Make sure the recording
includes some held MIDI notes.

**Expected:**
- Held notes show as **horizontal bars** running from where each note started to where it
  ended, inside the clip body — not as a single thin tick at the note's start.
- Knob (CC) and OSC channels in the same clip still show as their line of ticks.
- A note that was still held when the recording stopped stretches all the way to the right
  edge of the clip rather than disappearing.

## Step: Remove selected channels via the output view

**Action:**
In the Output Window with the DataClip preview open, click one or two channel labels in
the left column to select them. The "Remove" button at the top becomes the active
action. Click **Remove**.

**Expected:**
- The selected channels disappear from both the Output Window preview and the clip body
  on the timeline.
- The change is permanent — closing and reopening the project shows the channels are still
  gone.
- The selection is cleared after the action.

## Step: Undo restores removed channels (memory + disk)

**Action:**
Press `Ctrl+Z` immediately after the previous step.

**Expected:**
- The channels reappear in the Output Window and in the clip body, back in their original
  order in the list.
- The restore is permanent too — closing and reopening the project shows the channels are
  back, not just on screen for now.
- `Ctrl+Y` (Redo) removes them again, and that also sticks.

## Step: Remove events inside a time range

**Action:**
In the Output Window, click-drag horizontally on the canvas to select a range (the
selection shows as a faint shaded vertical band). Optionally also pick a channel by
clicking its label. Click **Remove**.

**Expected:**
- Only the events inside the selected stretch of time are removed; events on the same
  channel outside that stretch stay.
- If no channel is selected, the removal applies to **all** channels.
- The change is permanent (it survives closing and reopening the project).
- Undo restores every removed event; redo deletes them again.

## Step: Edits propagate to sibling clips referencing the same file

**Action:**
With at least one Remove command already applied, **Cut** the data clip on the timeline
(playhead inside the clip → context menu → **Cut at Time**). Look at both halves.

**Expected:**
- Neither half brings back the channels you deleted — the new clip from the cut reads the
  same recording, with your edits already applied.
- The deletion is still permanent (it survives closing and reopening the project).

## Step: Removed events do not fire during replay

**Action:**
Wire the clip into [SimulateIoData] and play through the clip.

**Expected:**
- The events you removed don't replay — neither the deleted channels nor the events inside
  a deleted stretch of time drive anything.
- The remaining events still replay at the same moments as before the edit.

## Step: Inline DataClip edit pane — context menu reveals the pane below the timeline

**Action:**
Right-click on the empty clip area with **no** data clip selected and look at the context
menu. Then select a data clip in the clip area, right-click it, and choose
**Show Clip Data**.

**Expected:**
- With no data clip selected, the context menu has no **Show Clip Data** entry.
- With a data clip selected, the entry appears; choosing it opens a pane below the timeline
  with the clip's channel list and event markers. Selecting an audio clip (or anything
  that isn't a data clip) makes the pane disappear again.
- Re-opening the context menu while the pane is visible shows a checkmark on
  **Show Clip Data**; choosing it again hides the pane.

## Step: Inline pane — channel rows align with the timeline ruler above

**Action:**
With the pane visible, look at where the event ticks sit horizontally vs the timeline's
beat lines / clip body in the row above.

**Expected:**
- Event ticks line up directly under the same events drawn inside the clip body in the
  row above — same horizontal position, same density.
- The channel rows are in the same order as the channel list in the Output Window preview.
- Held-note channels show as horizontal bars from note start to note end, not as single
  ticks.

## Step: Resize splitter

**Action:**
Hover the thin gap between the dope-sheet area and the inline pane. Drag down to shrink
the pane, drag up to grow it.

**Expected:**
- A resize cursor (↕) appears on hover; dragging up/down resizes the pane and the dope
  sheet above it, trading height between them.
- The drag stops before either the dope sheet or the pane gets squashed too small.
- Closing and re-opening the pane (close button, then context menu **Show Clip Data**)
  restores the dragged height.

## Step: Mouse wheel zooms the timeline X from inside the pane

**Action:**
With the pane visible, move the mouse cursor inside the pane and scroll the mouse wheel.

**Expected:**
- The timeline above and the events inside the pane zoom together and stay lined up.
- The zoom centres on the mouse; events under the cursor stay roughly under the cursor as
  the scale changes.

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
down inside the pane. Close the pane (close button or context menu **Show Clip Data**).
Re-open it with the same clip selected.

**Expected:**
- The pane reopens scrolled back to the top (first channel visible), not at the
  scroll position you left it.
- The same reset happens when you switch from one data clip to another while the pane is
  open.

## Step: Close button hides the pane (and any curve mode)

**Action:**
With the pane visible, click the `×` icon in the upper-right corner of the pane. Also
test the case where curve editing is active on a dope-sheet row in addition to the clip
pane being open.

**Expected:**
- The pane closes whether it was opened for clip editing or for curve editing, and stays
  closed: selecting another data clip does **not** bring it back until you choose
  **Show Clip Data** from the context menu again.
- If curve editing was also active, it's turned off too — the pane stays hidden until you
  explicitly re-open it.
- The close icon stays clickable even when the channel list is long enough to show a
  scrollbar — it shifts left so the scrollbar doesn't cover it.

## Step: Background clicks deselect with the pane open

**Action:**
With the pane visible, click on the empty background of the clip area or the dope sheet
above (not on a clip or keyframe).

**Expected:**
- The data clip is deselected and the pane disappears (the same as it would without the
  pane open).
- Any selected keyframes are deselected too.
- Drag-selecting a rectangle inside the dope sheet still works — it selects the clips and
  keyframes under it as expected.

## Step: Cross-type click switches selection cleanly

**Action:**
Click a data clip. Then click an audio clip. Then click the data clip again.

**Expected:**
- Each click leaves **only** the clicked clip selected — never both types at once.
- The Parameter window shows the inspector for whichever type is selected each step; the
  inline pane appears or disappears depending on whether the selected clip is a data clip.
