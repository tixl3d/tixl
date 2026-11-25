# Routing Connections

Connection lines take the shortest path from an output to an input, so in a busy graph they cross operators and
each other. Route points let you lead a line around operators, and let several lines from the same output run
along one shared trunk. This page covers adding, moving, sharing and removing route points.

Route points only change how a line is drawn. They don't change what the graph computes, and they work for every
data type.

## Adding a route point

Hold `Alt` and move the mouse over a connection line. The hover circle grows to show where the point will go.
**Alt+Click** adds the point and you can drag it right away. Release the mouse to place it.

`Alt`+drag also pans the graph. A drag that starts right on a connection line adds a route point instead, so start
the pan on an empty spot, or pan with the middle or right mouse button.

Add as many points as you need. A point added between two existing points goes between them in the line's path.

Lines between operators that are snapped together have no visible length, so they can't be routed.

## Selecting and moving route points

Route points can be selected like operators:

- Click a route point to select it, **Shift+Click** to add it to the selection.
- A selection fence selects the route points inside it together with the operators.
- **Shift+G** (select connected inputs) also selects the route points of the lines between the selected operators.

A selected route point shows a white ring. Drag a route point to move it; no modifier is needed. If it is selected,
all selected route points move with it. Press `Esc` while dragging to cancel. The graph doesn't pan while you drag a
route point, even with `Alt` held.

While you drag a route point, it snaps vertically to the output and input of its line and to the line's other
route points, so straight horizontal runs are easy to make. A horizontal line shows where it snapped. Hold `Ctrl` to
drag without snapping.

When you drag selected operators, the selected route points move along. Route points that aren't selected stay where
they are, and the lines stretch to them. Dragging a section moves the route points inside it.

## Sharing a trunk

Lines coming from the same output can share route points. Where such lines run on top of each other, for example
right after the output, **Alt+Click** adds one point that all of them share.

To join lines that don't overlap yet:

1. Add a route point to the first line.
2. Add a route point to the second line and drag it onto the first point.
3. A large ring shows that the points will merge. Release the mouse.

Both lines now run through the same point and look like one line up to it. Moving the shared point moves the whole
trunk. Points of lines from different outputs can't be merged, so a trunk always carries one signal.

## Removing route points

There are three ways to remove a route point:

- **Alt+Click** it without moving the mouse. If you move the mouse before releasing, the point is dragged instead.
- Shake it while dragging, the same way you shake an operator off its connections.
- Select it and press `Delete`.

A shared point is removed from every line that uses it. A line without route points goes back to its automatic
path.

## Copying and undo

Route points are saved with the project. When you copy, paste or duplicate operators, the lines between the copied
operators keep their routes. Every change to route points can be undone with `Ctrl+Z`.

## See also

- [Keyboard shortcuts](KeyboardShortcuts.md)
