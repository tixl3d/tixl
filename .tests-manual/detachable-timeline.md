---
id: detachable-timeline
title: Detachable Timeline Window
scope: timeline
tags: [layout]
added: 2026-11-04
added-in-version: 4.4
prerequisites:
  - A project with a few animated parameters is open in the Graph Window.
  - The Project Settings use Animation (timeline) playback, not beat tapping.
related-help:
  - ../.help/references/topics/ui/Timeline.md
---

Covers attaching and detaching the timeline, the graph window's tool icons in the upper right corner, and
playback shortcuts with several graph windows.

## Step: Graph tool icons in the upper right corner

**Action:**
Look at the upper right corner of the Graph Window, then hover over each icon.

**Expected:**
- Two icons are shown: hover preview mode and Project Settings.
- Clicking the hover preview icon cycles through its three modes; the tooltip changes with it.
- Clicking the Settings icon toggles Project Settings.
- With View > Graph Minimap enabled, the minimap sits below the icons instead of covering them.

## Step: Attached timeline is unchanged

**Action:**
With the timeline attached (View > Detach Timeline unchecked), select an animated operator and look at the
bottom of the Graph Window.

**Expected:**
- The timeline shows below the graph, with the splitter and the fold toggle, as before.
- The transport row at the bottom no longer contains the Settings and hover preview icons.
- The IO indicator sits right of the idle motion indicator and flashes on MIDI or OSC input; clicking it
  toggles the IO Events window.
- Audio mute, keyframe pinning and Record are still in the transport row.

## Step: Detaching the timeline

**Action:**
Choose View > Detach Timeline.

**Expected:**
- A window titled "Timeline" appears, with a header strip at its top and the dope sheet below it.
- The header has the same shade as the time ruler below it, separated by a thin dark line.
- The idle motion indicator, the IO indicator and keyframe pinning sit on the left; time, format, transport and Record are
  centered; the Dope/Curve mode button and mute sit on the right.
- The Graph Window no longer shows a timeline or a bottom toolbar; the upper right icons remain.
- Playing, scrubbing and editing keyframes in the Timeline window work as in the attached timeline.

## Step: Header groups on a narrow window

**Action:**
Make the Timeline window narrow by docking it into a small slot or resizing it.

**Expected:**
- The centered group moves right instead of overlapping the left group.
- The right group never overlaps the centered group; it is clipped at the window edge instead.

## Step: Docking the timeline window

**Action:**
Drag the Timeline window's tab into a dock slot next to the Output View, then resize the split.

**Expected:**
- The timeline fills its dock slot and follows the slot's size.
- Keyboard shortcuts in the timeline (for example `C` to insert a keyframe, `Del` on a selected clip) work
  after clicking into the Timeline window.

## Step: Timeline follows the focused graph window

**Action:**
Open a second Graph Window from the Window menu and navigate it into a different composition with other
animated operators. Click into the first Graph Window, then into the second.

**Expected:**
- The Timeline window always shows the timeline of the graph window that was clicked last.
- Each graph window keeps its own timeline zoom, scroll and selection when switching back and forth.
- Clicking into the Timeline window or the Parameters window does not switch which timeline is shown.

## Step: Playback shortcuts with two graph windows

**Action:**
With both graph windows showing a project, press `Space`, then press it again.

**Expected:**
- The first press starts playback, the second stops it.
- `,` and `.` jump exactly one keyframe per press.

## Step: Toggle All UI and Live mode

**Action:**
With the timeline detached, press `Shift+Esc` twice, then open the View menu. Afterwards open Project
Settings and switch playback to Live (beat tapping).

**Expected:**
- `Shift+Esc` hides and shows the graph's UI elements, but the Timeline window keeps showing the timeline.
- In the View menu, the Timeline entry is disabled while the timeline is detached.
- In Live mode the centered group shows the BPM field and the Sync button instead of the transport; the
  Dope/Curve mode button is hidden.

## Step: No project open

**Action:**
Switch the focused graph window back to the Hub.

**Expected:**
- The Timeline window shows "Open a project to see its timeline." and no errors appear in the console.

## Step: Attaching again and layouts

**Action:**
Save the current layout with `Ctrl+F2`. Close the Timeline window, then load the layout again with `F2`.

**Expected:**
- After closing the window, the timeline is attached below the graph again.
- Loading the saved layout detaches the timeline again and restores it in its dock slot.
