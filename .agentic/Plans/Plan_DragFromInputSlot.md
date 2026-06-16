# Drag from input slot onto the graph (MagGraph)

Ticket: #1074 — https://github.com/tixl3d/tixl/issues/1074
Size: —   Milestone: v4.2

## Problem
Dragging from the input area of a parameter out onto the graph (to create/redirect a
connection) works in the legacy graph but is unimplemented in the new MagGraph. The entry
point is a stub that only logs.

## Affected code
- Stub: `Editor/Gui/MagGraph/Ui/MagGraphView.cs:206` — `IGraphView.StartDraggingFromInputSlot(...)`
  currently just `Log.Debug("... not implemented yet")`.
- Caller: `Editor/UiModel/InputsAndTypes/InputValueUi.cs:834` — fires once the drag delta from
  an input slot passes the click threshold.
- Legacy reference: `Editor/Gui/Graph/Legacy/GraphView.cs:82` → `ConnectionMaker.StartFromInputSlot(...)`
  in `Editor/Gui/Graph/Legacy/Interaction/Connections/ConnectionMaker.cs:152-180`.
- MagGraph connection-drag machinery to reuse: `GraphUiContext` (ActiveSourceItem / ActiveTargetItem /
  ActiveInputDirection / TempConnections), `GraphStates.DragConnectionEnd`, `InputSnapper`, `InputPicking`.

## Proposed approach
Implement `StartDraggingFromInputSlot` to mirror the legacy two-case logic:
1. If an existing connection feeds this input → delete it and start dragging *its source output*
   (legacy `IsDisconnectingFromInput`).
2. If the input is unconnected → start a new temp connection *targeting* this input (reverse of the
   normal output-drag), typed from `inputDef.DefaultValue.ValueType`.
Then enter the MagGraph connection-drag state machine (set the Active* fields on `GraphUiContext`,
push a `TempConnection`, transition so `DragConnectionEnd` handles the drop) and wrap the mutation in
a macro command for undo.

## Risks / side-effects
- Connection drag is a shared interaction surface; getting the state-machine transition wrong can
  strand the UI in a half-dragging state. Build on the existing `DragConnectionEnd` path rather than a
  parallel one.
- Undo: must construct/commit through a macro command like the legacy path.
- Reverse-direction temp connections (target-anchored) must be supported by the snap/pick code.

## Open questions
- Does the current MagGraph `TempConnection` model cleanly express a target-anchored (input-side)
  drag, or does it assume an output source? May need a small extension.
- Interaction parity: should the disconnect-and-redrag case match legacy exactly, or adopt MagGraph's
  snapping conventions?

Estimated ~150–250 lines across MagGraphView + 2–3 interaction files. Needs in-editor verification.
