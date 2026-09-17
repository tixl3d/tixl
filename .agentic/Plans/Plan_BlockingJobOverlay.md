# Plan: Stall Overlay (Progress UI While the Main Thread Is Blocked)

Drafted 2026-09-17. **Phase 1 implemented 2026-09-17** (uncommitted at the time of writing).
Manual tests: [stall-overlay](../../.tests-manual/stall-overlay.md).

## Motivation

Compiling, duplicating, combining, creating or loading a project, hot reload, or an operator
blocking in its update all run synchronously on the main thread. The editor froze without
feedback, and after about five seconds Windows ghosted the window as "Not Responding". We rarely
know beforehand whether an operation will be slow.

## Approach: a watchdog presenter

A second thread watches the main thread's frame heartbeat. Once the main thread neither renders
nor answers window messages for about half a second, the watchdog presents the last captured UI
frame, dimmed, with a panel showing the current activity. It stops as soon as the main thread
renders again.

The decisive property: **no call site changes**. Every stall is covered, including ones nobody
anticipated. Labeling an operation is optional and only adds a message and an estimate.

Rejected alternatives, for the record:

- **Splitting jobs into background build and main-thread swap with completion callbacks.** Every
  compiling call site, including seven undo commands, would have needed restructuring. Operator
  constructors and disposal use the D3D immediate context, and `BlockingWindow.ShowMessageBox`
  takes the render lock and switches the global ImGui context, so whole jobs cannot simply move to
  a task. Moving only the `dotnet build` process into the background remains a valid later
  optimization; it shortens stalls but is no longer needed for feedback.
- **A WinForms overlay window on its own thread.** Separate top-level window, no theme, z-order
  and DPI issues.
- **Rendering one banner frame before blocking.** Static, and still ghosts.

## How it works

| Piece | File | Role |
| --- | --- | --- |
| `StallWatchdog` | `Editor/App/StallWatchdog.cs` | Thread, stall detection, D3D11 presenting, `PresentLock` |
| `StallOverlay` | `Editor/Gui/UiHelpers/StallOverlay.cs` | Builds overlay geometry, glyph snapshot, last log line |
| `MainThreadActivity` | `Editor/Gui/UiHelpers/MainThreadActivity.cs` | Optional scopes with message, per-key duration averages |

Constraints and how they are met:

- **The watchdog must not touch ImGui.** The main thread may be stalled in the middle of an ImGui
  frame and the ImGui context is process-global. `StallOverlay` lays out text from a snapshot of
  the font atlas glyphs taken on the main thread whenever the atlas is rebuilt. It emits
  `ImDrawVert` triangles, so it reuses the ImGui shaders via `WindowsUiContentDrawer.TryBindUiPipeline`.
  Note: ImGui.NET declares the `ImFontGlyph` bitfield as three fields, which shifts every following
  member. The snapshot reads the native layout directly.
- **The immediate context is shared.** `ID3D11Multithread` protection is switched on (the video
  decoder already did this on demand). The watchdog records into a deferred context and submits one
  command list with `restoreContextState: true`, so the stalled frame continues with its pipeline
  state intact.
- **Flip-model swap chains discard the back buffer.** `ProgramWindows.CaptureUiFrame` copies every
  frame before present. The mirror-UI-on-second-view feature now uses the same copy instead of its own.
  A watchdog present also discards the cleared back buffer of the stalled frame, so
  `NotifyRenderingBackBuffer` tells the main thread to clear again before it draws.
- **Ghosting.** `DisableProcessWindowsGhosting` keeps Windows from replacing the window with a
  frozen ghost copy.
- **Modal loops are not stalls.** Dragging the window, menus, message boxes and file dialogs stop
  the render loop but still pump messages. The watchdog therefore confirms a stall with
  `SendMessageTimeout(WM_NULL, SMTO_ABORTIFHUNG)`, the same criterion Windows uses.
- **Shared objects.** `PresentLock` guards the back buffer view, the captured frame and the font
  atlas. The main thread takes it when replacing any of them (`RebuildBackBuffer`, capture, atlas
  rebuild) and in `NotifyRenderingBackBuffer`, which also ends a takeover.

Overlay content: title from the innermost `MainThreadActivity` scope or `Working...`, the last
log line, and a bar. With a stored average for the outermost scope's key the bar follows
`1 - exp(-1.5 * elapsed / average)`, capped at 97%. Without one, a segment slides. Elapsed seconds
appear once there is no estimate or the stall outlasts it. Averages persist in
`activityEstimates.json` in the settings folder (versioned, string keys, unknown keys ignored).
They are machine-specific measurements, which is why they are not in `UserSettings`.

Current scopes: `compile`, `compile-restore`, `load-packages`, `create-project`,
`duplicate-symbol`, `combine-symbol`.

## Verification done

Via the debug protocol method `stallMainThread` (see [DEBUG_PROTOCOL](../DEBUG_PROTOCOL.md)):

- Presenting from the watchdog while the window thread sleeps works on the dev machine (NVIDIA,
  Windows 11, windowed flip-discard). No ghosting. This was the gating risk.
- Unlabeled, first labeled, estimated and overdue states render correctly; the editor recovers
  cleanly after each stall, more than ten times in a row.
- Real recompiles take about one second here, so the overlay stays invisible for them, as intended.

Not verified yet, covered by the manual test set: window drag and native dialogs, resize between
stalls, second output window, a genuinely long compile, other GPU vendors.

One unexplained event during testing: an editor instance shut down gracefully about two and a half
minutes after a probe screenshot was requested through `ScreenshotWriter`. The shutdown was
graceful, so the main thread was alive, and the probe code is gone. If a hang after a stall is ever
reported, the first suspect is the watchdog's `Present` inside `PresentLock` while the main thread
waits for that lock in `NotifyRenderingBackBuffer`.

## Open items

1. **Kill switch.** There is no setting to disable the watchdog. Add one if any driver misbehaves.
2. **Queued input replays after a stall.** Clicks and keys pressed during a stall arrive afterwards,
   as before. Flushing them risks losing key-up events and stuck modifiers, so it needs care.
3. **Cost of the per-frame back buffer copy** at 4K on weak GPUs is unmeasured. It is one GPU-side
   `CopyResource`. If it matters, skip it while rendering to file or make it optional.
4. **Startup.** The watchdog starts with the render loop. Project loading at startup still shows
   the WinForms splash. Starting the loop earlier would cover it and remove a Windows-only
   dependency, but changes the startup lifecycle (see
   [Plan_BrokenPackageRecovery](Plan_BrokenPackageRecovery.md)).
5. **More scopes** where log lines are not informative enough: unarchiving a project, asset import,
   render export setup.
6. **`Compiler.cs` timing log** used `Elapsed.Milliseconds`, which wraps at one second. Fixed
   alongside, because estimates and log lines should agree.

## Portability (see [Plan_CrossPlatformV5](Plan_CrossPlatformV5.md))

`StallOverlay` and `MainThreadActivity` are backend-neutral. Only the presenting part of
`StallWatchdog` is D3D11-specific and should become a small backend interface: "present this
texture plus these triangles from a foreign thread".

- **Vulkan** is simpler: the watchdog records its own command buffer, and the only shared objects
  are the queue and the swap chain, guarded by one mutex in the backend. It should bypass the
  D3D11-shaped facade. The swap chain must acquire its image late in the frame and keep a spare
  image, so a stalled main thread never starves the watchdog. On Wayland, present needs a timeout
  because hidden windows receive no frame callbacks.
- **Ghosting has no Linux equivalent.** Compositors ping the thread that pumps window events. The
  portable end state with SDL3 is a thin event-pump thread with the editor frame loop on a second
  thread, which removes the ghosting problem on every platform. The stall check then changes from
  `SendMessageTimeout` to a heartbeat of the editor thread alone.
