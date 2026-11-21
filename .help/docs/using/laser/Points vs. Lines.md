# Points vs. Lines

A laser scanner only produces two primitives: a **stroke** (beam on while moving) and a **dot** (beam on while standing still). Everything you show - line art, text, point clouds, mesh wireframes - is a combination of those two. The laser operators therefore encode *which primitive is meant* directly in the point stream, using the blanking convention, so that every stage from source to DAC treats them consistently.

## The encoding

The stream convention (see [Laser Data Model](Laser%20Data%20Model.md)):

* A **line / shape** is a run of lit points (`I > 0`) - consecutive positions are stroked.
* A **dot** is a blanked jump (`I = 0`) to the dot position followed by a *dwell run*: the same position repeated `N` times lit. The beam jumps while off, then lingers bright enough to be visible.
* Blanked runs of any length are shape boundaries - nothing between two blanks is ever connected by a lit line.

```
Lines mode:      lit(x0) lit(x1) lit(x2) ...                  -> one continuous stroke
Points mode dot: blank(d) lit(d) lit(d) ... lit(d)            -> isolated dot at d
Wireframe edge:  blank(e0) lit(s0) lit(s1) ... lit(e1)        -> isolated edge stroke
```

## Where it is produced

[LaserCamera](LaserCamera.md) decides the primitive **automatically from what is drawn in its Scene** - there is no mode switch:

| Source content | Scene draw | Encoding |
|----------------|------------|----------|
| Point clouds | point draw op ([DrawPoints2]-style) | Isolated dwell dots (blank + `DotDwellPoints` lit repeats) |
| Line strips (with `PairPointsForLines` separators) | line draw op ([DrawLines]-style) | Blank-separated line segments |
| Meshes (`MeshBuffers`) | [DrawMesh]-style op | Wireframe - each unique edge as an isolated lit run |

The stock draw graphs register their buffer and primitive kind during the capture pass. Point clouds
and line strips are told apart after readback: line strips carry separator points, clouds do not.

## Where it is respected

* [Laser optimization](LaserOptimizer.md) splits the frame into segments at blanked runs - each dot dwell run is its own single-shape segment, reordered like any other shape. Since 2026-09 it blanks on **every** inter-shape move (at least 1 point, scaled by jump length up to `BlankingDelayPoints`), so even nearby dots and shapes can never burn a connecting line. Corner dwell ignores dwell runs (they evaluate as 180°).
* [Laser safety](Laser%20Safety.md) passes dots through unchanged as long as the dwell run stays within `MaxStaticPoints` (default 64) - the operators clamp their `DotDwellPoints` to 64 for exactly that reason. Long blanked dot jumps are subdivided with blanked points only.
* The transports ([EtherDream](EtherDream.md), [PONK](PONK.md)) send the encoding as-is; on the wire blanking is intensity 0 / the DAC control bit.

## Practical notes

* **Colors stay colors**: primitive type is decided by the connected input, never by dimming a color to zero. Fading a color to black fades the artwork; it does not change which shapes exist (upstream of the converter), and it does not accidentally merge or split paths.
* **Frame budget**: a dot costs `1 + DotDwellPoints` points. 100 dots at dwell 8 = 900 points of the frame - tune dwell against your kpps budget.
* **Many dots**: the optimizer's reorder pass is O(n²) in segments; for very large clouds switch `EnableOptimization` off - blanking already guarantees correctness in input order.

## Same image on every output

All three outputs draw the **same frame content** - what [LaserCamera](LaserCamera.md)/[Laser optimization](LaserOptimizer.md) produce is what reaches the beam, the network or the preview:

| | [EtherDream](EtherDream.md) | [PONK](PONK.md) | [CITP](CITP.md) |
|---|---|---|---|
| Field mapping | ILDA 16-bit pass-through | ÷32767 → [-1,1] | ÷32767 → [-1,1] → 0..4095 |
| Full ILDA field maps to | full DAC field | full PONK range | full CITP range |
| Blanked points | `I = 0` (beam off) | black points | black points |
| Colors | 16-bit | 8-bit | RGB565 |
| Point filtering | none | none | none |

So shapes land at the same relative positions and dots/strokes keep their identity everywhere. The remaining differences are protocol-inherent, not content differences: color depth (16-bit vs 8-bit vs RGB565) and the fact that PONK/CITP receivers render black points as invisible connectors instead of turning the beam off. If a receiver's image is misplaced, check its aspect/orientation settings first - the operators never flip axes.
