---
id: help-window
title: Help Window
scope: help
tags: [essential]
added: 2026-06-24
added-in-version: 4.2
prerequisites:
  - A project is open with a few library operators on the canvas (include one that is discussed in the
    meet-up videos, e.g. `[SphereSDF]`, `[FastBlur]`, or `[RaymarchField]`).
related-help:
  - ../.agentic/Plans/Plan_HelpWindow.md
---

Covers the dockable Help window: following the hovered/selected operator, the pin + history flow, the
ranked "Discussed in meet-ups" resource list, and the Learn tab.

## Step: Opening the Help window

**Action:**
Open the `Windows` menu in the main menu bar and click `Help`.

**Expected:**
- A window titled `Help` appears (dock it next to the Parameter window if it floats).
- The header shows two tabs, `Help` and `Learn`, with `Help` active.
- With nothing hovered or selected, the body reads "Hover or select an operator to see its description."

## Step: Following the hovered operator

**Action:**
With the Help window visible, move the mouse over different operators in the Graph window without clicking.

**Expected:**
- The Help body updates instantly as the pointer crosses each operator — no hover delay.
- It shows that operator's name, namespace, and description (the same content as the Parameter window's help view).

## Step: Following the selection

**Action:**
Click an operator in the Graph window to select it, then move the mouse away from any operator (over empty canvas).

**Expected:**
- The Help body stays on the selected operator once the pointer is no longer over a different one.
- Selecting a different operator switches the Help body to it and scrolls back to the top.

## Step: Discussed in meet-ups list

**Action:**
Hover or select an operator that is covered in the videos (e.g. `[SphereSDF]` or `[RaymarchField]`).

**Expected:**
- Below the description a video-resource section lists up to two rows, each like
  `▶ Tutorial (3min, 1 year ago)`. The heading adapts to the kinds present — "Related tutorials" when
  they're all tutorials, "Discussed in meet-ups" when all meet-ups, "Watch & learn" when mixed.
- If there are more than two, a "Show all N" row expands the full list; "Show less" collapses it again.
- Older clips show a faint right-aligned "predates current UI" note.

## Step: Resource tooltip and opening a video

**Action:**
Hover one of the meet-up resource rows, then click it.

**Expected:**
- A tooltip appears immediately with a header line of relevancy axes (e.g. `1 MIN · IN-DEPTH · EXPERIMENT · YOUTUBE`),
  the video title and month/year, the "what you'll learn" note, and an "Open at m:ss in your browser" hint.
- Clicking the row opens the video in the default browser at the segment's start time.

## Step: Pinning a topic

**Action:**
With an operator shown, click the outlined pin icon in the Help window header. Then move the mouse over other
operators and select different ones in the graph.

**Expected:**
- The pin icon switches to a filled/active state.
- The Help body stays on the pinned operator and no longer follows the hover or selection.

## Step: History back and forward

**Action:**
While pinned on one operator, pin a second operator (hover it is not enough — the panel is detached; instead
click the pin again to unpin, hover/select the second operator, then pin it). Use the `‹` and `›` chevron
buttons in the header.

**Expected:**
- `‹` returns to the previously pinned operator; `›` steps forward again.
- Landing on a history entry keeps the panel pinned (it does not jump back to following the selection).
- The chevrons are dimmed and inert at the ends of the history.

## Step: Unpinning

**Action:**
Click the filled pin icon again, or the `✕` button shown while pinned.

**Expected:**
- The pin returns to its outlined state.
- The Help body resumes following the hovered/selected operator.

## Step: Learn tab

**Action:**
Click the `Learn` tab in the header.

**Expected:**
- The body shows the current version's release notes (rendered markdown with operator links), or
  "No release notes for this version yet." if none ship with this build.
- Switching back to `Help` restores the operator doc view.
