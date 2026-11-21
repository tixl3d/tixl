# Laser optimization

Scanners are physical devices with inertia: jumping long distances or hitting corners at full speed smears the image. Every laser output runs an optimization stage in the Output Setup (select the output - the **Optimization** section) that reorders the drawing and inserts helper points so the scanners can keep up. The same settings apply no matter which plug the output streams to.

## Processing stages

1. **Segment detection** - the frame is split into segments at blanked points (`I == 0`). Everything between two blanks is one shape. This is also how dots stay separate from lines: [LaserCamera](LaserCamera.md) (dots) and [MeshToLaserPoints](MeshToLaserPoints.md) (vertices mode) encode each dot as *blanked jump + dwell run*, which shows up here as its own single-shape segment.
2. **Path optimization** *(if `Reorder path` is on)* - greedy nearest-neighbour ordering: starting from the current position, the closest unvisited segment endpoint is chosen (either end of the segment; choosing the end flips the segment's direction). This minimizes total travel and therefore maximizes effective frame rate. The finished tour is then rotated so its **loop seam** - the link from the last shape back to the first, which the DAC rescans every time it replays the frame - is the shortest available link, keeping the loop closed and smooth.
3. **Blanking** - the beam is always off while traveling between shapes, and the travel is a **ramp**: the blanked samples are distributed along the line from the previous shape to the next, with the last samples held at the destination so the scanners settle before the beam opens. A single-sample jump would be a maximal-acceleration demand that rings the galvos - the ramp is what scanner longevity is made of. The sample count scales with jump length up to `Blanking points` (`ceil(distance / Max jump * delay)`, always ≥ 1 for real moves); without the blank at all, nearby shapes (and dot dwell runs) would be connected by visible lit lines. Sub-pixel moves insert nothing - the beam is already there. `Max jump` is therefore a *scaling reference*, not an on/off threshold.
4. **Corner dwell** - corners with an interior angle below `Corner angle` degrees repeat the corner point `Corner anchors - 1` extra times so the beam lingers and the corner renders sharply. (Dwell runs of dots evaluate as 180° and are never duplicated. The interior angle is what you eyeball: 180° is a straight line, 90° is a square corner - so at the default 30° only spikes dwell, and a square's corners need a threshold above 90°, e.g. 120-135°.)

## The settings

| Setting | Default | Purpose |
|---------|---------|---------|
| `Reorder path` | on | Off = keep original order; blanked travel is inserted before shape starts only, then corner processing |
| `Max jump (units)` | 5000 | Scaling reference for blank delay (16-bit units, full screen = 65534) |
| `Blanking points` | 5 | Max blanked points per inter-shape move (clamped 0..100), scaled by distance; always ≥ 1 for real moves |
| `Corner angle (°)` | 30° | Angles below this count as sharp corners (180° = straight) |
| `Corner anchors` | 3 | Repeats per sharp corner (1..10) |

## Usage notes

* The optimization increases the point count. If the output was already near its per-frame point budget, corners/blanking push frames longer - watch the point count against your scan rate.
* Turn `Reorder path` off to compare, or when the input order matters (e.g. text where drawing order defines stroke overlaps).
* The nearest-neighbour pass is greedy, not a full TSP solve - cheap and predictable, but not always the globally shortest path. It is O(n²) in the segment count, so with very large dot clouds (hundreds+ of dots) consider turning `Reorder path` off and letting blanking do the work in input order. A frame too dense even for the tour grid (more than 4096 segments after detection) skips the reorder entirely and keeps the input order - still blanked and corner-dwelt, so the render thread never stalls on a dense frame.

## See also

- [Laser safety](Laser%20Safety.md) - the safety stage that always runs after this one.
- [Laser data model](Laser%20Data%20Model.md) - how frames reach the optimization stage.
