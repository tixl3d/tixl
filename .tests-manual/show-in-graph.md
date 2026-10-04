---
id: show-in-graph
title: Show in Graph (Shortcut and Project Open)
scope: output-window
tags: [graph, output, shortcut]
added: 2026-09-24
added-in-version: 4.3
prerequisites:
  - A project with at least two operators wired together is open, and the Graph Window and an Output View are visible.
related-help:
  - ../.help/docs/using/KeyboardShortcuts.md
---

Covers **Show in Graph** from the Output View's breadcrumb menu: its `Shift+P`
shortcut, the entry in Settings, and the automatic call when a project opens.

## Step: The menu entry shows its shortcut

**Action:**
In the Output View toolbar, click the breadcrumb combo box next to the pin icon.

**Expected:**
- The menu lists **Show in Graph** with `Shift+P` right-aligned on the same row.

## Step: Shift+P frames the pinned operator

**Action:**
Select an operator in the graph that is *not* currently visible (pan away from it first, or
zoom out and park the view elsewhere). Pin it to the Output View with `P`. Pan the graph away
from it again. Now press `Shift+P`.

**Expected:**
- The graph selects that operator and centres it in view.
- The operator was already the pinned one — pressing the shortcut does not change what the
  Output View shows.

## Step: With nothing pinned, it follows the graph selection

**Action:**
Unpin the Output View (click the filled pin icon). Select an operator, pan the graph away from
it, then press `Shift+P`.

**Expected:**
- The view comes back to the selected operator.

## Step: Opening a project frames what the Output View shows

**Action:**
Open a project, pin an operator to the Output View, and save. Pan the graph somewhere else so
the pinned operator is off-screen. Close and reopen the project.

**Expected:**
- Once the project has loaded, the graph shows the pinned operator, selected and framed.
- The Output View still shows that same operator (the pin survived the restart).

## Step: Opening a project without a pin keeps the saved view

**Action:**
Open a project whose Output View is *not* pinned. Pan and zoom the graph somewhere specific,
save, and reopen the project.

**Expected:**
- The graph returns to that saved viewport; nothing is re-framed, because there is no output
  operator to show.

## Step: The shortcut is listed and rebindable

**Action:**
Open **Settings** and switch to the **Keyboard Shortcuts** category. Find `Show In Graph`.

**Expected:**
- The row shows `Shift+P`.
- Clicking the row opens the edit popup; assigning a different combination works, and the new
  shortcut triggers the action. `Shift+P` is reported as free — it does not conflict with
  `P` (Pin to Output Window), `Ctrl+P` (Display Image as Background) or `Ctrl+Shift+P`
  (Clear Background Image).
