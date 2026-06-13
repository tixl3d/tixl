# Thumbnail renderer extraction

Move variation/preset/snapshot thumbnail rendering out of the Variations window
(`VariationBaseCanvas`) into a standalone, frame-driven service so thumbnails can
be (re)rendered without that window being open — e.g. the snapshot control view's
**+** button, headless re-render, future callers.

## Why

Today all thumbnail rendering lives in [`VariationBaseCanvas`](../../Editor/Gui/Windows/Variations/VariationBaseCanvas.cs):
`DrawBaseCanvas` → `UpdateThumbnailRendering` → `UpdateNextVariationThumbnail` →
`ApplyVariationForThumbnail` / `CapturePendingThumbnail` / `RenderThumbnail`. It only
runs while the Variations window draws its canvas tab. The snapshot control view's +
therefore can't produce a thumbnail on its own (current workaround:
`SnapshotCanvas.RequestThumbnailSave` defers until the window next draws).

## What the renderer actually needs (confirmed)

- An **output texture slot** + **EvaluationContext**: today `RenderProcess.OutputWindow.ShownInstance.Outputs[0]`
  as `Slot<Texture2D>`, updated with a static `EvaluationContext` (170×130). `RenderProcess`
  is a per-frame singleton, updated independent of the Variations window — so the output
  is available whenever an **Output window** shows a texture-producing instance. The gate
  `RenderProcess.State == ReadyForExport` just means "valid output present and idle".
- The **pool + composition instance** (`SymbolVariationPool`, `Instance`).
- **Apply/restore**: `pool.BeginHover(instance, variation)` to set the variation's state,
  render one frame later (race-avoidance delay = 1 frame), then `pool.StopHover()`.
- **Save**: `ThumbnailManager.SaveThumbnail(variationId, package, texture, category, saveToFile)`.
- **Category**: `PackageMeta` (curated default, persisted) vs `Temp` (live preview).
  Live ids tracked in `_liveThumbnailIds` so `RestoreDefaultThumbnails` can invalidate them
  when live previews are turned off.

## State machine (today, per-canvas)

`_renderThumbnailIndex`, `_pendingThumbnailVariation`, `_thumbnailCaptureDelayFrames`,
`_allThumbnailsRendered`, `_rerenderRequested`, `_rerenderToFileRequested`,
`_currentRenderInstance`, plus static `_imageContext`, `_liveThumbnailIds`.

## Design

New `VariationThumbnailRenderer` (static, `Editor/Gui/Interaction/Variations/`):

- Owns the state machine and the `EvaluationContext`.
- **Ticked once per frame** from `VariationHandling.Update()` (already called every frame,
  window-independent) — *not* from any window draw.
- Acquires the output slot from `RenderProcess` each tick; no-ops when none is available.
- Request API:
  - `RequestRender(pool, instance, variationId, toFile)` — one variation.
  - `RequestRenderAll(pool, instance, toFile, onlySelected)` — bulk ("Update thumbnails").
  - Live-preview mode for the Variations window: enqueue visible/all variations to `Temp`.
- Keeps `_liveThumbnailIds` + `RestoreDefaultThumbnails` (moved here).

Consumers:
- `VariationBaseCanvas`: stops owning the loop; calls the service for live previews,
  "Update thumbnails", and on creation. Still draws thumbnails via `ThumbnailManager.GetThumbnail`.
- `SnapshotControlView` +: `VariationThumbnailRenderer.RequestRender(pool, instance, id, toFile:true)`
  (replaces the `SnapshotCanvas.RequestThumbnailSave` shim).

## Phases

1. **Service skeleton + one-shot render.** ✅ **Done (2026-06-13).**
   [`VariationThumbnailRenderer`](../../Editor/Gui/Interaction/Variations/VariationThumbnailRenderer.cs)
   owns a single-variation request queue + the capture state machine, ticked from
   `VariationHandling.Update` (window-independent), rendering into `RenderProcess`'s pinned
   output. The snapshot control view's + routes through it. The canvas's live loop stays in
   place; to avoid both driving the pinned output the canvas calls
   `VariationThumbnailRenderer.NotifyCanvasRendered()` and the service yields a few frames.
   The interim `SnapshotCanvas.RequestThumbnailSave` shim was removed.
2. **Migrate the canvas loop.** Move live-preview + "Update thumbnails" + selected-render
   + `_liveThumbnailIds`/`RestoreDefaultThumbnails` into the service; canvas becomes a
   consumer. Remove the dual path and the `SnapshotCanvas.RequestThumbnailSave` shim +
   `VariationBaseCanvas.ProcessExternalRequests` hook added as the interim bridge.
3. **Cleanup + docs.** Remove dead fields; manual test set covering: + with Variations
   window closed renders a thumbnail; live previews still toggle/restore; "Update
   thumbnails" still writes defaults; export unaffected.

## Risks / verification

- Pure runtime/visual behavior — **must be verified in the running editor** (thumbnails
  can't be asserted in a headless build). Live previews, restore-on-disable, and the
  hover/blend interplay are the fragile parts.
- The renderer still needs *an Output window* showing a texture output. "Out of the
  Variations window" — yes; "no windows at all" — no (there's no texture otherwise).
- Only one variation renders per frame (1-frame settle delay); bulk re-render of a large
  pool takes several frames — same as today.
