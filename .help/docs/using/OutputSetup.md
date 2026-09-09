# OutputSetup

The Output Setup is where a project meets the room: it maps what your graph renders onto the displays, projectors and video streams of a venue. Use it for projection mapping onto walls and objects, for multi-display installations, and for sending a rendered canvas to another application over Spout or NDI.

This page walks through the parts of the view, the way content flows through a setup, and how a setup is bound to the computer it runs on.

## Where to find it

Open an **Output window** and switch it to setup mode with the projector button in its toolbar. The window then shows the setup canvas on top and the **flow outliner** strip below it. The strip's header holds the setup switcher, the canvas tabs (**Board**, **Straight**, **Output**), a help button and a toggle that collapses the strip to its header.

A project can hold several setups, one per venue. The switcher's menu creates, duplicates and deletes them. Duplicating keeps every entity id, so operators that reference a surface or output keep working in the copy.

## Two files, one show

A setup is saved next to the project in its `.meta` folder and is meant to be shared and versioned: it describes the venue in metres and pixels, but never which connector of which computer drives a projector. That part lives in a second, machine-specific file, `outputs.machine.json`, which holds this computer's **local bindings**. A touring show keeps one setup per venue and rebinds outputs on the show computer once.

## The flow outliner

The strip below the canvas lays the setup out along its content flow, in four columns:

1. **Content** — every [SendToOutput] op in the graph. Each one is a source of pixels. A **slice** cuts a rectangle out of a source; sources start with one full-frame slice, and the source's context menu adds more.
2. **Surfaces** — the planes light lands on: a wall, a screen, the face of a set piece. A surface shows one slice and is mapped onto one or more outputs. **Regions** are coplanar children of a surface, arranged in pixels, for poster slots and split layouts.
3. **Outputs** — the canvases that projectors and displays receive, in pixels. Surfaces are corner-pinned onto them. A **patch** feeds a slice straight onto part of the canvas without a surface, for a plain display or a quick test.
4. **Local bindings** — this machine's plugs. Attached displays are detected; **+** adds a Spout or NDI sender under a name you choose. A plug whose package isn't loaded is listed but sends nothing.

Lines between the columns show the routing: slice → surface, surface → output, slice → patch, output → plug. Hovering or selecting a row lights up its lines. Rows whose entity isn't in use recede.

### Connecting things

Drag a row onto another row to connect them. The direction doesn't matter: dropping a surface onto an output is the same link as dropping the output onto the surface. What connects to what:

- A slice or source onto a surface shows it there. Dropping a second slice onto an occupied surface adds a region for it instead of replacing.
- A surface onto an output maps it there with a default corner pin.
- A slice or source onto an output adds a full-canvas patch; onto a patch, it re-feeds that patch.
- An output onto a plug binds it to that display or stream.

With a slice or surface selected, every row that could take it shows an arrow in its gutter; clicking the arrow binds or unbinds without dragging. Each row's context menu offers the same connections plus Rename, Duplicate and Delete. `Del` removes the selection while the strip has focus.

## The canvas

### Board

The Board shows every entity as a card at its neutral placement, in metres: sources, surfaces, outputs, reference photos and props. It answers "what is in this venue and how big is it". Drag a card by its frame or its name label to move it; the handle at the top-right corner scales it. Selecting a card selects the entity everywhere else; right-click a card or its label for the entity's menu. Right-click empty Board to add a surface, a reference image or a prop.

Double-clicking a card enters its space: a surface opens **Straight**, an output opens **Output**, a source opens its texture to lay out slices. Clicking empty strip or choosing **Board** returns.

### Straight

The selected surface seen flat, as if you stood in front of it. Place its content here, add and arrange regions, and set the surface's real size. A surface traced on a reference photo straightens on that photo, in place.

### Output

An output's canvas: the live composite the projector receives. Each mapped surface appears as a quad you corner-pin over the real wall while watching the projector. Once a surface carries reference points, aim them at their real positions and the pin is solved to hit them exactly. Patches appear as plain quads on the canvas.

## Reference images and props

A **reference image** is a photo or a plan of the venue. Trace a surface on a photo to define it from the picture, then measure it with line annotations to give it real dimensions. Plans are orthographic and only need a scale. A **prop** is a scale reference in the stage — a 1.70 m person, a table — so sizes can be sanity-checked at a glance.

## Presenting

An output presents when it is bound to a plug and its **Send** toggle is on. Turning Send off pauses the output without dropping the binding, useful to mute a projector between cues.

- **Displays** open the output fullscreen on that display. One display can be driven at a time today; the first bound output takes it.
- **Spout and NDI** push the output's composite into a sender under the plug's name every frame. Other applications on the same machine (Spout) or the network (NDI) receive it. Rename the plug to rename the sender; remove it from its context menu.

The bindings are also reachable from the output's context menu and from the Output window's breadcrumb menu.

## Content from the graph

[SendToOutput] supplies a texture to the setup. Its **Update** toggle freezes the content at its last frame when off. The op's parameter window shows the texture's resolution and how many slices and targets use it.

[UseProjectorCam] renders its content through an output's projector camera, so 3D geometry aligned to the stage lands correctly on that projector without a corner pin. Wire its result through a [RenderTarget] into a [SendToOutput] bound to the same output.

## Tips

- Name surfaces after the real thing ("Left wall", "Bar counter"); slices and patches are named after what they cut from, so they stay right when you rename.
- Keep the outliner strip tall while routing and collapse it while pinning corners.
- Every structural edit is undoable with `Ctrl+Z`. Bindings are machine state and save immediately, outside the undo stack.

## See also

- [Live performances](LivePerformances.md)
- [Realtime rendering](RealtimeRendering.md)
