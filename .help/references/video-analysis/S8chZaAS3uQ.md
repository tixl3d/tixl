---
video: S8chZaAS3uQ
type: meetup
date: 2026-02-24
title: TiXL Meetup 2026-02-09 / Part 1 (Patching)
duration: 0:31:27
---

A casual patching meet-up that opens by improvising a glowing title effect with [DirectionalBlur], then turns into a live walkthrough of the new thumbnail-manager system (texture atlas, per-symbol curated thumbnails) and a hot-reload coding session inside the MagGraph node-drawing code to make nodes show a stored thumbnail when they have no live texture.

## Mentions
- 0:44→3:20 [DirectionalBlur] · explained · experiment · Example · 80% — Layered over a title so the un-blurred original stays readable underneath, it reads almost like motion blur; driving its amount with a very slow animated value gives a gentle drifting smear.
- 1:18→1:41 [ui:AnimationArea] · passing · experiment · Tip · 55% — A slow looping animated value can be wired straight into an effect's strength to make the look breathe over time rather than sitting static.
- 6:18→8:09 [ui:AssetLibrary] · passing · discussion · Concept · 60% — Why a single central thumbnail pipeline matters: every place that needs a preview image pulls from one renderer instead of each view computing its own.
- 8:09→11:18 [ui:Asset] · explained · experiment · Concept · 75% — How on-demand previews work: hovering an image generates its thumbnail on the first frame, then it is cached so the next hover is instant; thumbnails are written as small alpha-PNGs to a temp folder so deleting them just forces regeneration.
- 11:18→12:46 [ui:Asset] · explained · experiment · Performance · 75% — Previews share one large GPU texture atlas with a fixed slot count (~500); when full it evicts the least-recently-used slot, so the live preview cost stays bounded no matter how many images you browse.
- 12:46→13:55 [ui:Asset] · explained · experiment · Performance · 70% — A cached small PNG loads far faster than decoding the full-resolution source, so the preview path checks for an existing thumbnail before ever touching the original image.
- 13:55→14:42 [Bloom] · passing · experiment · Tip · 50% — Used as the example operator when curating a thumbnail, showing the "set thumbnail" workflow applies to any image-output effect, not just project compositions.
- 14:42→16:02 [ui:SymbolLibrary] · explained · discussion · Concept · 65% — Curated library-operator thumbnails live in a versioned "meta" folder beside the operator (committed with the repo), separate from the throwaway per-project previews in the temp folder.
- 16:02→17:02 [ui:Symbol] · explained · experiment · Tip · 75% — Right-click an operator and "set thumbnail" to freeze its current main-output view as a curated preview; pin a sub-output first to choose exactly which image becomes the thumbnail rather than whatever evaluates by default.
- 17:02→18:30 [ui:Symbol] · passing · experiment · Gotcha · 60% — A set thumbnail only shows up once the symbol also has a description, so an operator with no description appears to "lose" the thumbnail you just assigned.
- 18:30→21:06 [LoadImage] · passing · experiment · Concept · 55% — Some operators expose a custom node UI instead of a plain preview, which is why an image loader can surface its file path on the node face rather than a thumbnail.
- 21:40→23:35 [ui:Graph] · in-depth · experiment · Concept · 80% — The node-drawing routine is large because one method decides, per node, which connection lines are visible, whether to show a dot or a thumbnail or labels, and whether corners round — all the per-node visual state lives in that single draw method.
- 23:35→25:01 [ui:Graph] · explained · experiment · Concept · 70% — Custom node UIs are checked first; only when a node has none does the drawing fall through to the generic thumbnail/preview path.
- 25:10→26:02 [ui:Graph] · explained · discussion · Performance · 70% — Node previews are deliberately dropped below a zoom threshold to save draw calls, which is what lets very large graphs stay responsive when fully zoomed out.
- 25:47→27:05 [ui:Graph] · explained · experiment · Gotcha · 70% — Before drawing a node's live preview the code bails out on several conditions — no instance, no outputs, a non-texture first output, or a null/invalid texture — each a guard you must satisfy to reach the actual image draw.
- 27:05→31:27 [ui:Graph] · in-depth · experiment · Example · 80% — With hot code reload you can edit the live node-draw method and, when a node has no current texture, fall back to a stored thumbnail looked up by the symbol's id and package — the returned rect reports whether the thumbnail is ready before you try to draw it.
