# Video clip player

**Status:** Updated 2026-06-07. **Phases 1 and 2 are done.** The `VideoClipPlayer` symbol op is wired and in
use: `PlayVideoClip` is renamed to `VideoClip`; the `_ProcessVideoClips` helper does active-set filtering,
`LayerIndex` ordering, per-clip `Color`/`BlendMode`, an interim forward **preroll**, and — with `AutoCollect`
on — scans + drives unwired sibling `VideoClip`s. See *Phase-1 implementation — settled* below (which
**supersedes the "player composites in C#" mechanism** in *Architectural decisions*) and the *Status: DONE*
blocks under *Phase 1* and *Phase 2*. Next: Phase 3 (efficiency / export hardening) and Phase 4 (docs/tests).
Follows the FFmpeg playback work ([`Plan_FfmpegVideo.md`](Plan_FfmpegVideo.md)).

## Goal

A single **`VideoClipPlayer`** operator produces **one continuous `Texture2D`** by compositing the
video clips that are active at the current playback time, stacked **bottom→top by `LayerIndex`**. It
draws from **two sources at once**:

1. **Wired clips** — `VideoClip` ops connected into the player (idiomatic, like [`Group`](../../Operators/Lib/render/transform/Group.cs)).
2. **Auto-collected clips** — sibling `VideoClip` ops in the same composition, discovered by scanning
   (no wires), gated by an `AutoCollect` input (the "Auto play" idea), like
   [`GetAllSpatialAudioPlayers`](../../Operators/Lib/io/audio/_/GetAllSpatialAudioPlayers.cs).

This removes today's friction: every `[PlayVideoClip]` currently owns its own output `Texture2D`, so
stitching several cut clips into one continuous output means a manual `[Blend]`/`[Layer]` chain, and
off-screen clips keep decoding. The player owns the composite and only the active clips decode.

`[PlayVideo]` (the single-file, graph-driven op) is **out of scope** and unchanged — it covers the
"one video, programmatically scrubbed" case the way `[AudioPlayer]` covers graph-driven audio.

## Phase-1 implementation — settled (symbol op + `_ProcessVideoClips`)

**This supersedes the "player composites in C#" / `IVideoClipProvider` mechanism in *Architectural decisions*
below.** Reading the existing render ops settled it: `VideoClipPlayer` is a **symbol op** (a composition
graph), not a C# operator. The only new C# is one small helper, `_ProcessVideoClips`, modeled on
[`Loop`](../../Operators/Lib/flow/Loop.cs) — so there is **no hand-written low-level rendering**; the drawing
reuses `[RenderTarget]` + `[DrawScreenQuad]`. (Why the change: a C# player would mean writing blind D3D blit code I
can't runtime-verify; the symbol-op route reuses proven ops, and the user wires the graph.)

**The graph inside `VideoClipPlayer`** (wired in the editor):
```
VideoClipPlayer.Clips (MultiInput<Texture2D>) ─► _ProcessVideoClips.Textures
[UseTextureReference].Reference          ─► _ProcessVideoClips.TextureReference
[UseTextureReference].Texture            ─► [DrawScreenQuad].Texture
[GetForegroundColor].Color               ─► [DrawScreenQuad].Color        (per-clip tint + opacity)
[GetIntVar "VideoClip.BlendMode"].Result ─► [DrawScreenQuad].BlendMode    (per-clip blend; optional)
[DrawScreenQuad].Output (Command)        ─► _ProcessVideoClips.DrawCommand
_ProcessVideoClips.Output                ─► [RenderTarget].Command
[RenderTarget].ColorBuffer               ─► VideoClipPlayer.Output
```

**Texture-passing — the `[UseTextureReference]` / `RenderTargetReference` indirection.** A
[`RenderTargetReference`](../../Core/DataTypes/RenderTargetReference.cs) is a mutable holder with a settable
`ColorTexture`; [`UseTextureReference`](../../Operators/Lib/image/use/UseTextureReference.cs) outputs both the
holder (`Reference`) and its current `ColorTexture` (`Texture`). `_ProcessVideoClips` is the *provider*: each
iteration it writes the current clip's frame into the holder, then re-evaluates the draw subgraph so
`[UseTextureReference].Texture` resolves to that clip and `[DrawScreenQuad]` composites it. This is `Loop`'s
invalidate-then-evaluate, with a typed texture ref instead of `context.FloatVariables`. (`[RenderTarget]`
already drives the same ref the same way.)

**`_ProcessVideoClips` — built + committed**
([`Operators/Lib/io/video/_ProcessVideoClips.cs`](../../Operators/Lib/io/video/_ProcessVideoClips.cs), op Guid
`0162ddd9-4611-4a0a-b02f-8f68ded99cfb`):
- Slots: `Textures` `MultiInputSlot<Texture2D>` · `TextureReference` `InputSlot<RenderTargetReference>` ·
  `DrawCommand` `InputSlot<Command>` · `Output` `Slot<Command>`.
- `Update`: classify each connected clip by its `TimeClip` range vs. the playhead. **Active** clips are
  insertion-sorted by `LayerIndex` (descending → lowest layer on top), then for each: publish its per-clip
  `Color` to `context.ForegroundColor` and blend to `context.IntVariables["VideoClip.BlendMode"]`, set
  `reference.ColorTexture = texture`, and `DrawCommand.InvalidateGraph()` + `GetValue(context)`. **Upcoming**
  clips (within `PrerollSeconds` of their start) are pulled but not drawn, to warm their decoder. Inactive
  clips are skipped, so gaps keep the RenderTarget's (transparent) clear color.
- **Build gotcha:** it lives in `Lib/io/video/` (namespace `Lib.io.video`), *not* a `_/` subfolder — a `_`
  child namespace shadows the bare `_` discards (`out _`) the sibling video ops use and breaks the build. The
  `_`-prefixed name + `SymbolTags: "16"` keep it internal/hidden instead.

**Resolved in Phase 1 (these were the first-cut limitations):**
- **Active-set filtering — done.** Only clips whose `TimeClip.TimeRange` contains the playhead composite,
  reached via each texture slot's `Parent` → its `ITimeClipProvider` output. Exclusive end matches
  `TimeClipSlot`, so adjacent clips at a cut don't both draw.
- **Per-clip color + blend — done.** `VideoClip` carries a `Color` (Vector4, tint + alpha opacity) and a
  `BlendMode`; the player applies them per clip (see *Status: DONE* under Phase 1). Per-clip 2D *transform* is
  still the `ImageComposeTransform` / `TransformImage` story (below, and
  [`Plan_ImageComposeTransform.md`](Plan_ImageComposeTransform.md)).

## Why this is mostly assembly, not new infrastructure

Four existing mechanisms cover most of it:

- **Timeline discovery is automatic.** Any op whose output is a `TimeClipSlot` shows on the timeline —
  the editor scans children for `ITimeClipProvider` ([`Structure.GetAllTimeClips`](../../Editor/UiModel/ProjectHandling/Structure.cs#L140)).
  `VideoClip` keeps its `TimeClipSlot`, so clips appear and stack on layers with no extra work.
- **Active-set + source-time remap is free.** [`TimeClipSlot.UpdateWithTimeRangeCheck`](../../Core/Operator/Slots/TimeClipSlot.cs#L53)
  already no-ops a clip outside its `TimeRange` and remaps `LocalTime`/`LocalFxTime` from
  `TimeRange`→`SourceRange` for active clips. The manual remap in
  [`PlayVideoClip.cs:33-51`](../../Operators/Lib/io/video/PlayVideoClip.cs) becomes redundant.
- **Sibling scan has a precedent.** `GetAllSpatialAudioPlayers` walks `Parent.Children` for a marker
  interface — exactly the no-wires collection the `AutoCollect` path needs.
- **Composite-into-a-target has a precedent.** [`RenderTarget`](../../Operators/Lib/image/generate/basic/RenderTarget.cs)
  binds/clears a target and evaluates a sub-tree into it with full save/restore of context state;
  [`DrawScreenQuad`](../../Operators/Lib/render/basic/DrawScreenQuad.cs) blits a `Texture2D` into the
  bound target with a `BlendMode` and `Color`. The player reuses both.

The one genuinely new thing is the player **driving** the scanned clips' evaluation (Phase 2). The
wired path (Phase 1) is just `Group` specialised to video, so it carries no new risk.

## Architectural decisions (proposed — confirm before Phase 2)

> The **mechanism** here (C# player composites; `IVideoClipProvider`) is **superseded** by *Phase-1
> implementation — settled* above (symbol op + `_ProcessVideoClips` + `[UseTextureReference]`). The
> **behaviour** below — active-set filter, `LayerIndex` order, per-clip opacity/blend — still applies.

- **One player, one output.** `VideoClipPlayer` owns an internal render target and outputs its
  `Texture2D`. Single op, single output.
- **Two sources, one composite.** Wired (`MultiInput`) and scanned (`AutoCollect`) clips are merged
  into one list, **deduped by `SymbolChildId`** (a clip can be both wired and auto-collected),
  filtered to the active set (`TimeRange` contains `LocalTime`), and **sorted by `LayerIndex` only** —
  a single ordering rule across both sources. The timeline is the source of truth for stacking.
- **Player composites; clips stay simple (recommended).** `VideoClip` keeps producing a `Texture2D`
  and exposes `Opacity` / `BlendMode` / its `TimeClip` via an `IVideoClipProvider` interface. The
  player runs one `DrawScreenQuad`-style blend per active clip into its render target, reading params
  off the interface (so opacity/blend work identically for wired and scanned clips — they don't have
  to travel down a texture wire). *Alternative considered:* make each `VideoClip` a self-drawing
  `Command` (DrawScreenQuad internally) and let the player just bind the target and drive commands in
  order (pure `Group`). Simpler player, but the wired/scanned paths diverge and the player can't do
  cross-clip effects later. The Phase-1 prototype settles this; the plan assumes player-composites.
- **No new `EvaluationContext` instance.** The "separate context" is a save→mutate→evaluate→restore
  scope on the *current* context, as `RenderTarget`/`Group` already do. Let each clip's own
  `TimeClipSlot` perform the gating + source-time remap rather than hand-rolling it in the player —
  this avoids the known sharp edge noted at [`TimeClipSlot.cs:61`](../../Core/Operator/Slots/TimeClipSlot.cs#L61)
  ("setting local time should flag time accessors as dirty").
- **Driving is isolated to the scanned set.** Wired clips flow through normal `MultiInput`
  invalidation (zero new risk). Only auto-collected clips are force-evaluated by the player. Build
  order follows from this: wired first, scan behind the flag.
- **Decode pool, not per-clip controllers (for the many-clip case).** A handful of wired clips can keep
  their own `VideoPlaybackController` (Phase 1–2). But an NLE timeline with **hundreds of small clips**
  needs a **bounded pool of N live controllers owned by the player**, assigned by temporal relevance, with
  preroll + eviction — see *Decode pool, preroll & eviction* below. Crossfades just need ≥2 pool slots, not
  "one texture total."
- **Composition-scoped scan.** `AutoCollect` walks the player's own composition only (no nesting),
  matching the audio precedent.
- **Rename keeps the Guid.** `PlayVideoClip` → `VideoClip` preserves symbol Guid
  `04c1a6dc-3042-48a8-81d2-0a5a162016dc` so existing projects load. New inputs are additive.
- **A lone `VideoClip` with no player renders nothing.** Intended (like a `TimelineAudioClip` without
  the audio engine), but surface it as an operator status hint so it isn't mistaken for a bug.

## Decode pool, preroll & eviction (scaling to many clips)

The per-clip-controller model is fine for a few wired clips, but an NLE timeline can hold **hundreds of
small clips** with only a few visible at once. One `VideoPlaybackController` per clip would mean hundreds of
open demuxers, decoder contexts, worker threads, and textures — infeasible. The fix is a **single global
decode pool owned by the `VideoPlaybackEngine`** (see `Plan_FfmpegVideo.md` — *not* a per-player pool, since
two players plus PlayVideo must share one budget). The player is the **scheduler that feeds it**: because it
already scans every clip and knows each `TimeRange` plus the playhead — and, unlike a per-clip op, is *not*
gated by the pull-based graph — it can tell the engine which clips to warm *before* they're visible.

- **Clips are descriptors; the engine owns the controllers.** A `VideoClip` carries `{file, TimeRange,
  SourceRange, Opacity, BlendMode, Optimize-for}` but does not own a controller. The **engine's** pool holds
  **N live controllers** (~5–20, budgeted, shared across all players + PlayVideo) and assigns them to the
  clips the players ask for; a clip's `VideoTexture` is served by its assigned controller, or the last frame
  / a placeholder if it has none.
- **Temporal scheduling each frame.** From the playhead + direction/speed and every clip's `TimeRange`,
  classify clips as **active** (visible now → must hold a controller), **upcoming** (visible within a
  lookahead horizon → preroll), or **far** (evict).
- **Preroll = no seek delay on cut-in.** For an upcoming clip, acquire a controller early and **open + seek
  to its `SourceRange` in-point + decode the first GOP**, so the first frame is ready the instant the clip
  becomes visible. The lookahead horizon covers preroll latency (open + seek + first-GOP decode), widened by
  cut density (rapid cuts ⇒ preroll several clips ahead).
- **Eviction returns resources to the pool.** A clip that drifts far from the playhead releases its
  controller: close the demuxer/decoder (frees decoder memory + file handle), **park the worker thread** in
  a shared pool (don't tear down), and **return textures to a size-bucketed texture pool**. Re-acquiring
  later re-opens + re-prerolls — the cost the lookahead hides.
- **Budget by decode cost, not clip count.** A long-GOP H.264/HEVC decoder is the expensive resource; HAP /
  all-intra clips are cheap (codec note below). The pool budgets by *cost* — many cheap clips can be live
  while only a few expensive decoders are.
- **Graceful degradation when active > budget.** If more clips are simultaneously visible than the pool
  holds (e.g. a 30-layer composite), prioritize by layer/size/opacity and show last-frame (or a placeholder)
  for the overflow rather than thrashing decoders.
- **Pooled threads + textures, no per-clip spawn/alloc** — same no-per-frame-allocation discipline as the
  rest of the engine.

This supersedes "keep per-clip controllers" for the many-clip case; per-clip controllers remain the trivial
fallback for a few wired clips (Phase 1–2). The pool lands with the AutoCollect / many-clip work (Phase 3).

### Codec determines the decode path (and whether caching even helps)

Caching exists only to avoid **re-decoding a GOP** on a seek; that payoff is codec-dependent:

- **Long-GOP H.264/HEVC** — expensive seek ⇒ the RAM GOP-cache (pipeline A) earns its keep, or D3D11VA
  zero-copy (pipeline B) for forward throughput. The `Optimize for` param picks between them.
- **All-intra CPU codecs (ProRes, DNxHD, MJPEG, all-intra H.264)** — every frame is a keyframe ⇒ seeking is
  already cheap ⇒ **no GOP-cache**; decode the target frame on demand. The param mostly affects whether to
  use a hardware decoder for forward throughput.
- **HAP (and GPU-texture codecs: HAP Q/Alpha, NotchLC, DXV)** — all-intra **and** GPU-compressed (BC/DXT).
  **Always GPU→GPU**: read the chunk, Snappy-decompress (cheap, CPU), upload the BC texture, sample it. No
  swscale, no CPU RGBA decode, **no RAM cache** — caching decoded RGBA throws away HAP's whole point (its
  compressed form *is* the efficient GPU-resident form). Note FFmpeg's built-in `hap` decoder outputs full
  RGBA on the CPU, so a real HAP path takes the raw chunks and uploads BC directly — HAP is its own
  codepath, not the swscale pipeline. (HAP stays low priority per the original scope; captured here as the
  canonical "codec overrides the cache choice" case.)

So the effective pipeline = **(codec class) × (`Optimize for` intent)**: the param is the user's hint, but
the codec can override it — HAP / intra-GPU is always GPU regardless; long-GOP honors the intent.

## Compose params & `TransformImage` (the context convention)

Compose state — 2D transform, color/tint, opacity, blend, sampler — follows the standard TiXL context
convention, **the same one Scene `[Transform]` uses for 3D** (no new connection type):

- **`EvaluationContext` carries an `ImageComposeTransform` with a neutral, meaningful default** — identity
  matrix, color = 1, opacity = 1, normal blend. A leaf that does nothing special leaves it untouched.
- **`TransformImage` is the general subgraph modifier** (not video-specific): like Scene `[Transform]`, it
  **manipulates `context.ImageComposeTransform` for its subgraph's evaluation**, so every image / `VideoClip`
  in its subtree picks up the accumulated transform. This is how a wired clip gets an external / graph-driven
  transform.
- **`VideoClip` carries its own full compose-param set** (transform / color / opacity / blend / sampler) as
  animatable inputs. As a leaf it **reads** the current `context.ImageComposeTransform`, **combines** it with
  its own params, and **registers** `{frame, TimeClip, finalTransform}` for the player's blit — it does *not*
  write the context field; only transform ops do. With no ancestor `TransformImage` (context neutral) it uses
  only its own params — the everyday and **virtual (auto-collected)** path. Auto clips sit outside any
  `TransformImage` subtree, so they can only be transformed via their own params — exactly the earlier rule,
  now enforced by the mechanism rather than a special case.

This is **exactly Scene `[Transform]` for images** (an op transforms the contribution of its subgraph as it
composes further up), so the "transforms upward" quirk is idiomatic — no new mental model. The player
composites the registered set per `LayerIndex`, applying each clip's final transform in its single blit; the
per-clip param *interface* shrinks to a marker ("this child is a video clip") for the auto-collect scan.
`ImageComposeTransform` becomes a real `EvaluationContext` field (Core). Build-time detail: how the player
collects each clip's `{frame, TimeClip, transform}` (a context sink the clips register into, or a per-clip
reset→pull→read) — inactive clips no-op cheaply via `TimeClipSlot` gating, so pull-then-filter is fine.

**Scope for the video work:** only the video ops (`VideoClipPlayer` blit + `VideoClip` + `TransformImage`)
consume the field here — the video feature is *not* blocked on broad adoption. Extending it so **every**
texture-consuming op honors the context transform (UV transforms free everywhere, ~100+ ops) is a separate,
larger initiative — see [`Plan_ImageComposeTransform.md`](Plan_ImageComposeTransform.md). The neutral default
keeps the two decoupled: un-migrated ops simply ignore the (identity) context.

## Interface sketch

> **Superseded by the as-built (see *Status: DONE* under Phase 1).** The shipped `IVideoClipProvider` exposes
> the input **slots** (`ColorInput` / `BlendModeInput`), not values; `TimeClip`/`LayerIndex` come from the
> clip's `TimeClipSlot` output (`ITimeClipProvider`) rather than this interface, and the player pulls the
> texture from the multi-input slot, not a `VideoTexture` member. The original sketch is kept below for the
> Phase-2 scan rationale.

```csharp
// Implemented by VideoClip; consumed by VideoClipPlayer for both wired and scanned clips.
internal interface IVideoClipProvider
{
    TimeClip TimeClip { get; }          // TimeRange (active test) + LayerIndex (ordering)
    Slot<Texture2D> VideoTexture { get; } // player pulls this to make the clip decode its current frame
    float Opacity { get; }
    int   BlendMode { get; }            // SharedEnums.BlendModes, as DrawScreenQuad
}
```

`TimeClipSlot` already implements `ITimeClipProvider`, so for a wired clip the player can reach the
`TimeClip` from the collected slot's `Parent`; the marker interface adds the params and the
texture-pull entry point and lets the scan identify "this child is a video clip."

## Player update loop (target shape)

```
Update(context):
  bind + clear internal RT                              // RenderTarget.cs:99-135 pattern
  candidates = []
  candidates += map(Clips.GetCollectedTypedInputs() -> IVideoClipProvider)   // wired
  if AutoCollect:
      candidates += Parent.Children where child is IVideoClipProvider        // scanned
  dedup by SymbolChildId
  active = candidates where TimeClip.TimeRange contains context.LocalTime
  sort active by TimeClip.LayerIndex                    // bottom -> top, single rule
  for clip in active:
      tex = clip.VideoTexture.GetValue(context)         // drives decode (wired: idiomatic; scanned: forced)
      screenQuadBlend(tex, clip.Opacity, clip.BlendMode) into RT   // DrawScreenQuad shader path
      if context.Playback.IsRenderingToFile:
          Playback.OpNotReady |= !clipIsReady           // keep export frame-exact (see PlayVideo.cs:51)
  restore RT + context
  Output.Value = RT texture
```

## Current state — what exists

- [`Operators/Lib/io/video/PlayVideoClip.cs`](../../Operators/Lib/io/video/PlayVideoClip.cs) (+ `.t3`/`.t3ui`) —
  rename target. Outputs `Texture2D` + `TimeClipSlot<Command>`; owns a `VideoPlaybackController`.
- [`Video/VideoPlaybackController.cs`](../../Video/VideoPlaybackController.cs) — per-clip decode worker
  + texture upload + export-gated `IsReady`. Reused unchanged.
- [`Core/Operator/Slots/TimeClipSlot.cs`](../../Core/Operator/Slots/TimeClipSlot.cs) — gating + remap.
- [`Operators/Lib/render/transform/Group.cs`](../../Operators/Lib/render/transform/Group.cs) — wired
  collect + draw + opacity-via-`ForegroundColor` template (Phase 1).
- [`Operators/Lib/flow/Switch.cs`](../../Operators/Lib/flow/Switch.cs) —
  `LimitMultiInputInvalidationToIndices` (Phase 3 efficiency for the wired set).
- [`Operators/Lib/image/generate/basic/RenderTarget.cs`](../../Operators/Lib/image/generate/basic/RenderTarget.cs) —
  RT bind/clear/save-restore.
- [`Operators/Lib/render/basic/DrawScreenQuad.cs`](../../Operators/Lib/render/basic/DrawScreenQuad.cs) —
  texture→bound-RT blit with `BlendMode`/`Color`.
- [`Operators/Lib/io/audio/_/GetAllSpatialAudioPlayers.cs`](../../Operators/Lib/io/audio/_/GetAllSpatialAudioPlayers.cs) —
  sibling-scan-by-marker-interface template (Phase 2).
- [`Operators/Lib/io/video/PlayVideo.cs:51`](../../Operators/Lib/io/video/PlayVideo.cs#L51) — the
  export `OpNotReady` pattern to replicate per active clip.

## Phases

### Phase 1 — Rename + clip params + player in wired mode

**Goal:** `VideoClipPlayer` composites *wired* `VideoClip`s into one render target. Fully idiomatic;
no scanning, no force-evaluation. Ship-able on its own.

**Scope:**

- Rename `PlayVideoClip` → `VideoClip`: class + `.cs`/`.t3`/`.t3ui` filenames; **keep Guid**
  `04c1a6dc-…`. Update the `/*PlayVideoClip*/` name comments (regenerated on save). Drop the now-dead
  manual remap (lines 33-51) and lean on `TimeClipSlot`.
- Add `VideoClip` inputs: `Opacity` (float, default 1), `BlendMode` (int, `SharedEnums.BlendModes`).
  Additive — no migration.
- Add `IVideoClipProvider` (interface above) and implement it on `VideoClip`.
- New `VideoClipPlayer` op: `MultiInputSlot<Texture2D> Clips`, `bool AutoCollect` (default **off** in
  this phase), `Slot<Texture2D> Output`. Update loop above with the *wired* branch only; composite via
  the `DrawScreenQuad` shader path into an internal RT (model the RT lifecycle on `RenderTarget`).
- Resolution: default to `context.RequestedResolution`; optional `Resolution` input later.

**Testable outcome:** Place two `VideoClip`s at overlapping/adjacent `TimeRange`s, wire both into a
`VideoClipPlayer`. Output shows the active clip(s); at an overlap both composite with their `Opacity`/
`BlendMode`; stacking follows `LayerIndex`. Scrubbing/export render the right frames. A single wired
clip behaves like today minus the manual remap.

**Effort:** ~1–1.5 days. The real work is the player's RT + per-clip blend; the rename is mechanical.

**Status: DONE (2026-06-07) — as built, with deviations from the scope above:**
- `PlayVideoClip` → `VideoClip` (Guid kept `04c1a6dc-…`); the manual source-time remap is dropped in favor of
  `TimeClipSlot`.
- **The per-clip param is `Color` (Vector4, default white), not a separate `Opacity` float.** It carries tint
  *and* opacity (alpha) in one parameter and maps 1:1 onto `context.ForegroundColor`. Plus `BlendMode` (int,
  `SharedEnums.BlendModes`, default `Normal`). Both are additive inputs on `VideoClip` (new Guids).
- `IVideoClipProvider` exposes the input *slots* as `ColorInput` / `BlendModeInput` (the `…Input` suffix
  convention, cf. `ITransformable`); the player reads each active clip's values through it.
- **Conveyance:** per-clip `Color` rides `context.ForegroundColor` → `[GetForegroundColor]` →
  `DrawScreenQuad.Color` (no string var, no `Vector4` construct op). Per-clip blend rides
  `context.IntVariables["VideoClip.BlendMode"]` → `[GetIntVar]` → `DrawScreenQuad.BlendMode` (optional wiring;
  unwired ⇒ `Normal`). The helper restores `ForegroundColor` after the loop.
- **`LayerIndex` order is descending** (lowest layer composites last → on top), stable by multi-input
  connection order for ties.
- **Interim forward preroll** (`PrerollSeconds = 0.5`, timeline-seconds): a clip within that window before its
  start is pulled-but-not-drawn to warm its decoder, so its first frame is ready at the cut instead of a
  transparent blink. Caveats: forward play only; very fast playback may still blink; scrub/seek jumps still
  cold-start. Player-level stand-in for the engine-level direction-aware scheduler (Phase 3 /
  `Plan_FfmpegVideo.md`).
- **Not added:** the `AutoCollect` input (Phase 2) and a `Resolution` input — RT defaults to
  `context.RequestedResolution`. Don't ship a dead `AutoCollect` input before its scan exists.

### Phase 2 — `AutoCollect` (scan + drive)

**Goal:** with `AutoCollect` on, the player also composites unwired sibling `VideoClip`s.

**Scope:**

- Scan `Parent.Children` for `IVideoClipProvider` (guard `Parent == null`, log once like
  `GetAllSpatialAudioPlayers`).
- Merge with the wired list; dedup by `SymbolChildId`; same active-filter + `LayerIndex` sort.
- **Drive** each scanned clip's `VideoTexture.GetValue(context)` inside the save/restore scope. This
  is the new bit — validate that forced evaluation re-decodes correctly per frame and doesn't
  double-evaluate a clip that is also reachable elsewhere.
- Status hint on a `VideoClip` that no player is currently drawing.

**Testable outcome:** Drop several `VideoClip`s in a composition (no wires) + one `VideoClipPlayer`
with `AutoCollect` on → continuous playback across cuts; toggling `AutoCollect` off falls back to
wired-only; a clip both wired and scanned draws once.

**Effort:** ~1–2 days, mostly de-risking the forced evaluation. If it fights the dirty-flag system,
Phase 1 (wired) is unaffected.

**Status: DONE (2026-06-07) — as built:**
- The scan lives in `_ProcessVideoClips` (the helper inside the `VideoClipPlayer` symbol), so the composition
  is `Parent.Parent.Children` — the helper's grandparent — not `Parent.Children`. Guarded (`Parent?.Parent`).
  Gated by a new `AutoCollect` bool input on `_ProcessVideoClips`, wired from the symbol's `AutoCollect`.
- **Forced evaluation needs no invalidate.** `VideoClip.Texture` is `DirtyFlagTrigger.Animated`, and
  `DirtyFlag.IsDirty` is `TriggerIsEnabled || …`, so the slot is always dirty → a plain
  `provider.TextureOutput.GetValue(context)` re-decodes every frame for an unwired sibling. (`TextureOutput`
  was added to `IVideoClipProvider` for this.) The dirty-flag fight the open question feared didn't materialise.
- Wired + auto-collected clips merge into **one** active list, deduped by `SymbolChildId` (recorded in a reused
  `HashSet`), insertion-sorted by `LayerIndex`, then composited once. Reused `List`/`HashSet`, no per-frame
  allocation. Preroll applies to both sources.
- **Status hint — done (2026-06-07).** A `VideoClip` no player is drawing returns a `Notice` ("Not drawn by
  any [VideoClipPlayer] — wire it into one or enable AutoCollect"). The player stamps `Playback.FrameCount` on
  every clip it visits (wired + scanned, including inactive-in-gap ones) via `IVideoClipProvider.MarkManaged`;
  a clip unstamped for >2 frames hints. Tied to evaluation (the player must be in the rendered graph to stamp).
- **Still just documented, not handled:** multiple `AutoCollect` players in one composition each draw all
  clips (open question 2).

### Phase 3 — Efficiency, lifecycle, export hardening

**Goal:** off-screen clips cost nothing; export is frame-exact for every active clip.

**Scope:**

- Wired set: feed active indices into `Clips.LimitMultiInputInvalidationToIndices` (Switch pattern) so
  inactive branches don't invalidate/decode.
- Scanned set: skip inactive clips before pulling them (already implied by the active-filter).
- **Done (2026-06-07):** `Playback.OpNotReady |= !IsReady` per active clip — set in `VideoClip.Update`, gated
  by `IsRenderingToFile` *and* the clip being active, so pre-warmed upcoming clips don't stall export.
- **DONE (2026-06-09) — cache the AutoCollect sibling scan.** `_ProcessVideoClips` no longer walks
  `Parent.Parent.Children.Values` every frame. The structure-version signal was promoted to Core as
  **`Symbol.VersionCounter`** (mirrors `SymbolUi.VersionCounter`, forwarded in `SymbolUi.FlagAsModified`, which
  every child add/remove/copy-paste calls). The helper caches the sibling-clip list (child `Instance`s that are
  `IVideoClipProvider`) and rebuilds only when the composition instance changes (focus / hot-reload) or its
  `Symbol.VersionCounter` bumps. In the Player there's no `SymbolUi` so the mirror stays 0 and the static graph
  builds once. (Shared with the audio system — see `Plan_AudioClipPlayer.md` open question #4, which drove the
  Core counter.)
- **Introduce the decode pool** (see *Decode pool, preroll & eviction*): clips become descriptors; the
  player owns N pooled controllers with temporal scheduling, preroll of upcoming clips, and eviction of far
  ones. This is the core mechanism for many-clip timelines, not an optional memory tweak — per-clip
  controllers are kept only as the few-clips fallback. *(An interim, player-level forward preroll already
  ships in `_ProcessVideoClips` — `PrerollSeconds` look-ahead, warm-by-non-drawn-pull; the engine-level,
  direction-aware scheduler here supersedes it, and should also own the `PrerollSeconds`-equivalent horizon
  as a project setting alongside `MaxLiveStreams`.)*

**Testable outcome:** A 20-clip timeline with 1–2 active at a time holds steady decode/memory; export
of a crossfade is frame-exact on both clips.

**Effort:** ~1 day.

**Status (2026-06-07) — substantially met by earlier work + the engine, not a dedicated push:**
- **Done:** export frame-exactness (`OpNotReady`, above); off-screen clips never decode (the active-set filter
  skips them); bounded live decoders + idle eviction + shared cache budget already ship in
  `VideoPlaybackEngine` (`MaxLiveStreams`, `EvictStaleStreams`, `RedistributeBudget` — the "decode pool"
  core); interim forward preroll. The 20-clip-steady testable outcome should largely pass today.
- **Remaining (optimization-at-scale / deferred, not core):** wired-set `LimitMultiInputInvalidationToIndices`
  (niche — only helps many *wired* clips; the many-clip case is AutoCollect, which bypasses the multi-input);
  the AutoCollect scan cache (deferred above, needs the Core structure-version counter); folding the interim
  preroll + existing engine pool into the fuller engine-level descriptor/scheduler (a refactor, low marginal
  value now). The one real rough edge at scale is the per-frame AutoCollect scan allocation.

### Phase 4 — Naming, docs, tests, deprecation

**Scope:**

- Finalise input naming (`AutoCollect` vs `IncludeTimelineClips` — "Auto play" reads like a transport
  toggle; pick the clearer one before the op ships and locks its input set).
- `.help/` page for the timeline-video-clips workflow; cross-link from the video docs.
- Manual test set (below) under `.tests-manual/`.
- Decide the fate of any example projects using `[PlayVideoClip]` — they keep working via the retained
  Guid; update bundled examples to the new name on next save.

**Effort:** ~0.5–1 day.

**Status (2026-06-07) — partly done:**
- **Manual tests added:** [`video-clip-player-wired.md`](../../.tests-manual/video-clip-player-wired.md) and
  [`video-clip-player-autocollect.md`](../../.tests-manual/video-clip-player-autocollect.md). The
  `video-clip-player-efficiency.md` (many-clip decode/memory) set is **deferred with Phase 3** — it exercises
  the decode-pool scaling that isn't fully built yet.
- **Operator descriptions:** drafted for `[VideoClipPlayer]` and `[VideoClip]`; these live in each operator's
  Description field (edited in the TiXL editor), which regenerates the `.md` pages — so they're entered in the
  editor, not hand-written into `.help/docs/`. (The current `VideoClip.md` / orphaned `PlayVideoClip.md` are
  stale auto-generated stubs that refresh on the next docs regeneration.)
- **Pending:** input-name decision (`AutoCollect` currently in place — keep, or rename to
  `IncludeTimelineClips`, before user projects lock it); a hand-written workflow/guide page under
  `.help/docs/using/`; sweeping bundled examples off the old `[PlayVideoClip]` name.

## Open questions / deferred

1. **Audio.** Video clips usually carry audio; it's currently muted (BASS routing is backlog, see
   `PlayVideoClip` `Volume` note). This is the same "timeline of clips" problem the audio system
   already solved ([`TimelineAudioClip`](../../Core/Audio/TimelineAudioClip.cs) +
   [`AudioEngine`](../../Core/Audio/AudioEngine.cs) per-clip streams + stale detection). When audio is
   wired up, coordinate per-clip video audio through `AudioEngine` rather than building a parallel
   system — deliberate overlap, not duplication.
2. **Multiple players with `AutoCollect` in one composition** would each draw all clips. Document, or
   scope a player to a layer range, or treat 2nd+ as an error hint.
3. **Forced-evaluation correctness** (Phase 2): the dirty-flag/eval-order implications of a player
   driving siblings — the item to prototype before committing to `AutoCollect`.
4. **Compositing choice** (player-composites vs clip-self-draws `Command`): assumed player-composites;
   confirm with the Phase-1 prototype.
5. **Crossfades** need ≥2 live decoders — the pool reserves ≥2 slots at a cut/transition so the outgoing
   and incoming clips both stay warm (drives the minimum pool budget).
6. **Resolution / aspect handling** when clips differ in size (fit/cover/stretch, like `[Blend]`'s
   `ScaleMode`). Phase 1 can stretch; expose a mode later.

## Manual test sets (Phase 4)

- `video-clip-player-wired.md` — two wired clips, overlap composite, layer order, scrub, export. **Added.**
- `video-clip-player-autocollect.md` — unwired clips + `AutoCollect`, continuous cuts, toggle off,
  wired+scanned dedup. **Added.**
- `video-clip-player-efficiency.md` — many clips, few active; decode/memory steady; crossfade export
  frame-exact. **Deferred with Phase 3** (decode-pool scaling not fully built).
