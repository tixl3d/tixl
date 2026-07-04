---
id: graph-connection-splitting
title: Splitting Connections on the Graph
added: 2026-06-10
added-in-version: 4.3
scope: graph-window
tags: [user, essential]
prerequisites:
  - A project you can edit is open in the Graph Window.
  - A graph you can edit (e.g. a new empty operator) with at least one input
    node, a few operators, and an output node.
related-help:
  - ../.help/using/graph-window.md
---

Checks dropping an operator into the middle of an existing cable — both by
clicking the cable (which opens a search) and by dragging an operator onto a
snapped cable. This should work just as well when the cable starts at one of the
graph's **Input** nodes (on the left) or ends at its **Output** node (on the
right): the new operator slots in, and the cable never simply vanishes.

## Step: Click-split a connection between two operators

**Action:**
Hover over the middle of a cable between two ordinary operators until the insert
indicator appears, then click. Pick a fitting operator (one whose type matches)
from the search.

**Expected:**
- The new operator drops into the cable: source → new operator → target.
- `Ctrl+Z` restores the original direct cable and removes the operator.

## Step: Click-split a connection coming from an Input node

**Action:**
Hover over and click the middle of a cable that starts at one of the graph's
input nodes (the nodes on the left). Pick a fitting operator from the search.

**Expected:**
- The new operator drops in: input node → new operator → the previous target.
- The cable is replaced, not lost — the input node stays connected through the
  new operator.
- `Ctrl+Z` restores the direct cable.

## Step: Click-split a connection going to the Output node

**Action:**
Hover over and click the middle of the cable that feeds the graph's output node.
Pick a fitting operator from the search.

**Expected:**
- The new operator drops in: the previous source → new operator → output node.
- `Ctrl+Z` restores the direct cable.

## Step: Drag-split with an existing operator

**Action:**
Snap two operators together (vertically or horizontally), then drag a third
operator whose input and output types match onto the snapped cable between them
until the insert highlight appears, and drop it.

**Expected:**
- The dragged operator slots into the cable and the operators after it shift to
  make room.
- `Ctrl+Z` restores the previous layout and cable.

## Step: Drag-split must not create a cycle

**Action:**
Snap two operators **A** → **B** together. Add a third operator **C** (with
matching types) and wire a long cable from **B**'s output into one of **C**'s
inputs. Now drag **C** onto the snapped cable between **A** and **B** and try
to drop it there.

**Expected:**
- The insert is refused (the console logs that the connection would create a
  cycle) — **C** is not spliced in.
- The graph stays intact and responsive; no crash or freeze.

## Step: Drag-split a snapped connection from an Input or to the Output node

**Action:**
Snap an input node directly to a following operator (and, separately, an
operator directly to the output node). Drag another operator whose type matches
onto the snapped cable and drop it.

**Expected:**
- The operator slots in and the chain stays complete — the input or output node
  is still connected, now through the new operator.
- `Ctrl+Z` restores the direct snapped cable.
