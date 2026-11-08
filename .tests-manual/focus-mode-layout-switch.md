---
id: focus-mode-layout-switch
title: Focus Mode and Layout Switching
scope: layouts
tags: [edge, user]
added: 2026-07-03
added-in-version: 4.3
prerequisites:
  - A project with a visible image output is open (e.g. an example with a rendered scene).
---

Verifies that leaving focus mode — both by toggling it off and by jumping
directly to another layout — restores the normal editor view without a stale
background image.

## Step: Entering focus mode

**Action:**
With an operator producing an image pinned or selected in the Output Window, press `F12`.

**Expected:**
- The window layout collapses to the focus layout.
- The image fills the background behind the graph.
- Main menu, toolbar, and timeline are hidden.

## Step: Leaving focus mode with F12

**Action:**
Press `F12` again.

**Expected:**
- The previous layout and UI elements (menu, toolbar, timeline) are restored.
- The background image behind the graph is gone.
- The Output Window shows the previously displayed operator again.

## Step: Switching directly to another layout from focus mode

**Action:**
Press `F12` to enter focus mode again, then press `F2` (or pick a layout from
the `Load layout` menu) while still in focus mode.

**Expected:**
- The chosen layout is applied and menu, toolbar, and timeline reappear.
- The background image behind the graph is gone.
- The Output Window shows the previously displayed operator again.

## Step: Limiting the fill resolution

**Action:**
In *Settings → Interface → Output*, set **Max Fill Resolution** to `720p`, then press `F12`.
Open the Output Window again afterwards with its resolution set to "Fill", then to `1080p`.

**Expected:**
- In focus mode the image still fills the background, upscaled from a render whose short side is 720 pixels.
- The Output Window caption on "Fill" shows a size with a short side of 720.
- With `1080p` selected the Output Window renders 1920x1080 regardless of the limit.
- Setting the limit back to `Unlimited` restores the full window resolution.

## Step: Creating an operator in focus mode

**Action:**
In focus mode, press `Tab` in the graph, insert any operator, then press the arrow keys and `S`.

**Expected:**
- No parameter popup opens below the new operator.
- The arrow keys move the selection and `S` opens the parameter popover.

## Step: Opening the parameter popup by clicking the selected operator

**Action:**
In focus mode, click an operator that is not selected. Then click it once more. Then double-click an operator that has children.

**Expected:**
- The first click only selects the operator; no popup opens.
- Clicking the selected operator again opens its parameter popup after a short moment.
- The double-click dives into the operator without the popup showing up, inside or after returning.
- Middle-clicking an operator opens the popup right away.
