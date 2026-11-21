# ImageToLaserPoints

*in [Lib.laser.convert](README.md)*

Vectorizes a texture into ILDA laser points — the graph-side Find Paths / Find Contours stage that turns rendered images into laser shapes. Bright strokes and edges on a black background become lit laser paths; isolated bright pixels become dwell dots.

**Find Paths** mode thresholds the image and skeletons it (Zhang-Suen thinning): every thin bright stroke becomes ONE lit centerline — the encoding line art wants. A blob wider than `Thickness` switches to region outlines (marching squares): a thin line becomes the two-sided ribbon of its edges, matching the Thickness reduction vectorizer guides describe. **Find Contours** mode runs a Canny edge detector (Gaussian blur → Sobel → non-max suppression → hysteresis) and traces the edges as thin paths — outlines of everything with enough contrast, independent of fill.

The texture is read back through a staged ring buffer, so the vectorization runs one or two frames behind the rendered image — laser output tolerates this. Downscale to `MaxRes` uses max-pooling, so a thin bright line survives. Shapes are emitted as blanked approach point + evenly spaced lit samples (the encoding the output pipeline's segment detection splits into shapes), so the output setup's optimizer (reorder, blanking, corner dwell) and safety stage run on the result unchanged.

Like [LaserCamera] it is laser content: it lists as CONTENT in the Output Setup and routes through its `OutputRef`; the `LaserPoints` output feeds [LaserPointsToBuffer] for in-graph preview. The driver's point budget caps the frame; a frame that hits the cap sets the op's status warning.

## Input Parameters

| Name | Description |
|---|---|
| **Texture** | Image to vectorize — bright strokes or edges on a black background. Rendered frames, camera feeds and AI outputs all work; the cleaner the strokes (compression artifacts hurt), the better. |
| **Mode** | Find Paths: threshold + skeleton — one lit centerline per thin stroke (or region outlines for blobs wider than Thickness). Find Contours: Canny edges of the image. |
| **Threshold** | Brightness a pixel needs to count as content. Compression noise sits above zero — raise until the background drops out. Canny mode: how strong an edge must be (Sobel magnitude scale). |
| **BlurSize** | Find Contours only: Gaussian blur before the Sobel gradient — none or a 3/5/7/9 px kernel. Denoises the image so small parts are not detected. |
| **UseColor** | When on, shapes take the source pixel's color; when off, they are white. |
| **Thickness** | Find Paths only: expected stroke width. 1 = skeletonize everything into centerlines. Higher = blobs wider than this trace as region outlines instead (a thin line then yields the two sides of its ribbon). |
| **Denoise** | Drop shapes shorter than this many working pixels (dots are dropped at ≥ 1). Kills speckle from compressed sources. |
| **MaxPaths** | Cap on shapes per frame — the guard against pathological content producing a multi-second frame. Excess shapes are dropped. |
| **PointSpacing** | A point every N working pixels along a shape (like LaserCamera's `MaxEdgeStep`). Smaller = smoother/brighter lines, more points. |
| **MaxRes** | Working-resolution cap (256/512/1024/2048). Higher = finer detail, more CPU: 256 fits the laser pump's compute budget in real time; 1024+ is for detail-heavy static content. |
| **Brightness** | Global color multiplier (0..1) applied to the emitted points. |
| **Update** | Supplier freeze: when off, the last frame keeps streaming and nothing re-vectorizes. |
| **OutputRef** | Laser output this op feeds — drop the op on an output card in the Output Setup to set it. |

## Outputs

| Name | Type |
|---|---|
| **LaserPoints** | StructuredList — ILDA 16-bit laser points, shapes blanked-separated |
| **PointCount** | Int32 — points in the emitted frame |

## Notes

- Content best practice (same as the vectorizer guides): thin lines, no antialiasing if possible, black background, low compression.
- Borders are never thinned/deleted by the skeleton stage — content touching the image edge keeps its thickness there and is clipped at the field border.
- A frame that is *only* a static spot still renders dark (the output's static-dwell limiter) — beam effects need motion.
- CPU cost scales with `MaxRes`: 512² thinning ≈ 10–15 ms, Canny ≈ 5–10 ms; the pump's round-robin budget defers an output that misses its slice instead of stalling the editor.

**See also:** [LaserCamera](../camera/LaserCamera.md), [MeshToLaserPoints](MeshToLaserPoints.md), [LaserPointsToBuffer](LaserPointsToBuffer.md)

---
*Generated from operator definition in Lib.laser.convert*
