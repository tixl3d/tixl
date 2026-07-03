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

The Help window is your built-in guide while you work. It shows the documentation of the operator or
UI topic you last selected or clicked, and — while the hover toggle is on — instantly previews whatever
you point at. Every context change lands on a back/forward history, you can jump to videos where an
operator is discussed, and the Learn tab shows the latest release notes.

## Step: Opening the Help window

**Action:**
Open the `Windows` menu in the main menu bar and click `Help`.

**Expected:**
- A window titled `Help` appears (dock it next to the [ui:ParameterWindow|Parameter window] if it floats).
- The header shows two tabs, `Help` and `Learn`, with `Help` active.
- The right side of the header shows `‹` `›` history arrows and a hover-preview toggle icon (active by default).
- With nothing hovered or selected, the body reads "Hover or select an operator to see its description."

## Step: Hover preview

**Action:**
With the Help window visible, move the mouse over different operators in the [ui:Graph|Graph window] without clicking.

**Expected:**
- The Help body updates instantly as the pointer crosses each operator — no hover delay.
- It shows that operator's name, description, and parameter details.
- When the pointer leaves all operators, the body falls back to the last selected topic (or the empty message).

## Step: Hover toggle off

**Action:**
Click the hover-preview icon in the Help window header to switch it off, then hover operators in the graph
and symbols in the Symbol Library.

**Expected:**
- The Help body no longer follows the hover; it stays on the current topic.
- The Symbol Library shows its own description tooltip again (it is omitted while the toggle is on).
- The setting survives an editor restart. Switch it back on for the following steps.

## Step: Following the selection

**Action:**
Click an operator in the Graph window to select it, then move the mouse away from any operator (over empty canvas).

**Expected:**
- The Help body stays on the selected operator once the pointer is no longer over a different one.
- Selecting a different operator switches the Help body to it and scrolls back to the top.

## Step: Symbol Library hover and click

**Action:**
Open the Symbol Library window and move the mouse over different symbols in the tree without clicking.
Then click one.

**Expected:**
- The Help body previews each hovered symbol's description; no separate description tooltip appears while
  the hover toggle is on.
- Clicking a symbol makes it the Help window's current topic (it stays after the mouse moves away).
- This works whether or not that operator is present on the current canvas.

## Step: Symbol thumbnail

**Action:**
Hover or select a library operator that has a thumbnail image (most `Lib` operators with visual output).

**Expected:**
- The thumbnail is shown at the top of the Help body, above the operator's name and description.
- Operators without a thumbnail simply start with the name — no gap or placeholder.

## Step: UI-topic links

**Action:**
Show a doc that contains a `[ui:...]` link (e.g. hover the `?` documentation icon of the Guided Feature
Tests window, or a release note in the Learn tab with a blue UI-topic link). Hover the link, then click it.

**Expected:**
- Hovering the link previews the topic's doc in the Help window (no tooltip while the hover toggle is on).
- Clicking shows the topic in the Help window — title, a small "UI topic" label, and the doc text — and
  opens/focuses the window if it was closed.

## Step: Documentation icons

**Action:**
Hover the `?` documentation icon in the header of a window that has one (e.g. Guided Feature Tests), then click it.

**Expected:**
- Hovering previews the doc in the Help window (with the hover toggle off you get the tooltip instead).
- Clicking shows the doc in the Help window and focuses it. Icons whose topic has no embedded doc still
  open the wiki page in the browser instead.

## Step: Discussed in meet-ups list

**Action:**
Hover or select an operator that is covered in the videos (e.g. `[RaymarchField]` or `[SelectPointsWidthSDF]`).

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

## Step: History back and forward

**Action:**
Select three different operators in the graph one after another, then use the `‹` and `›` arrow buttons
in the header.

**Expected:**
- `‹` steps back through the previously shown topics; `›` steps forward again.
- Hovering other operators while browsing the history only previews them — releasing the hover returns to
  the history entry you are on.
- Selecting or clicking a new topic while somewhere back in the history jumps out of it: the new topic is
  shown and becomes the newest history entry.
- Re-selecting a topic that is already in the history doesn't create a duplicate entry.
- The arrows are dimmed and do nothing once you reach the start or end of your history.

## Step: Learn tab

**Action:**
Click the `Learn` tab in the header.

**Expected:**
- The body shows the current version's release notes, nicely formatted with clickable operator links, or
  "No release notes for this version yet." if this build doesn't include any.
- The history arrows and hover toggle are hidden; hovering operators does not change the Learn content.
- Switching back to `Help` restores the operator doc view.
