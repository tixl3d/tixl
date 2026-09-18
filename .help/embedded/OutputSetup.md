# Output Setup

Routes what your graph renders onto real displays, projectors and streams. A setup describes the venue and travels with the project; the bindings to this computer's displays and streams stay local.

**The canvas tabs**

- **Board** — every part of the setup as a card, laid out in metres: sends, surfaces (walls, screens), outputs (projectors) and reference photos. A send's card shows its texture with every slice drawn on it; the selected slice is edited right there. Drag cards to arrange them.
- **Straight** — the selected surface seen flat, for placing content and regions on it.
- **Output** — a projector's canvas: corner-pin each surface's quad over the live composite, or aim calibration points. Content sent straight to the output keystones here too — its corner handles sit on the canvas corners. Right-click bare canvas for the output's own menu.

**The strip's columns**, along the flow content → surfaces → outputs → plugs:

- **Content** — the [SendToOutput] ops in your graph. A slice cuts a rectangle out of one; slices are what surfaces and patches show.
- **Surfaces** — the physical planes light lands on. A surface shows one slice and is mapped onto one or more outputs; regions subdivide it.
- **Outputs** — projector or display canvases in pixels. A patch feeds a slice onto part of a canvas without a surface.
- **Local bindings** — this machine's plugs: attached displays and Spout / NDI senders (**+** adds one).

The **+** at the right of the header adds what no column lists: a reference photo to trace surfaces on, or a prop for scale.

Drag any row onto another to connect them, in either direction: a drop replaces what the target showed. Click empty strip to return to the Board.
