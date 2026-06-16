# Drag from multiple selected operator outputs at once (MagGraph)

Ticket: #1083 — https://github.com/tixl3d/tixl/issues/1083
Size: —   Milestone: v4.2

## Problem
In the legacy graph, dragging from the output of one of several selected operators (all the same symbol)
starts a drag from *all* their first outputs at once — dropping into a multi-input or onto the graph
background, filtering for matching ops with multi-input parameters. MagGraph only drags a single output.

## Affected code
- MagGraph current single-output / group-drag gating: `Editor/Gui/MagGraph/Interaction/MagItemMovement.cs:1315-1363`
  (`FindPrimaryOutputItem()` — restricts group drag; returns null when several items share the right edge).
- Output-drag initiation: `GraphStates.HoldOutput` (~349-408) and output-click detection in
  `MagGraphCanvas.DrawNode.cs`.
- Drop/finalize: `GraphStates.DragConnectionEnd` (~546-594, already iterates `context.TempConnections`),
  `InputPicking.TryConnectHiddenInput` (~75-125).
- Temp-connection data: `GraphUiContext.TempConnections`, `MagGraphConnection`.
- Legacy reference: `Editor/Gui/Graph/Legacy/Interaction/Connections/ConnectionMaker.cs:106-150`
  (`StartFromOutputSlot`) — when the clicked output's item is in a multi-selection, add one `TempConnection`
  per selected node whose first output matches the clicked output's value type.

## Proposed approach
1. On output-drag start, detect that the clicked output's item is part of a multi-selection; collect all
   selected items whose first output type matches; create one `TempConnection` per item (port the legacy
   loop) instead of a single one.
2. Reuse the existing multi-temp-connection drop path (`DragConnectionEnd` already loops temp connections);
   verify `InputPicking` finalizes N connections into the target (multi-input or background) and that
   type-matching holds for all sources.
3. Confirm snapping/positioning when several new connections feed one target.

## Risks / side-effects
- Touches the shared connection-drag flow across ~4–6 files (GraphStates, DrawNode, InputPicking,
  MagItemMovement, GraphUiContext, possibly OutputPicking). Medium integration risk — extend the existing
  state path, don't fork it.
- Edge cases: selected items with no outputs, mismatched output types (filter them out, as legacy does),
  dropping onto a single-input vs multi-input target.

## Open questions
- Does `FindPrimaryOutputItem`'s current restriction need relaxing/replacing, or can the multi-output drag
  bypass it?
- Background-drop behavior: replicate legacy "filter for matching ops with multi-input parameters" exactly?

Estimated ~200–300 lines across multiple interaction files. Needs in-editor verification.
