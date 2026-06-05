# Video clip player

**Status:** Drafted 2026-06-04. Design agreed; no code yet. Follows the FFmpeg playback work
([`Plan_FfmpegVideo.md`](Plan_FfmpegVideo.md)) which made frame-precise video playback cheap enough
to composite several clips on one timeline.

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

### Phase 3 — Efficiency, lifecycle, export hardening

**Goal:** off-screen clips cost nothing; export is frame-exact for every active clip.

**Scope:**

- Wired set: feed active indices into `Clips.LimitMultiInputInvalidationToIndices` (Switch pattern) so
  inactive branches don't invalidate/decode.
- Scanned set: skip inactive clips before pulling them (already implied by the active-filter).
- Thread `Playback.OpNotReady |= !IsReady` per active clip so export waits for every contributing clip.
- **Introduce the decode pool** (see *Decode pool, preroll & eviction*): clips become descriptors; the
  player owns N pooled controllers with temporal scheduling, preroll of upcoming clips, and eviction of far
  ones. This is the core mechanism for many-clip timelines, not an optional memory tweak — per-clip
  controllers are kept only as the few-clips fallback.

**Testable outcome:** A 20-clip timeline with 1–2 active at a time holds steady decode/memory; export
of a crossfade is frame-exact on both clips.

**Effort:** ~1 day.

### Phase 4 — Naming, docs, tests, deprecation

**Scope:**

- Finalise input naming (`AutoCollect` vs `IncludeTimelineClips` — "Auto play" reads like a transport
  toggle; pick the clearer one before the op ships and locks its input set).
- `.help/` page for the timeline-video-clips workflow; cross-link from the video docs.
- Manual test set (below) under `.tests-manual/`.
- Decide the fate of any example projects using `[PlayVideoClip]` — they keep working via the retained
  Guid; update bundled examples to the new name on next save.

**Effort:** ~0.5–1 day.

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

- `video-clip-player-wired.md` — two wired clips, overlap composite, layer order, scrub, export.
- `video-clip-player-autocollect.md` — unwired clips + `AutoCollect`, continuous cuts, toggle off,
  wired+scanned dedup.
- `video-clip-player-efficiency.md` — many clips, few active; decode/memory steady; crossfade export
  frame-exact.
