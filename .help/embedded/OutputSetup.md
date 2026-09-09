# Output Setup

Routes what your graph renders onto real displays, projectors and streams. A setup describes the venue and travels with the project; the bindings to this computer's displays and streams stay local.

- **Board** — every part of the setup as a card, laid out in metres: the content, the surfaces (walls, screens), the outputs (projectors) and reference photos. Drag cards to arrange them; double-click a card to edit it in its own space.
- **Straight** — the selected surface seen flat, for placing content and regions on it.
- **Output** — a projector's canvas: corner-pin each surface's quad over the live composite, or aim calibration points.
- **Content** — the [SendToOutput] ops in your graph. A slice cuts a rectangle out of one; slices are what surfaces and patches show.
- **Surfaces** — the physical planes light lands on. A surface shows one slice and is mapped onto one or more outputs; regions subdivide it.
- **Outputs** — projector or display canvases in pixels. Patches feed a slice straight onto part of a canvas, without a surface.
- **Local bindings** — this machine's plugs: attached displays and Spout / NDI senders (**+** adds one). Drop an output on a plug to present it there.

Connect by dragging one row onto another in the strip, or use the row's context menu. Click empty strip to return to the Board.
