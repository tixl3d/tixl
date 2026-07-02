---
id: help-window
title: Help Window
scope: help
tags: [user, essential]
added: 2026-06-24
added-in-version: 4.2
prerequisites:
  - A project is open with a few library operators on the canvas (include one that is discussed in the
    meet-up videos, e.g. `[SphereSDF]`, `[FastBlur]`, or `[RaymarchField]`).
related-help:
  - ../.agentic/Plans/Plan_HelpWindow.md
---

The Help window is your built-in guide while you work. It follows whatever operator you point at or
select and shows what it does. You can pin a topic so it stays put, step back and forth through topics
you've looked at, jump to videos where an operator is discussed, and switch to the Learn tab for the
latest release notes.

## Step: Opening the Help window

**Action:**
Open the `Windows` menu in the main menu bar and click `Help`.

**Expected:**
- A window titled `Help` appears (dock it next to the [ui:ParameterWindow|Parameter window] if it floats).
- The header shows two tabs, `Help` and `Learn`, with `Help` active.
- With nothing hovered or selected, the body reads "Hover or select an operator to see its description."

## Step: Following the hovered operator

**Action:**
With the Help window visible, move the mouse over different operators in the [ui:Graph|Graph window] without clicking.

**Expected:**
- The Help body updates instantly as the pointer crosses each operator — no hover delay.
- It shows that operator's name and description (the same content you'd see in the Parameter window's help view).

## Step: Following the selection

**Action:**
Click an operator in the Graph window to select it, then move the mouse away from any operator (over empty canvas).

**Expected:**
- The Help body stays on the selected operator once the pointer is no longer over a different one.
- Selecting a different operator switches the Help body to it and scrolls back to the top.

## Step: Following the Symbol Library

**Action:**
Open the Symbol Library window and move the mouse over different symbols in the tree (or type in its search
field and hover a result), without clicking.

**Expected:**
- The Help body updates to each hovered symbol's description, the same as hovering it in the graph.
- This works whether or not that operator is present on the current canvas.

## Step: Discussed in meet-ups list

**Action:**
Hover or select an operator that is covered in the videos (e.g. `[RaymarchField]` or `[SelectPointsWidthSDF]` or ).

**Expected:**
- Below the description a video-resource section lists up to two rows, each a ▶ play icon, the video type
  in bold, and a relevancy annotation, e.g. **Tutorial** `(3min · In-depth · Experiment)` — no age in the row.
- The ALL-CAPS heading adapts to the kinds present — "RELATED TUTORIALS" when all tutorials, "DISCUSSED IN
  MEET-UPS" when all meet-ups, "WATCH & LEARN" when mixed.
- Hovering a row gives it white text and a soft rounded highlight (same as Asset Library folder rows). Only
  one row highlights at a time.
- If there are more than two, a "Show all N" row expands the full list; "Show less" collapses it again.
- Older clips show a faint right-aligned "predates current UI" note.
- The section stays docked at the bottom of the panel (above a faint divider) while the description and
  parameter list scroll independently above it. It aligns with the description text, not the left edge.

## Step: Resource tooltip and opening a video

**Action:**
Hover one of the meet-up resource rows, then click it.

**Expected:**
- A tooltip appears immediately: a thumbnail on the left (with a play badge and a `Tutorial 5:23` type +
  full-length label), and on the right a caps header like `5MIN ON YOUTUBE / SEP 2024`, the video title in
  bold white (clamped with `...` if long), and the "what you'll learn" note.
- (If a thumbnail image isn't available, the tooltip simply shows the text only.)
- Clicking the row opens the video in your browser, jumped to the moment that operator is discussed.

## Step: Pinning a topic

**Action:**
With an operator shown, click the outlined pin icon in the Help window header. Then move the mouse over other
operators and select different ones in the graph.

**Expected:**
- The pin icon switches to a filled/active state.
- The Help body stays on the pinned operator and no longer follows the hover or selection.

## Step: History back and forward

**Action:**
While pinned on one operator, pin a second operator (hovering isn't enough while pinned — click the pin
again to unpin, hover or select the second operator, then pin it). Now use the `‹` and `›` arrow buttons
in the header.

**Expected:**
- `‹` returns to the previously pinned operator; `›` steps forward again.
- Landing on an earlier topic keeps it pinned (it doesn't jump back to following your selection).
- The arrows are dimmed and do nothing once you reach the start or end of your history.

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
- The body shows the current version's release notes, nicely formatted with clickable operator links, or
  "No release notes for this version yet." if this build doesn't include any.
- Switching back to `Help` restores the operator doc view.
