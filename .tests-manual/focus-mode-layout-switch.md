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
