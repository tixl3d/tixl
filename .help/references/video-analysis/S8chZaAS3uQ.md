---
video: S8chZaAS3uQ
type: meetup
date: 2026-02-09
title: Part 1 — Thumbnail manager feature dev, release tweaks, intro patching
duration: 31:27
---

Part 1 of a TiXL community meet-up where the host opens by tweaking an intro/title animation while waiting for attendees, then spends most of the session walking through the new thumbnail-manager system — how thumbnails are cached as PNGs in a temp folder, packed into one ImGui atlas texture, set per-operator/preset, and stored with the library. The latter half is a live coding session in the editor's graph-node draw code, wiring the thumbnail manager into operator node previews via hot-reload. Operators are barely the focus; only a few are named in passing while demonstrating thumbnail behaviour.

## Mentions
- 1:31 [Wave] · passing — host floats using a wave to slowly animate the intro/title text along a direction
- 11:42 [LoadImage] · passing — used as the example node whose image generates a thumbnail on hover into the atlas texture
- 13:07 [Mix] · passing — "mixed alpha" sequence example shown to demonstrate alpha-channel thumbnails (ASR-ambiguous)
- 14:30 [Bloom] · passing — right-click "set thumbnail" demonstrated on a Bloom preset; noted it has no auto thumbnail yet
- 17:54 [RenderTarget] · passing — host notes the thumbnail source needs to be a render target / image output, can be transparent
- 20:36 [Bloom] · passing — revisited; preset thumbnails need to be saved at preset-creation time
- 20:36 [DetectEdges] · passing — named alongside Bloom as another preset whose thumbnail should be auto-generated
