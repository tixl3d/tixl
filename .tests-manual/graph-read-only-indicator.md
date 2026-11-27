---
id: graph-read-only-indicator
title: Read-Only Graph Indicator
scope: graph-window
tags: [user]
added: 2026-11-27
added-in-version: 4.4
prerequisites:
  - A project you can edit is open in the Graph Window.
  - The graph contains a library operator with a subgraph, e.g. [Blob].
---

Checks that the graph of a read-only operator is clearly marked, and that the marking follows the composition and
the theme.

## Step: Entering a read-only operator

**Action:**
Note the graph background. Double-click the [Blob] to open its graph.

**Expected:**
- The background turns noticeably darker.
- A large, faint **READ ONLY** label sits centred behind the operators and connections, and the grid still draws on top of it.
- Panning and zooming the graph doesn't move or scale the label.
- Resizing the Graph Window keeps the label centred; a narrow window shrinks it.

## Step: Leaving it again

**Action:**
Click your project in the breadcrumbs to go back up.

**Expected:**
- The background returns to its usual color and the label is gone.

## Step: Themes

**Action:**
Open **Settings → Theme**, switch to **Bright**, then **DarkContrast**, and open the [Blob] each time.

**Expected:**
- In every theme the read-only background differs visibly from the usual one, and the label stays faint but readable.
- The Theme editor lists **Canvas Background (Read Only)** next to **Canvas / Editor Background**, and changing it updates the read-only graph immediately.
