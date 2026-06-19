# Video Proxy Media (background transcode for fast seeking)

**Status:** Draft — 2026-06-19. Design only, no code yet. The "background optimizer / `-optimized.mp4`"
pulled out of [`Plan_FfmpegVideo.md`](Plan_FfmpegVideo.md)'s backlog into its own design, now that it has
real UX (a prompt, a Generate/Use state machine, settings).

## Goal

When a user adds a **long-GOP H.264/HEVC** video, TiXL offers to transcode it in the background to an
**all-intra proxy** (ProRes / HAP / …), and once ready the `VideoPlaybackEngine` **auto-switches** playback
to the proxy so scrubbing and random seeking are cheap. The original is always retained; proxies are a
preview/authoring optimization.

## Relationship to the other video plans

- **Built on the encode milestone.** A proxy is a transcode (decode source → encode proxy), so it **reuses
  the tier-1 LGPL encoder** from [`Plan_FfmpegEncode.md`](Plan_FfmpegEncode.md) and **cannot ship before it
  exists**. One encoder, two consumers (render-export + proxy generation).
- **Substitution lives in the engine.** [`Plan_FfmpegVideo.md`](Plan_FfmpegVideo.md)'s `VideoPlaybackEngine`
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

| Setting | Home | Default | Note |
|---|---|---|---|
| `GenerateProxyVideos` (Off/Ask/On) | `UserSettings` (per-machine) | **Ask** | generation needs the encoder → editor/per-machine |
| `ProxyFormat` (ProRes/HAP/DNxHR/MJPEG) | `UserSettings` | ProRes 422 Proxy *(confirm)* | never H.264/HEVC (GPL) |
| `ProxyResolution` (100/50/25 %) | `UserSettings` | 50 % *(confirm)* | scaled proxy = preview-only |
| `UseProxyVideos` (interactive preview) | `UserSettings` *(open: Core if the player uses proxies — see Risks)* | true | preview only |
| `UseProxiesForRendering` | **`RenderSettings`** (project `.t3ui`) | **false** | opt-in draft render from the proxy; default export uses the source |

Mirrors the encode split: *generation* params are per-machine (`UserSettings`); the *render* behavior is a
per-project `RenderSetting`. Only the preview-use toggle's home is still open.

## Generation (the `OptimizerService`)

- **Trigger:** a video is added/first opened whose codec is **long-GOP H.264/HEVC** with a keyframe interval
  past a threshold. **Skip** already-cheap sources — all-intra (ProRes/DNxHD/MJPEG), HAP/GPU-texture, and
  short-GOP files don't need a proxy. *(Threshold — propose "max keyframe interval > ~2 s" i.e. GOP > ~2×fps;
  tune.)*
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
([`Plan_FfmpegVideo.md`](Plan_FfmpegVideo.md): all-intra is decode-on-demand with no GOP-cache; HAP bypasses
the cache entirely). So **no new special-case is needed** — the instant the engine switches a stream to a
proxy, the codec class flips and the RAM cache is skipped automatically. Stated as policy:

> **Proxy and the RAM GOP-cache are mutually-exclusive solutions to the same problem.** An active proxy
> supersedes the cache, freeing that clip's slice of the shared cache budget for the clips that *aren't*
> proxied, and overriding the cache half of `Optimize for: Fast Seeking`.

Proxy (persistent disk, survives restart, helps draft export) and cache (in-session RAM) are otherwise
**complementary** across a session.

## Phasing (build-verifiable)

1. **Generation core.** `OptimizerService` transcode (source → proxy via the tier-1 LGPL encoder) +
   `ProxyFormat`/`ProxyResolution` `UserSettings` + a manual "Generate proxy" action (no auto-trigger yet).
   *Verify: a proxy file is produced for ProRes & HAP and plays back.*
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
| Engine substitution / auto-switch | `Video/VideoPlaybackEngine.cs`, `Core/Video/VideoPlayback.cs` |
| Decode session — open proxy vs source | `Video/VideoDecoderSession.cs`, `Video/VideoPlaybackController.cs` |
| Tier-1 encoder reused for transcode | see [`Plan_FfmpegEncode.md`](Plan_FfmpegEncode.md) |
| Cache (already codec-gated; auto-skips) | `Video/VideoFrameCache.cs` |
| Generation params + preview-use toggle | `Editor/Gui/UiHelpers/UserSettings.cs` |
| `UseProxiesForRendering` | `Editor/Gui/Windows/RenderExport/RenderSettings.cs` |
| Import/first-open trigger hook | TBD — asset registration / `PlayVideo` first open |

## Manual test set

Add `.tests-manual/video-proxy-media.md`: long-GOP import prompts (ProRes import doesn't); accepting
generates a proxy and scrubbing gets faster; render-to-file is full-res source with `UseProxiesForRendering`
off and a draft when on; changing `ProxyFormat` keeps the old proxy until Regenerate; deleting the source
invalidates the proxy.
