---
id: graph-connection-routes
title: Connection Route Points
scope: graph-window
tags: [user]
added: 2026-11-25
added-in-version: 4.4
prerequisites:
  - A project you can edit is open in the Graph Window.
  - A graph with one operator whose output feeds two operators of the same type (e.g. one [Time] into two [Add]), placed so the lines are long and not snapped.
related-help:
  - ../.help/docs/using/RoutingConnections.md
---

Checks adding, moving, sharing and removing route points on connection lines, and that routes survive moving,
copying, saving and undo.

## Step: Adding a route point

1. Hold `Alt` and hover the line into the first [Add].
2. **Alt+Click** the line and drag before releasing.

**Expected:** While hovering with `Alt` held, the filled hover dot on the line is 1.5× larger than without it; the
rings at the line's ends keep their size. After the
click a filled point follows the mouse and the line bends through it. No selection fence appears and the selection
doesn't change.

## Step: Only the clicked line changes

1. Look at the line into the second [Add].

**Expected:** It still follows its automatic path. (Both [Add] operators are instances of the same type; their
lines must not share routes.)

## Step: Moving and cancelling

1. Drag the route point to a new place, release.
2. Drag it again and press `Esc` before releasing.

3. Drag the point once more and press `Alt` during the drag.

**Expected:** The first drag moves the point. The cancelled drag puts it back where it was. Holding `Alt` during a
drag doesn't pan the graph.

## Step: Vertical snapping

1. Drag a route point slowly up and down past the height of its line's output, its input and another point of the
   same line.
2. Repeat with `Ctrl` held.
3. Drag it past the height of a point on a line from a different operator.

**Expected:** The point catches at each of those heights and a horizontal snap line flashes. With `Ctrl` it moves
freely. Points of other lines don't attract it.

## Step: Sharing a trunk

1. Alt+Click the line into the second [Add] to add a point.
2. Drag that point onto the first route point.

**Expected:** A large ring appears around the first point while hovering it. After release both lines run through one
point and overlap from the output up to it. Dragging the shared point moves both lines.

## Step: Adding a point where lines overlap

1. Make sure the two lines from [Time] run on top of each other for a stretch (e.g. right after the output).
2. **Alt+Click** on that stretch.

**Expected:** One point is added that both lines share; dragging it moves both. One undo removes it from both.

## Step: Lines from different outputs don't merge

1. Add a route point to a line that comes from a different output.
2. Drag it onto the shared point.

**Expected:** No large ring appears and the points don't merge.

## Step: Selecting route points

1. Click a route point, then **Shift+Click** another one.
2. Draw a selection fence around [Time], both [Add] operators and their route points.
3. Select the first [Add] and press **Shift+G**.

**Expected:** Selected route points show a white ring. The fence selects operators and route points together.
**Shift+G** selects the upstream operators and the route points of the lines between them.

## Step: Moving operators

1. Drag only the first [Add] (nothing else selected).
2. Fence-select [Time], both [Add] operators and their route points, and drag the group. Try it near other operators
   so it snaps.
3. Undo.

**Expected:** Dragging one operator stretches its line to the route points, which stay in place. Dragging the group
moves the selected route points exactly with the operators, also when the group snaps. One undo puts operators and
route points back.

## Step: Dragging a section

1. Put the operators and their route points into a section and drag the section by its header.

**Expected:** The route points inside the section move with it.

## Step: Shaking off and deleting

1. Drag a route point and shake the mouse quickly.
2. Select another route point and press `Delete`.

**Expected:** The shaken point is removed and the drag ends. `Delete` removes the selected point. Both can be undone.

## Step: Undo and redo

1. Press `Ctrl+Z` repeatedly, then `Ctrl+Shift+Z`.

**Expected:** Each route change (add, move, merge, group move) is undone and redone in order.

## Step: Copy, paste and duplicate

1. Select [Time] and both [Add] operators, copy and paste them.
2. Duplicate the selection with `Ctrl+D`.

**Expected:** The copies have the same routes, moved by the same offset as the operators. Moving a route point of a
copy doesn't move the original.

## Step: Removing a route point

1. Hold `Alt`, press on the shared route point, move the mouse a bit and release.
2. **Alt+Click** the shared route point without moving the mouse.

**Expected:** The first try moves the point and keeps it. The second removes it from both lines; lines without route
points return to their automatic path.

## Step: Panning still works

1. `Alt`+drag starting on an empty spot of the graph.

**Expected:** The graph pans.

## Step: Save and reload

1. Add a route point, save the project, close and reopen it.

**Expected:** The route is still there. Routes of lines you deleted before saving are gone.
