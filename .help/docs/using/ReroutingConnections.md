# Rerouting connection lines

A dense graph stacks operators on top of each other, and the cables between them
run straight through the nodes instead of around them. Bend points let you pull
an individual cable aside so you can see where it goes.

## What bend points do

A bend point belongs to one connection, not to an operator. Add two or three and
that cable follows them exactly — it keeps its type colour, its hover behaviour
and its tooltip, it just takes the route you drew.

A rerouted cable is drawn as straight segments between its bend points, and each
bend point is shown as a round handle you can drag. The automatic route is hidden
while the cable is rerouted, so what you see is exactly the route you set.

Bend points only change how the cable is **drawn**. They have no effect on what a
graph evaluates or on the order operators run in, so you can never break a
project by rerouting a line.

## Adding a bend point

Hold `Alt` and hover a cable — a dot marks the spot the bend point will land on.
Click to drop it there.

- **`Alt`+Click** the cable to add a bend point.
- Keep the mouse button down and drag to place it in one motion.

`Alt` is only needed to *start* a bend point. The cable's normal click behaviour —
clicking it to insert an operator — is unchanged.

## Moving and removing bend points

Once a bend point exists it is a visible handle, so it needs no modifier:

- **Drag a handle** to move it. The handle fills in while you drag.
- **Right-click a handle** to remove it.

Dragging a handle never opens the operator search and never starts a selection
fence, so the gesture only ever affects the cable.

## Cables you cannot reroute

Two operators snapped directly together share a collapsed cable with no visible
length. There is nothing to bend there, and a bend point would have nowhere to go,
so `Alt` does nothing on those. Move one of the operators apart first if you want
to reroute the cable between them.

## Escaping and undoing

- Press `Esc` while dragging to abandon the drag and put the cable back the way it
  was.
- `Ctrl+Z` undoes an added, moved or removed bend point.

## When to reroute rather than rewire

Bend points are worth reaching for when the graph is *correct* but hard to read —
two operators sit on the same axis, a long cable crosses half the canvas, several
outputs leave one node and you cannot tell which is which.

If you are rerouting a cable because it is genuinely in the way of the layout,
check whether the graph wants restructuring instead: pulling an operator out of a
line into its own branch, or using a section to fold part of the graph away, often
fixes the readability problem for every cable at once rather than one at a time.

## See also

- [Presets and snapshots](PresetsAndSnapshots.md)
- [Pitfalls](Pitfalls.md) — what to do when an operator shows up as missing.
