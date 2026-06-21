# UI Timelapse (making-of capture from the screenshot tool)

**Status:** Draft — 2026-06-21. Design only, no code yet. A sibling to continuous capture
([`Plan_ContinuousCapture.md`](Plan_ContinuousCapture.md)) but deliberately **not** part of the render setup —
it lives on the **screenshot tool**, captures the **editor UI** (not the operator output), and encodes a
**sped-up timelapse** for "making-of" videos.

## Goal

Record the editor's UI over a long session — building a patch, performing — and encode it to a short
timelapse video (one frame every N seconds, played back at normal FPS → an hour compressed to ~minutes).
Triggered from the **screenshot icon's context menu**, not the Render To File window.

## Decisions (revised 2026-06-21, from the user)

- **Trigger from the app menu**, not the output-window icons: **AppMenu → View → "Record Timelapse" /
  "Stop Timelapse"** (the label toggles with state). The screenshot icon — or any output-window icon — is the
  wrong home for this. (View menu: [AppMenuBar.cs:451](../../Editor/Gui/AppMenuBar.cs), items drawn via
  `MenuItem()` / `MenuItemToggle()` → `CustomComponents.DrawMenuItem`.)
- **NVENC-only.** The menu item is only enabled when a **hardware** H.264/HEVC encoder is available
  (`VideoEncoderAvailabilityCache.Get(...).Kind == Hardware`); greyed with an explanatory tooltip otherwise.
  A long UI timelapse at full-window resolution isn't worth a software encode.
- **Blinking recording indicator while active.** A `StatusAttention` dot in the app-menu-bar status cluster
  (between the app menu and the existing status icons — [AppMenuBar.cs:63-79](../../Editor/Gui/AppMenuBar.cs),
  alongside `DrawErrorsIndicator()` / the perf graph). Its tooltip shows **status, elapsed duration, and file
  size so far**; **clicking the dot stops** the recording (a second way to stop, besides the menu item).
- **Tooltip** on the menu item explains what it does (records the editor UI on an interval into a sped-up
  video). **Frequency lives in Settings → Interface → Output**, next to the existing `ContinuousScreenshotDelay`;
  the tooltip points there.
- **Separate, later work** — parked behind continuous capture.

## The UI frame is already a texture (no swapchain grab)

The UI-mirror feature already composites the rendered editor frame into an off-screen `Texture2D`:

- `ProgramWindows._uiCopyTexture` + `ProgramWindows.UiCopyTextureSrv` — created in
  `RebuildUiCopyTextureIfRequired()` ([ProgramWindows.cs:404](../../Editor/App/ProgramWindows.cs)), format
  `R8G8B8A8_UNorm`, sized to `Main.SwapChain` mode description.
- `CopyUiContentToShareTexture()` ([ProgramWindows.cs:446](../../Editor/App/ProgramWindows.cs)) does
  `_deviceContext.CopyResource(Main.BackBufferTexture, _uiCopyTexture)` — the whole editor frame, after ImGui
  has rendered.
- Today this only runs when `UserSettings.Config.MirrorUiOnSecondView` is on
  ([WindowsUiContentDrawer.cs:156](../../Editor/UiContentDrawing/WindowsUiContentDrawer.cs)).

**So the timelapse just encodes `_uiCopyTexture`.** It does **not** need `MirrorUiOnSecondView` to be enabled:
on each capture tick, call `RebuildUiCopyTextureIfRequired()` + `CopyUiContentToShareTexture()` once (a cheap
GPU copy, only every N seconds), then feed the texture to the encoder. Capture must happen at the same point
in the frame as the mirror copy (after the UI is drawn), so hook the recorder into `WindowsUiContentDrawer`'s
post-draw path rather than the per-frame render-export loop.

## Encoding — reuse the continuous-capture writer

Reuse `IRenderVideoWriter` / `FfmpegVideoExportWriter` (NVENC H.264). One frame is written per interval tick;
because the file's FrameRate is a normal playback rate (e.g. 30/60), the result is automatically sped up by
`interval × fps`. This is the same "write current texture N times / once" path as realtime continuous capture,
minus the wall-clock pacing — here the *interval timer* is the pacing.

Keep it a **small dedicated recorder** (`UiTimelapseRecorder`) rather than overloading `RenderProcess`:
different source texture, different trigger, different output folder, no audio, no render settings. It can
share the writer and the encoder-availability cache.

## UI / interaction

- **AppMenu → View item.** Add a single toggling item to the View menu
  ([AppMenuBar.cs:451](../../Editor/Gui/AppMenuBar.cs)) next to the existing `MenuItemToggle(...)` rows:
  **"Record Timelapse"** when idle, **"Stop Timelapse"** when active.
- **NVENC gate.** When no hardware encoder is available, draw the item disabled (greyed, tooltip still shows)
  — the tooltip says why.
- **Menu tooltip.** "Records the editor UI into a sped-up timelapse video (one frame every N seconds) — good
  for making-of clips. Set the interval in Settings → Interface → Output. Requires a hardware (NVENC) encoder."
- **Status-bar recording dot.** While recording, draw a blinking `StatusAttention` dot in the menu-bar status
  cluster ([AppMenuBar.cs:63-79](../../Editor/Gui/AppMenuBar.cs)) — drive the blink from the shared
  `ImGui.GetTime()` source, not a per-element timer. Its tooltip shows the running status: elapsed duration,
  frames captured, and current output file size. **Clicking the dot stops the recording.** (Make the dot an
  `InvisibleButton` + draw, so it's clickable; keep the blink subtle.)
- **Output path & filename.** A file under the project's `Screenshots/` (or a `Making-Of/` subfolder). The
  name encodes the **speed factor** and an incrementing **version index**, e.g. `timelapse-x10-v01.mp4`. The
  speed factor is `interval × playbackFps` (one frame per `interval` seconds of real time, played back at the
  file's FPS), rounded for the label. The version index auto-increments per recording — and on a
  mid-recording restart (see resize handling) — reusing the render-export `-vNN` increment convention
  (`RenderPaths.GetNextIncrementedPath` / the version regex). No render-settings path involved.

## Settings

- Add `UiTimelapseIntervalSeconds` (default e.g. 10) to `UserSettings`
  ([UserSettings.cs:152](../../Editor/Gui/UiHelpers/UserSettings.cs) is the neighbour
  `ContinuousScreenshotDelay`), surfaced in Settings → Interface → Output
  ([SettingsWindow.cs:223](../../Editor/Gui/Windows/SettingsWindow.cs)).
- No `RenderSettings` changes — this is intentionally decoupled from render-export.

## Independent of the screenshot tool

Moving the trigger to AppMenu → View **decouples this from the output-window screenshot icon entirely.** That's
a simplification, not just a relocation: the screenshot icon's right-click context menu and the interval
continuous-screenshot *loop* described in the help (`ExportVideos.md`) **may not actually be wired in the
current code** — the *settings* exist (`ContinuousScreenshotDelay`, `ScreenshotFileFormat`) and the click
handler only does a single `TryRenderScreenShot()`. By living on its own menu item + recorder, the timelapse
has no dependency on that possibly-unbuilt menu. (The continuous-screenshot gap is a separate item to verify.)

## The cursor is missing from the mirror texture

For a *making-of* the mouse cursor really matters — and it is **absent** from `_uiCopyTexture` today. TiXL uses
the **hardware OS cursor** (`ImGui.GetIO().MouseDrawCursor` is left off — see
[ImGuiDx11RenderForm.cs:280](../../Editor/App/ImGuiDx11RenderForm.cs)), which Windows composites *outside* the
D3D backbuffer, so the `CopyResource(Main.BackBufferTexture, _uiCopyTexture)` copy never contains it.

Two ways to fix, both follow-ups (the timelapse can ship cursor-less first, but it's much less useful that way):

1. **Composite a cursor sprite into `_uiCopyTexture`** after the copy, at the current mouse position — scoped
   to capture ticks only, so it doesn't affect the live editor or the second-view mirror. Preferred: contained,
   no global side effects.
2. **Enable `io.MouseDrawCursor`** so ImGui software-draws the cursor into the backbuffer. Simpler, but it's a
   **global** change — it alters the cursor everywhere in the editor and the second-view mirror, not just the
   timelapse. Likely too broad.

This also improves the existing **UI-mirror / second-view** feature (it has the same missing-cursor gap), so
fixing it once benefits both — worth flagging as shared low-hanging fruit.

## Open questions

1. **Interval source of truth.** Reuse `ContinuousScreenshotDelay`, or a separate `UiTimelapseIntervalSeconds`?
   (Leaning separate — screenshot cadence and timelapse cadence are different intents.)
2. **Window resized mid-recording — decided (2026-06-21).** `_uiCopyTexture` follows the swap-chain size, but
   the encoder is fixed at its creation resolution. On a resize, **finalize the current file and immediately
   start a new recording with an incremented version index** (`timelapse-x10-v01.mp4` → `…-v02.mp4`), rather
   than stretching one file across resolutions. Each clip then stays at a single consistent resolution; the new
   clip tracks the new window size. (No letterboxing/rescaling needed — a clean cut-and-restart is simpler and
   the auto-increment plumbing already exists.)
3. **Multi-monitor / spanned UI.** `_uiCopyTexture` is the main window's backbuffer only — the timelapse
   captures the main editor window, not a spanned/second view. Document that.
4. **Long sessions.** Same MP4 ~4 GB caveat as continuous capture — but interval capture makes hitting it far
   less likely (one frame / N seconds).

## Manual test + help

- New `.tests-manual/` set: enable a hardware encoder, start "Capture UI Timelapse…", build a patch for a
  minute, stop, verify a short sped-up video of the UI; verify the item is disabled (with tooltip) on a
  machine without NVENC; verify the interval respects the Settings value.
- Extend `.help/docs/using/ExportVideos.md` (or `Recording.md`) with a short "UI timelapse / making-of"
  paragraph once it ships.
