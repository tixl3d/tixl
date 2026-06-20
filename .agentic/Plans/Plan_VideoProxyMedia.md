# Video Proxy Media (background transcode for fast seeking)

**Status:** Draft — 2026-06-19. Design only, no code yet. The "background optimizer / `-optimized.mp4`"
pulled out of [`Plan_FfmpegVideo.md`](archive/Plan_FfmpegVideo.md)'s backlog into its own design, now that it has
real UX (a prompt, a Generate/Use state machine, settings).

## Goal

When a user adds a **long-GOP H.264/HEVC** video, TiXL offers to transcode it in the background to an
**all-intra proxy** (ProRes / HAP / …), and once ready the `VideoPlaybackEngine` **auto-switches** playback
to the proxy so scrubbing and random seeking are cheap. The original is always retained; proxies are a
preview/authoring optimization.

## Relationship to the other video plans

- **Built on the encode milestone.** A proxy is a transcode (decode source → encode proxy), so it **reuses
  the tier-1 LGPL encoder** from [`Plan_FfmpegEncode.md`](archive/Plan_FfmpegEncode.md) and **cannot ship before it
  exists**. One encoder, two consumers (render-export + proxy generation).
- **Substitution lives in the engine.** [`Plan_FfmpegVideo.md`](archive/Plan_FfmpegVideo.md)'s `VideoPlaybackEngine`
  already owns the "substitute proxies transparently" role; auto-switch is its job.
- **Supersedes the M2 RAM cache for the proxied clip** (see *Cache supersession*) — they're two solutions
  to the same problem.

## Licensing: proxies are GPL-free (no popup)

The proxy popup is a **performance** prompt, never a licensing one. Proxies use **LGPL all-intra** codecs —
ProRes, DNxHR, MJPEG, HAP — all native/LGPL in the bundled build. **`ProxyFormat` deliberately excludes
H.264/HEVC**: software H.264 is `libx264` (GPL), so an H.264 proxy would drag the GPL gate back in for a
preview file. (HAP needs `libsnappy` in the build — shared verify item with the encode plan.)

## State machine (`GenerateProxyVideos` × `UseProxyVideos`)

`GenerateProxyVideos` is tri-state; `UseProxyVideos` toggles preview substitution:

| Generate | Use (preview) | Behavior |
|---|---|---|
| **Ask** | — | **Undefined** → prompt on first long-GOP import; remember the answer |
| On | On | Background-transcode, engine **auto-switches** preview to the proxy when ready |
| On | Off | **Pre-bake** proxies (e.g. before enabling use, or for a teammate) but keep previewing the source *(semantics — confirm)* |
| Off | On | Use proxies that already exist; don't transcode new ones |
| Off | Off | Ignore proxies entirely |

The prompt is a **convenience** dialog (yes / no / always / never), visually distinct from the encode
milestone's GPL-install dialog — different verb (speed-up vs install), different urgency.

## Settings — where each value lives

**Decision (2026-06-20): proxy config is PROJECT-level, not per-machine.** The project defines which proxies it
wants (format/scale/auto) so the choice is consistent across machines/teammates; whether *this* machine can
generate is the orthogonal encoder-availability question. (Supersedes the original per-machine `UserSettings`
lean below.)

| Setting | Home | Default | Note |
|---|---|---|---|
| `GenerateProxyVideos` (Off/Ask/On) | **Project** (`.t3ui` / `RenderSettings`-adjacent) | **Ask** | auto-gen derives from the *project's* proxy settings |
| `ProxyFormat` (ProRes/HAP/DNxHR/MJPEG) | **Project** | ProRes 422 | never H.264/HEVC (GPL) |
| `ProxyResolution` (100/50/25 %) | **Project** | 50 % | scaled proxy = preview-only |
| `UseProxyVideos` (interactive preview) | **Project** *(open: Core if the player uses proxies — see Risks)* | true | preview only |
| `UseProxiesForRendering` | **`RenderSettings`** (project `.t3ui`) | **false** | opt-in draft render from the proxy; default export uses the source |

**Phase-1 interim:** `UserSettings.ProxyFormat` / `ProxyResolution` were added per-machine to get manual
generation working. **Migrate them to a project setting** when the state machine + UI land (Phase 3).

## Generation (the `OptimizerService`)

- **Auto-trigger (the goal):** a video is added/first opened whose codec is **long-GOP H.264/HEVC** with a
  keyframe interval past a threshold → generate per the **project's** proxy settings. **Skip** already-cheap
  sources — all-intra (ProRes/DNxHD/MJPEG), HAP/GPU-texture, and short-GOP files don't need a proxy.
  *(Threshold — propose "max keyframe interval > ~2 s" i.e. GOP > ~2×fps; tune.)*
- **Manual trigger — the Assets Library window** *(decision 2026-06-20)*. The asset lib already lists the
  project's media; per-asset "Generate proxy" + status belongs there, not (only) the graph right-click menu.
  The Phase-1 graph-node menu item stays as a quick path, but the canonical manual home is the Assets Library.
- **Editor-only background job:** a queue transcoding **one** source at a time (throttled, low priority,
  cancellable, progress surfaced). The transcode is a **pure file→file** decode→encode (no render pipeline),
  so it can run on a `T3.Video` worker reusing the bundled LGPL libs, driven by an editor-side service. The
  player never generates.
- **Idempotent:** skip if a valid matching proxy already exists (see identity); resume/skip across sessions.

## Proxy file identity & storage

Both `ProxyFormat` and `ProxyResolution` are user-changeable, so a proxy must be self-describing and the
engine must know whether what's on disk matches:

- **Container varies by codec** (HAP → `.mov`/`.avi`, ProRes/DNxHR → `.mov`) — a single `-optimized.mp4`
  name won't do.
- **Storage:** an AppData (or project-local) **proxy cache keyed by source path+hash**, with a sidecar
  recording `{sourceHash, sourceModified, format, scale}`. Preferred over a sibling `foo.proxy.*` file
  because **source folders may be read-only** (network / USB / shared assets). *(Sibling vs cache dir —
  confirm.)*
- **Invalidation:** source mtime/size/hash change → proxy stale → regenerate (or ignore).
- **Settings changed after a proxy exists:** **use-what-exists** (don't silently regenerate); offer an
  explicit **Regenerate** action.

## Playback substitution (engine; export-aware)

On stream open the engine picks **proxy vs source**:

- **Preview / realtime / scrub:** if `UseProxyVideos` and a valid proxy exists → open the **proxy**; the
  operator is unaware. A scaled proxy means the `PlayVideo` output texture is smaller — intended for preview
  speed; downstream ops process at proxy resolution.
- **Render-to-file:** use the **source** unless `UseProxiesForRendering` is on (then the proxy, yielding a
  draft at the proxy's resolution). Rides the existing `IsRenderingToFile` / export-gate signal that
  `PlayVideoClip` already consults.

## Cache supersession (why proxies skip M2)

A proxy is **all-intra/HAP** — exactly the codec classes the M2 cache is **already gated to skip**
([`Plan_FfmpegVideo.md`](archive/Plan_FfmpegVideo.md): all-intra is decode-on-demand with no GOP-cache; HAP bypasses
the cache entirely). So **no new special-case is needed** — the instant the engine switches a stream to a
proxy, the codec class flips and the RAM cache is skipped automatically. Stated as policy:

> **Proxy and the RAM GOP-cache are mutually-exclusive solutions to the same problem.** An active proxy
> supersedes the cache, freeing that clip's slice of the shared cache budget for the clips that *aren't*
> proxied, and overriding the cache half of `Optimize for: Fast Seeking`.

Proxy (persistent disk, survives restart, helps draft export) and cache (in-session RAM) are otherwise
**complementary** across a session.

## Phasing (build-verifiable)

1. **Generation core.** Split:
   - **1a. Transcoder — DONE & TESTED.** [`VideoServices/ProxyTranscoder.cs`](../../VideoServices/ProxyTranscoder.cs)
     `Generate(source, proxyPath, codec, scale, progress, cancel)` — opens a software `VideoDecoderSession`,
     scales+converts each YUV frame to RGBA at the proxy size in one swscale pass, and feeds the **existing
     render-export encoder** (`FfmpegVideoEncoderFactory.TryCreateWriter`) → all-intra proxy. CPU-byte-level (no
     D3D, no render pipeline), background-thread-safe, cancellable, progress, deletes partial output on
     error/cancel. **Video-only** (no audio in the preview proxy). Verified by
     [`ProxyTranscoderTests`](../../VideoServices.Tests/ProxyTranscoderTests.cs): a real long-GOP H.264 → half-res
     ProRes proxy decodes back as a smaller playable video (HAP skips on the no-hap test build). *Caveat:* the
     YUV→RGBA decode uses swscale's BT.601 default, so a 709 source picks up a slight preview-only colour drift
     in the proxy — acceptable (proxies are preview; render uses the source) but a later refinement is to match
     the decode matrix to the source tag.
   - **1b. Facade + editor service + settings + manual trigger — DONE (build-verified; in-editor verify
     pending).** The transcode entry is on the existing encoder facade — `IVideoEncoderFactory.GenerateProxy`
     ([`Core/Video/VideoExport.cs`](../../Core/Video/VideoExport.cs), impl in
     [`FfmpegVideoExport.cs`](../../VideoServices/FfmpegVideoExport.cs) delegating to `ProxyTranscoder`) — reusing
     the existing registration (no new holder). The editor's
     [`ProxyGenerationService`](../../Editor/Gui/Windows/RenderExport/ProxyGenerationService.cs) is a one-at-a-time
     background queue with per-source status/progress that resolves the factory (module-initializer nudge) and
     calls `GenerateProxy`. **Decisions made:** storage = **sibling file** (`clip.mp4` → `clip.proxy.mov`); proxy
     params = `UserSettings.ProxyFormat` (**ProRes** default; never H.264/HEVC) + `ProxyResolution` (**0.5**
     default); trigger = a **"Generate proxy video"** item in the graph **right-click menu** for an op whose
     file-path input points at a video (`GraphContextMenu` + `SymbolAnalysis.TryGetFileInputFromInstance` +
     `AssetRegistry.TryResolveAddress`), with a state-aware label (queued / generating % / regenerate). *Note:
     Phase 1 only **generates** the file — playback still uses the source until Phase 2's engine substitution.*
   *Verify (in-editor): right-click a `[PlayVideo]` → Generate proxy → a `*.proxy.mov` appears next to the source
   and re-imports/plays.*
2. **Engine substitution + `UseProxyVideos`.** Engine opens the proxy for preview when present; export uses
   the source. *Verify: scrub uses the proxy (measurably faster seek); render-to-file is full-res source.*
3. **Auto-trigger + state machine + Ask popup.** Prompt on long-GOP import; the Generate/Use matrix; the
   keyframe-interval threshold; skip intra/HAP sources. *Verify: a long-GOP H.264 import prompts; a ProRes
   import does not; the choice is remembered.*
4. **`UseProxiesForRendering`** `RenderSetting` (default false) — opt-in draft render from the proxy.
   *Verify: off → full-res source; on → draft at proxy resolution.*
5. **Polish.** Regenerate-on-settings-change, queue/progress UI, source-change invalidation, per-clip format/
   scale overrides, proxy-cache disk budget + LRU cleanup.

## Risks / open questions

- **`ProxyFormat` / `ProxyResolution` defaults** — propose ProRes 422 Proxy @ 50% (balanced size/quality);
  HAP for best scrub at a disk cost.
- **Keyframe-interval threshold** for offering a proxy — tuning.
- **"Generate-only" (On/Off) semantics** — propose pre-bake without switching preview; confirm.
- **Does the player ship/use proxies?** Decides `UseProxyVideos`'s home (`UserSettings` if editor-only —
  the v1 lean — vs a Core project setting if the player should seek fast too). Editor-only keeps exports lean.
- **Downscaled proxy changes preview resolution downstream** — desired for speed, but flag ops that assume
  source dimensions.
- **Disk usage** — intra proxies can exceed the source size; needs a proxy-cache budget + cleanup (Phase 5).
- **Storage location** — AppData cache (handles read-only source dirs) vs sibling file; confirm.

## Key files

| Concern | File |
|---|---|
| Transcoder (DONE — decode→encode file→file) | `VideoServices/ProxyTranscoder.cs` |
| Transcode facade (DONE — `IVideoEncoderFactory.GenerateProxy`) | `Core/Video/VideoExport.cs`, `VideoServices/FfmpegVideoExport.cs` |
| Editor background queue (DONE) | `Editor/Gui/Windows/RenderExport/ProxyGenerationService.cs` |
| Manual trigger (Phase 1 graph menu; → move to Assets Library) | `Editor/Gui/MagGraph/Interaction/GraphContextMenu.cs`, `Editor/Gui/Windows/AssetLib/*` |
| Engine substitution / auto-switch (Phase 2) | `VideoServices/VideoPlaybackEngine.cs`, `Core/Video/VideoPlayback.cs` |
| Decode session — open proxy vs source (Phase 2) | `VideoServices/VideoDecoderSession.cs`, `VideoPlaybackController.cs` |
| Cache (already codec-gated; auto-skips) | `VideoServices/VideoFrameCache.cs` |
| Proxy config (Phase-1 interim in UserSettings → migrate to **project** settings) | `Editor/Gui/UiHelpers/UserSettings.cs` |
| `UseProxiesForRendering` | `Editor/Gui/Windows/RenderExport/RenderSettings.cs` |

## Manual test set

Add `.tests-manual/video-proxy-media.md`: long-GOP import prompts (ProRes import doesn't); accepting
generates a proxy and scrubbing gets faster; render-to-file is full-res source with `UseProxiesForRendering`
off and a draft when on; changing `ProxyFormat` keeps the old proxy until Regenerate; deleting the source
invalidates the proxy.
