# Presets and Snapshots

Variations come in two flavours: **Presets** are parameter sets for operator symbols like [Blob] or [Layer2d]. **Snapshots** store the parameters of a group of operator instances in a composition — e.g. for switching scenes in a live performance.

## Previewing and applying

1. Click a thumbnail to apply it.
2. With **Preview on hover** enabled, hovering a thumbnail temporarily shows its result.
3. Hold the ALT key and hover to blend between variations. Select one, two or three thumbnails with a selection fence to control which ones participate in the blend.

## Live render previews

With **Live render previews** enabled, thumbnails are continuously rendered for the currently pinned output, so you can see how each preset would affect the final output further down the stream. These live thumbnails are temporary — disabling the option restores the default thumbnails.

Note: Thumbnail rendering interferes with effects that rely on feedback rendering, e.g. [AdvancedFeedback] or [SimpleLiquid]. In these cases disable live previews and hover preview.
