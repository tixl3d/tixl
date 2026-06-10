---
id: graph-connection-splitting
title: Splitting Connections on the Graph
added: 2026-06-10
added-in-version: 4.3
scope: graph-window
tags: [essential]
prerequisites:
  - A writable project is open in the Graph Window.
  - A composition you can edit (e.g. a new empty operator) with at least one
    input node, a few operators, and an output node.
related-help:
  - ../.help/using/graph-window.md
---

Covers inserting an operator into an existing connection, both by clicking the
connection (placeholder search) and by dragging an operator onto a snapped
connection. Splitting must also work when the connection starts at one of the
composition's **Input** nodes or ends at its **Output** node — these use a
special id internally and used to fail silently, deleting the connection
without inserting the new operator.

## Step: Click-split a connection between two operators

**Action:**
Hover the middle of a connection between two regular operators until the
insertion indicator appears, then click. Pick a fitting operator (matching
type) from the search.

**Expected:**
- The new operator is inserted into the connection: source → new op → target.
- `Ctrl+Z` restores the original direct connection and removes the operator.

## Step: Click-split a connection coming from an Input node

**Action:**
Hover and click the middle of a connection that starts at one of the
composition's input nodes (the nodes on the left representing the symbol's
inputs). Pick a fitting operator from the search.

**Expected:**
- The new operator is inserted: input node → new op → previous target.
- No connection disappears without replacement; nothing is logged as a warning.
- `Ctrl+Z` restores the direct connection.

## Step: Click-split a connection going to the Output node

**Action:**
Hover and click the middle of the connection that feeds the composition's
output node. Pick a fitting operator from the search.

**Expected:**
- The new operator is inserted: previous source → new op → output node.
- `Ctrl+Z` restores the direct connection.

## Step: Drag-split with an existing operator

**Action:**
Snap two operators together (vertically or horizontally), then drag a third
operator with matching input/output types onto the snapped connection between
them until the insertion highlight appears, and drop it.

**Expected:**
- The dragged operator is spliced into the connection and the downstream items
  shift to make room.
- `Ctrl+Z` restores the previous layout and connection.

## Step: Drag-split a snapped connection from an Input or to the Output node

**Action:**
Snap an input node directly to a downstream operator (and separately, an
operator directly to the output node). Drag another type-matching operator
onto the snapped connection and drop it.

**Expected:**
- The operator is spliced in; the connection chain remains complete
  (input/output node stays connected through the new operator).
- `Ctrl+Z` restores the direct snapped connection.
