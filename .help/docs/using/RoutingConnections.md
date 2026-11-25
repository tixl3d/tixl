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

## Moving route points

Drag a route point to move it; no modifier is needed. The point fills in while it is under the mouse or being
dragged. Press `Esc` while dragging to cancel. The graph doesn't pan while you drag a route point, even with `Alt`
held.

When you drag operators, a route point moves along only if the operators at **both** ends of every line that uses
it are dragged together, for example when you select a group of operators and drag them. If you move only one of
the two operators, its line stretches to the route points, which stay where they are.

## Sharing a trunk

Lines coming from the same output can share route points:

1. Add a route point to the first line.
2. Add a route point to the second line and drag it onto the first point.
3. A large ring shows that the points will merge. Release the mouse.

Both lines now run through the same point and look like one line up to it. Moving the shared point moves the whole
trunk. Points of lines from different outputs can't be merged, so a trunk always carries one signal.

## Removing route points

**Alt+Click** a route point (without moving the mouse) to remove it. If you move the mouse before releasing, the
point is dragged instead. A shared point is removed from every line that uses it. A line without
route points goes back to its automatic path.

## Copying and undo

Route points are saved with the project. When you copy, paste or duplicate operators, the lines between the copied
operators keep their routes. Every change to route points can be undone with `Ctrl+Z`.

## See also

- [Keyboard shortcuts](KeyboardShortcuts.md)
