---
id: graph-connection-splitting
title: Splitting, Routing, and Cutting Connections
added: 2026-06-10
added-in-version: 4.3
scope: graph-window
tags: [user, essential]
prerequisites:
  - A project you can edit is open in the Graph Window.
  - A graph you can edit (e.g. a new empty operator) with at least one input node, a few operators, and an output node.
related-help:
  - ../.help/docs/using/KeyboardShortcuts.md
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

## Step: Keep reroute anchors out of search and browsing

**Action:**
Open node search on empty graph space, clear the query, then search for `rer`, `RerouteFloat`, and `Types.Routing`. Repeat when inserting an operator into a float cable. In the node browser, search for the same terms, then clear the search and expand `Types`. Refresh the browser tree. Search for `Float` in both search surfaces, then Alt+RMB drag across a float cable to create an anchor. Reload the editable graph and repeat both searches and tree refresh. Restart the editor to reload the built-in TypeOperators package, then repeat with float, vector, and command anchors.

**Expected:**
- No reroute operator appears in either search surface, including exact-name and namespace searches.
- The browser tree contains no reroute entries or empty `Routing` folder, including after refresh.
- Ordinary operators such as `Float` remain discoverable.
- Reloading or replacing a symbol UI retains the same visibility; an ordinary duplicated operator remains visible.
- The routing gesture still creates a working anchor; undo and redo restore the expected connections.

## Step: Route a subset of a fan-out

**Action:**
Connect one float output to three operators. Hold Alt and drag the right mouse button across only two cables. Inspect the preview, then release. Move the new anchor by its center.

**Expected:**
- One compact anchor appears at the previewed position, connected to the original source and the two crossed targets.
- The third connection stays direct. Moving the anchor changes the cable paths without changing values.
- Undo once reverses the move; undo again removes the anchor and restores both direct cables. Redo restores the same anchor and position.

## Step: Keep independent sources separate

**Action:**
Cross wires from two different float outputs and a string output in one Alt+RMB stroke. Double back across a cable before releasing.

**Expected:**
- Three separate anchors appear, with their original types and source values.
- Repeated crossings of one cable do not create duplicate anchors or connections.
- One undo reverses the entire routing stroke.

## Step: Cut several multi-input occurrences

**Action:**
Connect several sources to a multi-input, including the same source twice with another source between those occurrences. Ctrl+RMB drag across the middle and last cables, then undo and redo. Repeat with Alt+RMB routing instead of cutting.

**Expected:**
- Only the crossed occurrences change; untouched sources keep their order.
- Undo restores the exact source sequence, including both identical occurrences.
- Routing preserves one target occurrence per crossed cable. Cutting keeps ordinary source operators and removes an anchor only when its last incident cable is cut.

## Step: Use the compact sockets

**Action:**
Drag from a reroute's right socket to a compatible input, then reconnect its left socket from another compatible output. Disconnect one side while leaving the other attached. Repeat at 25% and 50% zoom with 100% and 200% UI scale. Try connecting an incompatible type. Select and duplicate the anchor; copy/paste it with its neighboring nodes, then delete and undo.

**Expected:**
- The disconnected socket remains usable and retains the same type while the other side is attached. Both sockets are available on a newly added blank anchor.
- Socket drag and body movement have distinct hit regions. Temporary cables terminate at the visible socket.
- Outgoing cables start at the dot's edge without a gap. Incoming arrowheads keep their existing shape and position.
- Check horizontal, rising, falling, and backward cables at fractional zoom while dragging and undoing. Outgoing cables slightly overlap the dot's outline, and endpoints follow the displayed dot even while position smoothing is active.
- At low zoom, hovering or pressing the anchor's body gives it priority over the input cable's hover area. No cable hover indicator covers the dot; dragging the center moves the anchor, while its sockets still start connections.
- Incompatible connections and cycles are refused. Ordinary duplicate, clipboard, deletion, and undo behavior works.
- Deleting an anchor removes its incident cables without reconnecting its neighbors.

## Step: Remove an anchor after its last disconnection

**Action:**
Connect an anchor between a source and two targets. Remove its input wire, then its output wires one at a time. Undo and redo the final removal. Repeat using Disconnect, shake, and Ctrl+RMB cutting. Include two anchors joined only to each other, and a mixed selection of anchors and ordinary nodes. Give an anchor a name, comment, non-default input value, disabled state, and section membership before testing undo.

**Expected:**
- The anchor stays while any input or output cable remains and disappears when a completed action removes its last cable.
- One undo restores the last disconnected cable and the anchor with the same ID, type, position, settings, and section. Redo removes it again.
- Disconnect and shake retain their existing surrounding-wire reconnection behavior; their cleanup shares the disconnect undo entry. Shake ends the active anchor move before deletion.
- After shaking off an anchor, keep holding the left mouse button and move across the background and other nodes: no box selection or new node interaction starts. Release, then press and drag again to select normally. Also release outside the graph and return; the next press works normally.
- Ordinary disconnected nodes remain. A connection between two anchors keeps both alive until that connection is removed; cutting it removes both in the same undo step.
- A newly added blank anchor is not removed by unrelated edits. Deleting a wire during a reconnect drag does not remove its anchor before the drag finishes; dropping on a compatible socket keeps it.

## Step: Move anchors over each other without combining

**Action:**
Create two float anchors with separate outgoing branches. Drag one anchor's center onto and near the other, then release. Repeat with shared and different sources, one unconnected input, two blank anchors, a direct chain, and a multiple-item selection. Include duplicate connections to a multi-input with another source between them. Undo and redo each move. Repeat at 25% zoom and 200% UI scale, then move the anchors apart.

**Expected:**
- Both anchors retain their IDs, types, settings, and original wiring, including multi-input order and duplicate occurrences. Only the dragged positions change.
- Moving near or over another anchor shows no enlarged merge target, absorbed-node preview, or redirected preview cables. Overlapping dots may cover each other; moving them apart reveals both anchors.
- One undo restores the original positions; one redo restores the moved positions. Neither action combines or deletes anchors.
- Outgoing wires meet their own dot, and incoming arrowheads keep their existing shape and position.

## Step: Cancel without panning or opening a menu

**Action:**
Start each gesture and press Escape while still holding RMB. Move the mouse, then release. Repeat with a popup, leaving the graph window, dragging into a second graph view, and navigating to another composition during the stroke. Try a modified click without dragging, a stroke that hits nothing, and Ctrl+Alt+RMB.

**Expected:**
- No graph edit or undo entry is created in any of these cases.
- A cancelled press stays consumed until release; it does not turn into pan/zoom or open a context menu.
- Ordinary unmodified RMB panning works on the next press.

## Step: Check both canvas zoom settings

**Action:**
Repeat successful routing and cutting with each value of Middle Mouse Button Zooms. Begin a stroke immediately after zooming. Change Alt/Ctrl while holding the mouse during a stroke.

**Expected:**
- The canvas stays fixed while a stroke is held, including pending smooth zoom/pan motion.
- The tool is chosen at mouse-down and does not change with the modifiers mid-stroke.
- Unmodified gestures and wheel navigation work normally afterward.

## Step: Hit the displayed cable geometry

**Action:**
Use horizontal, vertical, backward-curving, and snapped connections. Cross them quickly, including one crossing on the release movement. Repeat at low/high zoom and UI scale. Route or cut a visible connection crossing a collapsed section boundary.

**Expected:**
- Crossing a displayed curve or snapped connector selects that occurrence even between cursor frames.
- Crossing empty space between curve endpoints does not count as crossing the cable.
- Fully hidden internal section connections stay unchanged. Visible boundary cables change their actual underlying connections.
- Wires and snapped markers completely outside the visible clip rectangle cannot be collected through the stroke's hit tolerance; visible clipped portions remain hittable.
- Hover targets, preview positions, and final anchor positions remain aligned at each scale.

## Step: Preserve anchors during automatic layout

**Action:**
Move anchors to deliberate positions, include them in a mixed selection, and run automatic layout. Drag the selection near other nodes and connections, including a potential splice with the anchor elsewhere in the selection. Repeat with an ordinary-only selection. Manually reconnect an anchor socket after layout. Fit the selection and place it inside a section.

**Expected:**
- Automatic layout keeps anchors fixed and treats their bounds as obstacles.
- Moving the selection still works, but anchors do not block-snap, splice, or create automatic connections.
- Any selected anchor prevents splicing the whole selection; ordinary-only selections retain snapping and splicing.
- Manual socket wiring still works after layout.
- Fit-selection and section bounds reflect the compact anchor size.

## Step: Reject unsupported routing without partial edits

**Action:**
Include a composition multi-input bundle source or a time-clip output in a Alt+RMB stroke together with an ordinary float cable. Try a routing stroke in a read-only graph. Separately cut an editable bundle cable with Ctrl+RMB.

**Expected:**
- The unsupported routing stroke leaves all cables intact and explains the unsupported connection.
- Read-only graphs cannot be edited by either gesture.
- Cutting an editable bundle cable removes that occurrence normally; undo restores it.

## Step: Verify command execution and persistence

**Action:**
Route a command chain with render-state preparation/restoration through two anchors and fan it out. Reconnect its source, disable/re-enable the command anchor, and try Bypass from both the graph and parameter panel. Save, reopen, and repeat after package reload. Export an executable using the routed graph.

**Expected:**
- Preparation, evaluation, and restoration retain their order from the first evaluation, with no duplicate evaluation per consumer.
- Disabled command anchors do not forward callbacks. Re-enabled anchors resume correctly. Bypass leaves reroutes unchanged.
- Anchor types, positions, ordinary connections, and rendered output survive save/reload and executable playback.
