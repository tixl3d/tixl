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
| `ProxyFormat` (ProRes/HAP) | **Project** — `CompositionSettings.ProxyConfig.Format` ✅ | ProRes | never H.264/HEVC (GPL) |
| `ProxyResolution` (0.1–1.0) | **Project** — `…ProxyConfig.Resolution` ✅ | 0.5 | scaled proxy = preview-only |
| `UseProxyVideos` (interactive preview) | **Project** — `…ProxyConfig.UseForPreview` ✅ | true | preview only; engine reads `CompositionSettings.Current` |
| `UseProxiesForRendering` | **`RenderSettings`** (project `.t3ui`) | **false** | opt-in draft render from the proxy; default export uses the source |

**Migration — DONE (Phase 3).** `ProxyFormat` / `ProxyResolution` / `UseProxyVideos` moved off per-machine
`UserSettings` into per-project `CompositionSettings.ProxyConfig` (`Format` / `Resolution` / `UseForPreview`),
serialized under a new `"Proxy"` section of the `.t3` `ProjectSettings` block (additive; old files fall back to
defaults — see [`CompositionSettings.cs`](../../Core/Settings/CompositionSettings.cs)). The engine now reads
`CompositionSettings.Current.Proxy.UseForPreview` directly (the `VideoPlayback.UseProxies` Core static and the
editor push were removed); `ProxyGenerationService` reads `Format`/`Resolution` from the same place. UI is a new
**Video Proxies** category in the Project Settings window. *Format-version bump deferred: the `"Proxy"` section
is additive/forward-tolerant and this is mid-feature — bump `SymbolFormatVersion` when the proxy feature is
finalized and a release is tagged (per the file-format-versioning convention).*

## Generation (the `OptimizerService`)

- **Auto-trigger (the goal):** a video is added/first opened whose **keyframe spacing** exceeds the proxy
  threshold → generate per the **project's** proxy settings. **Skip** already-cheap sources. The
  intra-vs-inter decision is **codec-agnostic and implemented** ([`ProxyEligibility.cs`](../../VideoServices/ProxyEligibility.cs)):
  it demuxes packet headers and measures the actual keyframe interval, so all-intra (ProRes/DNxHR/MJPEG/HAP →
  ~1 frame ≈ 0.03 s) is skipped while inter-frame H.264/HEVC is caught — and an I-frame-only H.264 is correctly
  skipped too. *Threshold `LongGopSeconds = 0.75 s`* (revised down from the original ~2 s guess: the sample
  `test-720p.mp4` is a perfectly normal 1 s-GOP H.264 that does scrub sluggishly at higher res, so 2 s was too
  coarse; 0.75 s sits clear of all-intra yet catches typical ~1 s camera/screen-recording GOPs — still tunable,
  could later scale with resolution).
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
     error/cancel. **Video-only** (no audio in the preview proxy). **Encodes to a temp `.partial.mov` sibling and
     atomically swaps into place on success** — the engine substitutes a proxy the instant its file exists, and a
     MOV has no playable `moov` atom until finalize, so writing the final path directly let preview pick up a
     half-written file ("moov atom not found"). The swap also prevents a crash leaving a broken-but-present proxy. Verified by
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
2. **Engine substitution + `UseProxyVideos` — DONE (build-verified; in-editor verify pending).** The engine
   substitutes proxy-for-source per request: [`VideoPlaybackEngine.RequestFrame`](../../VideoServices/VideoPlaybackEngine.cs)
   computes an *effective path* — the sibling proxy when `VideoPlayback.UseProxies` is on, the request is **not**
   rendering to file, and the proxy file exists — else the source; flipping the path makes the existing
   `VideoPlaybackController` re-open transparently to the operator. The proxy path is shared via
   `VideoPlayback.GetProxyPath` ([`Core/Video/VideoPlayback.cs`](../../Core/Video/VideoPlayback.cs)) so generation
   (`ProxyGenerationService`) and lookup can't diverge (`clip.mp4` → `clip.proxy.mov`; proxy codecs all mux to
   MOV). The toggle is interim per-machine `UserSettings.UseProxyVideos` (default on), pushed into the Core static
   via `UserSettings.ApplyVideoPlaybackSettings()` (startup + on change) and exposed as a **Video Proxies → "Use
   proxy videos for preview"** checkbox in Settings. *Existence-only validity for now — staleness/regenerate is
   Phase 5. UseProxyVideos moves to a project setting in Phase 3.*
   *Verify (in-editor): with a `*.proxy.mov` present, scrubbing a `[PlayVideo]` uses the proxy (faster seek, smaller
   preview texture); render-to-file stays full-res source; unchecking the setting reverts to the source.*
3. **Auto-trigger + state machine + Ask popup.** Split:
   - **3a. Eligibility detection — DONE & TESTED.** [`ProxyEligibility.Evaluate(sourcePath)`](../../VideoServices/ProxyEligibility.cs)
     measures keyframe spacing from the bitstream (demux-only, no decode) → `Recommended` / `NotNeeded` /
     `Unknown` + the measured interval + a reason string. Codec-agnostic (rides measured spacing, not an
     allow-list). Exposed across the ALC boundary as `IVideoEncoderFactory.EvaluateProxyNeed` →
     `VideoProxyAdvice` ([`Core/Video/VideoExport.cs`](../../Core/Video/VideoExport.cs)). Verified by
     [`ProxyEligibilityTests`](../../VideoServices.Tests/ProxyEligibilityTests.cs): a 1 s-GOP H.264 is
     `Recommended`; the all-intra ProRes proxy transcoded from it is `NotNeeded`; a missing file is `Unknown`.
   - **3b. Per-project settings migration — DONE & TESTED.** `ProxyFormat`/`ProxyResolution`/`UseProxyVideos`
     moved from per-machine `UserSettings` into per-project `CompositionSettings.ProxyConfig`, serialized under a
     new `"Proxy"` section in `.t3` (additive; missing → defaults). Engine reads `CompositionSettings.Current.Proxy`
     directly (removed the `VideoPlayback.UseProxies` static + editor push); generation reads it too; UI is a new
     **Video Proxies** category in the Project Settings window. Verified by
     [`CompositionSettingsProxyTests`](../../Core.Tests/CompositionSettingsProxyTests.cs): non-default round-trip,
     persistence when not Enabled, and missing-section → defaults. *(`SymbolFormatVersion` bump deferred to feature
     finalization.)*
   - **3c. Auto-trigger + state machine + Ask popup — TODO.** On import, consult `EvaluateProxyNeed`; if
     `Recommended`, apply the `GenerateProxyVideos` tri-state (Ask → prompt once and remember; On → enqueue;
     Off → skip). The Ask popup; the Generate/Use matrix; remembering the choice; move the manual trigger to
     the Assets Library. *Verify: a long-GOP H.264 import prompts; a ProRes import does not; the choice is
     remembered.*
4. **`UseProxiesForRendering`** `RenderSetting` (default false) — opt-in draft render from the proxy.
   *Verify: off → full-res source; on → draft at proxy resolution.*
5. **Polish.** Partly done:
   - **DONE — low-disk guard.** Per-machine `UserSettings.ProxyMinFreeDiskGb` (default 5). Generation refuses (with
     a `Failed` status) when the target drive is below it; the graph "Generate proxy" menu item disables with a
     free/required tooltip; an editor for the threshold lives in the proxy panel.
     ([`ProxyGenerationService.HasEnoughFreeDisk`](../../Editor/Gui/Windows/RenderExport/ProxyGenerationService.cs),
     `GraphContextMenu`, `ProjectSettingsWindow`.)
   - **DONE — progress feedback.** Gated 25/50/75 % encode logging, plus a live **Generation** status section in
     the proxy panel (queued / generating % / failed) on top of the existing context-menu "Generating… NN%".
   - **DONE — proxy storage management.** The proxy panel lists total `*.proxy.mov` size + count for the current
     project and across all writable projects, each with a confirm-popup Delete button (sources untouched; in-use
     files are skipped and reported). The directory scan is cached and invalidated via
     `ResourceFileWatcher.FileStateChangeCounter` (the existing monotonic file-change signal — writing/deleting a
     proxy bumps it, same pattern `AssetLibrary` uses) plus the active-project folder, so the panel stays
     allocation-free per frame. No new "UpdateVersion" counter was needed. Proxy suffix is now the shared
     `VideoPlayback.ProxySuffix` constant. ([`ProjectSettingsWindow.DrawProxyStorage`](../../Editor/Gui/Windows/TimeLine/ProjectSettingsWindow.cs).)
   - **TODO — remaining:** regenerate-on-settings-change, source-change invalidation (staleness), per-clip
     format/scale overrides, proxy-cache disk budget + LRU cleanup.

## Known issue — preview tearing (under investigation)

A horizontal content tear (two half-frames) appears in preview, **reported only with proxy media (possibly HAP)**.
Not display tearing: the editor swapchain is flip-model (`FlipDiscard`), which can't tear at present, and the main
present already uses sync-interval 1 when vsync is on — so it's a *texture* tear. The decode→upload buffer is
locked on both sides, so the leading hypothesis is the single reused software-upload texture
(`VideoPlaybackController._softwareTexture`, overwritten each frame via `UpdateSubresource`); the likely fix is to
double-buffer (ping-pong two textures) so an upload never lands on a texture the GPU may still be sampling. Pending
confirmation of whether it reproduces on the original (non-proxy) clip and whether it's constant vs. motion-only.

## Risks / open questions

- **`ProxyFormat` / `ProxyResolution` defaults** — propose ProRes 422 Proxy @ 50% (balanced size/quality);
  HAP for best scrub at a disk cost.
- **Keyframe-interval threshold** for offering a proxy — tuning.
- **"Generate-only" (On/Off) semantics** — propose pre-bake without switching preview; confirm.
- **Does the player ship/use proxies?** *(home decided 2026-06-20: per-project `CompositionSettings`.)* The
  setting travels with the project; whether the standalone player honours it is a separate, later choice.
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
| Engine substitution + shared proxy path (DONE — Phase 2) | `VideoServices/VideoPlaybackEngine.cs`, `Core/Video/VideoPlayback.cs` |
| Eligibility detection (DONE — Phase 3a; `EvaluateProxyNeed` facade) | `VideoServices/ProxyEligibility.cs`, `Core/Video/VideoExport.cs` |
| Per-project proxy settings (DONE — Phase 3b) | `Core/Settings/CompositionSettings.cs` (`ProxyConfig`), `Editor/Gui/Windows/TimeLine/ProjectSettingsWindow.cs` |
| Decode session — open proxy vs source (Phase 2) | `VideoServices/VideoDecoderSession.cs`, `VideoPlaybackController.cs` |
| Cache (already codec-gated; auto-skips) | `VideoServices/VideoFrameCache.cs` |
| Proxy config (migrated off UserSettings → per-project `CompositionSettings.ProxyConfig`) | `Core/Settings/CompositionSettings.cs` |
| `UseProxiesForRendering` | `Editor/Gui/Windows/RenderExport/RenderSettings.cs` |

## Manual test set

Add `.tests-manual/video-proxy-media.md`: long-GOP import prompts (ProRes import doesn't); accepting
generates a proxy and scrubbing gets faster; render-to-file is full-res source with `UseProxiesForRendering`
off and a draft when on; changing `ProxyFormat` keeps the old proxy until Regenerate; deleting the source
invalidates the proxy.
