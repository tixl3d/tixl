# Multi-Viewport Support (Drag ImGui Tabs to Separate OS Windows)

Feature: allow the user to drag an ImGui window tab out of the main editor window into a new OS-level window with its own swap chain, primarily for multi-monitor setups.

## Goals

- Keep exactly **one** `T3Ui` / editor instance. Secondary OS windows are render hosts, not separate editors.
- Keep **one** `ImGui` context; use ImGui's built-in multi-viewport feature to manage additional platform windows.
- Use **`[UnmanagedCallersOnly]` static functions** for the PlatformIO callbacks, exposed via `&Method` `delegate*` function pointers. Rationale: fewer crash surfaces than managed delegates (no GC keepalive, no reverse P/Invoke thunk, no small-struct-return ABI ambiguity), AOT-ready, and target platform (.NET 10 + `AllowUnsafeBlocks` already on) has no friction for this. Given reported macOS crashes, minimizing moving parts in the native-managed boundary is the primary driver.
- Do **not** P/Invoke into cimgui. Use ImGui.NET 1.91.6.1 managed API. Win32 P/Invoke (for `CreateWindowEx` etc.) uses `[LibraryImport]` source generators — AOT-ready, no runtime marshalling stubs.
- Minimize touches to the deep singleton state (`T3Ui`, `ProjectView.Focused`, `UndoRedoStack`, global playback). They stay global.

## Non-Goals (initial release)

- Per-monitor font atlases (ship with ImGui's built-in viewport scaling; revisit if users complain about blur).
- Fullscreen on secondary viewports (DXGI `SetFullscreenState` on non-main swap chains is brittle, especially on Wine).
- Replacing the hardcoded `ProgramWindows.Viewer` (output render window) with a viewport. Left as-is.
- Per-viewport menu bars, per-viewport undo stacks, per-viewport playback.
- Peer-window architecture (no main window). Main window is the one with the app menu; closing it quits.
- Windows 7 support. Windows 10 1703+ is the floor, required for per-monitor DPI awareness V2.

---

## Architectural Decisions (confirmed with user)

1. **One main window, N secondary viewports.** Main = `viewport[0]`, always exists, hosts the main menu bar. Secondary viewports are created on demand when user drags a tab out; destroyed when last ImGui window in them is re-docked elsewhere.
2. **Closing the main window exits the app.** Closing a secondary window removes that viewport only.
3. **If the main dockspace becomes empty** (user drags everything out), it stays as an empty gray dockspace. No auto-dock, no "drop a panel here" hint initially.
4. **Secondary windows use the shared D3D11 `Device` and `ImmediateContext`.** New `SwapChain` + `RenderTargetView` per secondary viewport. Device is created once by `ProgramWindows` and owned there.
5. **Secondary windows use raw Win32 `CreateWindowEx` via P/Invoke, not WinForms.** Main window keeps `ImGuiDx11RenderForm`. Rationale: target deployment includes Sikarugir (Wine-on-macOS), where WinForms has a history of message-pump and subclassing quirks. Raw Win32 is what ImGui's own reference Win32 backend uses and is the most battle-tested path under Wine. Main window is not worth rewriting — existing wiring for drag/drop, menus, dialogs would need to be migrated.

---

## Affected Systems — Impact Summary

Generated from a full audit of the codebase. Most concerns from the exploration phase are preserved here for traceability.

| System | File | Change Required |
|---|---|---|
| ImGui init flags | [WindowsUiContentDrawer.cs:36-100](../../Editor/UiContentDrawing/WindowsUiContentDrawer.cs:36) | Add `ImGuiConfigFlags.ViewportsEnable` |
| PlatformIO callbacks | new file | Implement ~15 managed delegates, assign to `ImGui.GetPlatformIO()` slots |
| Render loop | [WindowsUiContentDrawer.cs:105-211](../../Editor/UiContentDrawing/WindowsUiContentDrawer.cs:105) | After `ImGui.Render()` call `UpdatePlatformWindows()` + iterate `PlatformIO.Viewports` and render each; present all swap chains |
| `AppWindow` | [AppWindow.cs](../../Editor/App/AppWindow.cs) | Decouple from the two-window singleton pattern; make constructable on demand |
| `ProgramWindows` | [ProgramWindows.cs](../../Editor/App/ProgramWindows.cs) | Add `Secondary` list alongside `Main` + `Viewer`. `Present()` iterates all swap chains |
| WndProc / input | [ImGuiDx11RenderForm.cs:133-236](../../Editor/App/ImGuiDx11RenderForm.cs:133) | Every `RenderForm` (not just main) must forward input to ImGui, with viewport ID routing |
| File drops | [ImGuiDx11RenderForm.cs:70-131](../../Editor/App/ImGuiDx11RenderForm.cs:70) | Route drops to the viewport under the cursor, not hardcoded main |
| Keyboard shortcuts | WndProc filter at line 29 | Remove the "ignore if not main form" gate so shortcuts work from any viewport |
| Layout serialization | [LayoutHandling.cs:209-235](../../Editor/Gui/Windows/Layouts/LayoutHandling.cs:209) | Extend JSON schema with `AppWindow` positions (monitor index + rect); version bump |
| `SymbolUi.WindowLayout` | See `Plan_ProjectSettings.md` "Editor State Persistence" | Same extension as `LayoutHandling`; layouts are stored per-project on root op's `.t3ui` |
| DPI / scaling | [T3Ui.cs:109](../../Editor/Gui/T3UI.cs:109) global `UiScaleFactor` | Phase 1: keep global, rely on `DpiEnableScaleViewports`. Phase 2+: per-viewport scale if needed |
| Font atlas | [FontAtlasGenerator.cs:34-157](../../Editor/UiContentDrawing/FontAtlasGenerator.cs:34) | No change initially. Rule: never rebuild atlas mid-frame or while any viewport is rendering |
| `ProjectView.Focused` | [ProjectView.cs:478](../../Editor/UiModel/ProjectHandling/ProjectView.cs:478) | No change. Focus moves between viewports as user clicks; playback follows |
| Undo/Redo | [UndoRedoStack.cs:104](../../Editor/UiModel/Commands/UndoRedoStack.cs:104) | No change. Single stack, global. Shortcut routing fix is what enables it from any viewport |
| SpaceMouse | [SpaceMouse.Device.cs](../../Editor/Gui/Interaction/Camera/SpaceMouse.Device.cs) | Out of scope for phase 1. Continues to target main HWND |
| Dialogs | [T3Ui.cs:115-120](../../Editor/Gui/T3UI.cs:115) | No change. Modal dialogs pop on whichever viewport is active |
| `ProgramWindows.Viewer` | [ProgramWindows.cs:23](../../Editor/App/ProgramWindows.cs:23) | No change. Stays hardcoded, unrelated to multi-viewport |

---

## Phase Plan

Each phase should compile and ship independently. `dotnet build Editor/Editor.csproj` must pass at the end of each phase.

### Phase 0 — Foundations (no behavior change)

Goal: prepare for multi-viewport without yet enabling it.

1. **Extract `AppWindow` factory.** Make `AppWindow` constructable from a `{form, device, deviceContext, factory, size}` bundle rather than depending on `ProgramWindows.Main` being assigned.
2. **Add a `AppWindow` registry** in `ProgramWindows`:
   ```csharp
   internal static readonly List<AppWindow> AllWindows = [];  // Main + Viewer + secondaries
   ```
   On construction/disposal of any `AppWindow`, update the list. Keep `Main` and `Viewer` properties.
3. **Rewrite `ProgramWindows.Present()`** to iterate `AllWindows` with a per-window "should present this frame" flag instead of the hardcoded Main + conditional Viewer logic.
4. **Verify build + visual regression:** run the editor, confirm main + viewer windows behave identically.

**Deliverable:** no user-visible change. Refactor only.

### Phase 1 — PlatformIO callbacks + one dragged viewport

Goal: user can drag one ImGui tab out; it pops into a new OS window; input works; render works; layout is lost on restart (no persistence yet).

1. **Enable `ViewportsEnable`** in `WindowsUiContentDrawer.Initialize()`. With no callbacks wired, ImGui falls back to single-viewport. Verify nothing breaks.
2. **Create `ImGuiPlatformBackend.cs`** (new file under `Editor/App/ImGuiPlatform/`). This class:
   - Declares `[UnmanagedCallersOnly]` static methods for each PlatformIO callback:
     - `Platform_CreateWindow(ImGuiViewportPtr)`
     - `Platform_DestroyWindow(ImGuiViewportPtr)`
     - `Platform_ShowWindow(ImGuiViewportPtr)`
     - `Platform_SetWindowPos(ImGuiViewportPtr, Vector2)`
     - `Platform_GetWindowPos(ImGuiViewportPtr) → Vector2` (returned by value)
     - `Platform_SetWindowSize`, `Platform_GetWindowSize`
     - `Platform_SetWindowFocus`, `Platform_GetWindowFocus → byte` (bool as byte), `Platform_GetWindowMinimized → byte`
     - `Platform_SetWindowTitle(ImGuiViewportPtr, byte* utf8)` — manual UTF-8 decode via `Marshal.PtrToStringUTF8`
     - `Platform_SetWindowAlpha`, `Platform_UpdateWindow`
     - `Platform_RenderWindow`, `Platform_SwapBuffers`
   - Assigns function pointers via `&Method` + `delegate* unmanaged<...>` cast into `ImGui.GetPlatformIO().Platform_*` slots.
   - Each callback wraps a `try { ... } catch (Exception e) { Log.Error(...); return sane-default; }` guard — exceptions crossing the native boundary fast-fail the process.
   - No delegate instances to keep alive. No GC concerns.
3. **Create `ImGuiRendererBackend.cs`** — same structure for the renderer callbacks:
   - `Renderer_CreateWindow`, `Renderer_DestroyWindow`, `Renderer_SetWindowSize`, `Renderer_RenderWindow`, `Renderer_SwapBuffers`
4. **Secondary AppWindow creation flow (raw Win32):**
   - `Platform_CreateWindow` → call `CreateWindowEx` via P/Invoke with a registered window class (`"TixlViewport"`), style derived from `viewport.Flags` (`NoDecoration` → `WS_POPUP`, else `WS_OVERLAPPEDWINDOW`; `NoTaskBarIcon` → `WS_EX_TOOLWINDOW`; `TopMost` → `WS_EX_TOPMOST`). Use our own `WndProc` that forwards input to ImGui and routes Win32 messages to a per-HWND dispatcher.
   - `Renderer_CreateWindow` → construct a new `SwapChain` + `RenderTargetView` for that HWND, store on `viewport.RendererUserData` as a pinned `GCHandle` to a small `PerViewportData` struct
   - Store the per-viewport state on `viewport.PlatformUserData` as a `GCHandle`
5. **Extend render loop** in `WindowsUiContentDrawer.RenderCallback`:
   - After current main `ImGui.Render()` and `RenderDrawData(mainDrawData)`:
     ```csharp
     if ((io.ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0)
     {
         ImGui.UpdatePlatformWindows();
         ImGui.RenderPlatformWindowsDefault();
         // RenderPlatformWindowsDefault iterates viewports and invokes
         // Platform_RenderWindow + Renderer_RenderWindow + Platform_SwapBuffers
         // + Renderer_SwapBuffers for each non-main viewport.
     }
     ```
   - Our `Renderer_RenderWindow` callback sets the viewport's RTV, clears it, and calls the existing `RenderDrawData` helper with that viewport's `DrawData`.
6. **Win32 window class registration** (one-time at startup):
   - `RegisterClassEx` with class name `"TixlViewport"`, our `WndProc`, `CS_DBLCLKS | CS_OWNDC`, cursor and icon inherited from main window
   - The `WndProc` forwards input events to ImGui (same logic as `ImGuiDx11RenderForm.WndProc` but per-HWND, looking up the viewport via a `Dictionary<IntPtr, ImGuiViewportPtr>`)
7. **Acceptance:** user can drag the "Parameters" tab out of the main window; it becomes a separate OS window. Render is correct. Input works in that window (clicks, typing). Docking it back works.

**Deliverable:** feature works in a single session. No layout persistence. `--enable-multiviewport` CLI flag or hidden setting gates it on/off during development.

### Phase 2 — Input routing & shortcuts

Goal: keyboard, mouse, file drops, focus events all work correctly across N viewports.

1. **`ImGuiDx11RenderForm.WndProc` unconditional input forwarding.** Remove the line 29 gate (`if (this != ProgramWindows.Viewer?.Form)`) so every form routes input to ImGui. The original gate existed to avoid double mouse coords from the Viewer (which is *not* an ImGui viewport); keep that gate specifically for `Viewer` but enable it for all ImGui-managed windows.
2. **Per-viewport mouse position.** ImGui's built-in Win32 backend expects `io.AddMouseSourceEvent` / `io.AddMousePosEvent` in screen coords when multi-viewport is active. Replace the `io.MousePos = new Vector2(e.X, e.Y)` line in main form with `io.AddMousePosEvent(screenX, screenY)` using `Form.PointToScreen(...)`.
3. **File drops on secondary viewports.** Rewire `FilesDropped` event as a list keyed by HWND, or route to the viewport under drop coordinates. Each `ImGuiDx11RenderForm` has `AllowDrop = true` already; hook the existing `OnDragDrop` in the secondary form instances.
4. **Global shortcuts (F1-F10, Ctrl+S, Undo/Redo, etc.)** — since they route via ImGui's own key state now, they should just work once input forwarding is universal. Verify by:
   - Ctrl+Z in a popped-out Graph window undoes
   - F1 in a popped-out window still loads layout 1
   - Ctrl+S saves
5. **Focus events.** `WM_SETFOCUS` / `WM_KILLFOCUS` must fire `io.AddFocusEvent(bool)` in every window, not just main.

**Deliverable:** the feature is actually usable. All input paths tested from at least one popped-out window.

### Phase 3 — Layout persistence

Goal: layouts save and load the set of AppWindows, not just ImGui dockspace.

1. **Extend `LayoutHandling` JSON schema:**
   ```json
   {
     "ImGuiVersion": "1.91.6",
     "ImGuiSettings": "...",
     "WindowConfigs": [...],
     "SecondaryViewports": [
       {
         "MonitorIndex": 1,
         "Bounds": { "X": 100, "Y": 100, "W": 800, "H": 600 },
         "IsMaximized": false
       }
     ]
   }
   ```
   Monitor index is stored (not absolute coords) so moving between different monitor setups is graceful. Use `Screen.AllScreens[index].Bounds` to resolve on load; fall back to bounds if index is out of range.
2. **Extend `SymbolUi.WindowLayout`** (per-project layouts, see `Plan_ProjectSettings.md`) with the same schema. The ImGui ini string inside already encodes viewport positions — they just need matching `AppWindow`s to exist.
3. **Load order on F1:**
   1. Read JSON, extract `SecondaryViewports`
   2. Destroy any existing secondary `AppWindow`s (not Main or Viewer)
   3. Create new secondary `AppWindow`s at the saved bounds
   4. Call `ImGui.LoadIniSettingsFromMemory(json.ImGuiSettings)` — ImGui will now find the viewports it expects
4. **Save order on Ctrl+F1:**
   1. Walk `ProgramWindows.AllWindows`, serialize secondaries
   2. Call `ImGui.SaveIniSettingsToMemory()` for dock/viewport state
   3. Write JSON
5. **Version bump.** Bump `LayoutHandling` file version constant so old layouts without `SecondaryViewports` still load (empty list, single-window behavior).

**Deliverable:** F1/Ctrl+F1 preserves multi-window setup. Project-scoped layouts (via `SymbolUi.WindowLayout`) also preserve it.

### Phase 4 — DPI handling

Goal: acceptable rendering on multi-monitor setups with mixed DPI.

1. **Enable per-monitor DPI awareness V2** in the app manifest or via `SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)`. Verify this doesn't break main window scaling.
2. **Populate `PlatformIO.Monitors`:** on startup and on `WM_DISPLAYCHANGE`, enumerate monitors via `Screen.AllScreens` or `EnumDisplayMonitors`, fill `ImGui.GetPlatformIO().Monitors`. This feeds ImGui's docking preview and lets it place windows sensibly.
3. **Handle `WM_DPICHANGED` per form:** when a window is dragged to a different-DPI monitor, update `viewport.DpiScale`. ImGui will scale visuals via `DpiEnableScaleViewports`.
4. **Ship with soft text on non-1.0 DPI.** Document as known limitation. Per-DPI font atlas is a follow-up.

**Deliverable:** windows on a 150%-scaled secondary monitor look okay, maybe a little soft.

### Phase 5 — Polish

1. Empty-main-dockspace hint ("Drag a panel here to dock") — two lines of ImGui if desired.
2. Per-viewport window icon / title prefix.
3. Snap-to-edge indicator when dragging.
4. Close button on secondary window → gracefully re-docks or closes that window's tabs (ImGui handles this via viewport flags).
5. Test + fix on Wine if that's a deployment target (see Open Questions).

---

## Implementation Details

### Callback data flow

```
User drags tab out
  → ImGui internally creates ImGuiViewport
  → ImGui calls Platform_CreateWindow(viewportPtr)
      → we construct ImGuiDx11RenderForm, store in Secondary AppWindow list
      → viewport.PlatformUserData = GCHandle.Alloc(appWindow).ToIntPtr()
  → ImGui calls Renderer_CreateWindow(viewportPtr)
      → we construct SwapChain + RTV
      → viewport.RendererUserData = GCHandle.Alloc(renderData).ToIntPtr()
  → ImGui calls Platform_ShowWindow(viewportPtr)
      → form.Show()

Each frame
  → ImGui.NewFrame(), user code, ImGui.EndFrame()
  → ImGui.Render()                        [main viewport]
  → RenderDrawData(GetDrawData())          [main viewport]
  → ImGui.UpdatePlatformWindows()         [triggers Platform_SetWindowPos etc for moved/resized viewports]
  → ImGui.RenderPlatformWindowsDefault()
      → for each secondary viewport:
          Platform_RenderWindow(vp)         [we set RTV on our deviceContext]
          Renderer_RenderWindow(vp)         [we call RenderDrawData(vp.DrawData)]
          Platform_SwapBuffers(vp)          [we call swapChain.Present()]
          Renderer_SwapBuffers(vp)          [no-op]
  → ProgramWindows.Main.SwapChain.Present()

User drags tab back or closes window
  → Platform_DestroyWindow, Renderer_DestroyWindow
  → we dispose form, swapchain, RTV
  → free GCHandles stored on PlatformUserData / RendererUserData
```

### Callback registration ([UnmanagedCallersOnly])

```csharp
internal static unsafe class ImGuiPlatformBackend
{
    public static void Initialize()
    {
        var io = ImGui.GetPlatformIO();

        // Function pointers to static unmanaged functions. No GC keepalive needed —
        // these point to compiled native code for the lifetime of the process.
        io.Platform_CreateWindow   = (IntPtr)(delegate* unmanaged<ImGuiViewportPtr, void>)&OnCreateWindow;
        io.Platform_DestroyWindow  = (IntPtr)(delegate* unmanaged<ImGuiViewportPtr, void>)&OnDestroyWindow;
        io.Platform_ShowWindow     = (IntPtr)(delegate* unmanaged<ImGuiViewportPtr, void>)&OnShowWindow;
        io.Platform_GetWindowPos   = (IntPtr)(delegate* unmanaged<ImGuiViewportPtr, Vector2>)&OnGetWindowPos;
        io.Platform_GetWindowSize  = (IntPtr)(delegate* unmanaged<ImGuiViewportPtr, Vector2>)&OnGetWindowSize;
        io.Platform_SetWindowPos   = (IntPtr)(delegate* unmanaged<ImGuiViewportPtr, Vector2, void>)&OnSetWindowPos;
        io.Platform_SetWindowSize  = (IntPtr)(delegate* unmanaged<ImGuiViewportPtr, Vector2, void>)&OnSetWindowSize;
        io.Platform_SetWindowTitle = (IntPtr)(delegate* unmanaged<ImGuiViewportPtr, byte*, void>)&OnSetWindowTitle;
        // ...etc
    }

    [UnmanagedCallersOnly]
    private static void OnCreateWindow(ImGuiViewportPtr vp)
    {
        try { /* construct HWND via CreateWindowEx, stash PerViewportState on vp.PlatformUserData */ }
        catch (Exception e) { Log.Error($"Platform_CreateWindow: {e}"); }
    }

    [UnmanagedCallersOnly]
    private static Vector2 OnGetWindowPos(ImGuiViewportPtr vp)
    {
        try { /* GetWindowRect, return top-left */ }
        catch (Exception e) { Log.Error($"Platform_GetWindowPos: {e}"); return Vector2.Zero; }
    }

    [UnmanagedCallersOnly]
    private static void OnSetWindowTitle(ImGuiViewportPtr vp, byte* utf8Title)
    {
        try
        {
            var title = Marshal.PtrToStringUTF8((IntPtr)utf8Title) ?? "";
            // SetWindowText(state.Hwnd, title);
        }
        catch (Exception e) { Log.Error($"Platform_SetWindowTitle: {e}"); }
    }
}
```

Notes:
- `ImGuiViewportPtr` is a struct wrapping a pointer, so it's blittable and valid in `[UnmanagedCallersOnly]` signatures.
- `Vector2` return is handled by the ABI directly — same way native C code returns a struct.
- Every callback wraps `try/catch`. Exceptions crossing the native boundary would fast-fail the process.
- Per-viewport state lookup goes via `vp.PlatformUserData` (a `GCHandle.ToIntPtr()`), not via captured closure state.

### Per-viewport state access

```csharp
// Stored on viewport.PlatformUserData as a GCHandle.ToIntPtr
internal sealed class PerViewportState
{
    public AppWindow AppWindow;
    public ImGuiDx11RenderForm Form;
    public SwapChain SwapChain;
    public RenderTargetView RenderTargetView;
    public GCHandle SelfHandle;  // stored for deterministic free in DestroyWindow
}

private static PerViewportState GetState(ImGuiViewportPtr vp)
{
    if (vp.PlatformUserData == IntPtr.Zero) return null;
    return (PerViewportState)GCHandle.FromIntPtr(vp.PlatformUserData).Target;
}
```

---

## Risks and Mitigations

| Risk | Mitigation |
|---|---|
| Exception thrown inside `[UnmanagedCallersOnly]` callback fast-fails process | Every callback wraps `try/catch`, logs, returns sane default. Enforce in code review — no callback may have an un-caught path |
| `PlatformUserData` `GCHandle` leaked on abnormal destroy | Track handles in a static `HashSet`, free all in `Shutdown()`. Free also in `Platform_DestroyWindow` |
| Main viewport confused with secondary (e.g. callback called for viewport[0]) | Guard `if (vp.Flags.HasFlag(ImGuiViewportFlags.IsPlatformWindow))` before creating/destroying OS windows for the main viewport |
| Font atlas rebuild while a secondary viewport is rendering | Defer atlas rebuilds to outside the render loop; document the invariant |
| Wine/SharpDX DXGI swap chain issues on secondary windows | Smoke test under Sikarugir in Phase 2 (sooner than Phase 5 as originally planned, given reported macOS crash history) |
| `Platform_SetWindowTitle` UTF-8 marshalling | Manual decode via `Marshal.PtrToStringUTF8(bytePtr)`. Works correctly; no runtime thunk involved |
| Closing main window leaves dangling secondaries | On main form close, iterate and dispose all secondary AppWindows first |
| Dropping a tab on the empty main dockspace doesn't re-dock (ImGui edge case) | Test explicitly; ImGui 1.91 handles this correctly in practice, but verify |
| macOS instability under Sikarugir | Minimize native-managed boundary complexity (already addressed by `[UnmanagedCallersOnly]` choice). Use raw Win32 for secondary windows. Avoid fullscreen on secondaries. Smoke test early |

---

## Testing Checklist

Minimum before merging phase 1:
- [ ] Drag "Parameters" tab out; confirm new OS window appears
- [ ] Close the popped-out window; tab returns to main dockspace
- [ ] Drag the popped-out window to a second monitor; render stays correct
- [ ] Type in an input field in the popped-out window; text appears
- [ ] Click and drag something in the popped-out window; interaction works
- [ ] Ctrl+Z from main window still undoes
- [ ] Resize the main window; secondaries unaffected
- [ ] Exit app with one window popped out; no crash, no leaked HWND

Phase 2 adds:
- [ ] Ctrl+Z from popped-out window undoes
- [ ] F1 from popped-out window loads layout
- [ ] Drop a file on popped-out window; it's handled correctly
- [ ] Alt-Tab switches between main and secondary appropriately

Phase 3 adds:
- [ ] Ctrl+F1 saves, restart app, F1 loads — window popped out at same position
- [ ] Save layout with window on monitor 2, remove monitor 2, load → window appears on monitor 1 (graceful fallback)
- [ ] Old pre-multi-viewport layout file loads without crash (no `SecondaryViewports` field)

---

## Open Questions

1. **Should `ProgramWindows.Viewer` eventually fold into the viewport system?** Left out of scope here. Could be a later refactor for consistency, but current hardcoded dual-swap-chain works fine for its narrow use case.
2. **How does the user actually trigger multi-viewport for the first time?** ImGui lets you drag the tab out of the dockspace to empty space. Verify this UX discovery works without additional UI cues.

## Resolved Decisions

- **Sikarugir is a macOS Wine wrapper** (successor to Wineskin, uses WineD3D/DXVK/D3DMetal). Users have reported random crashes on macOS. → Use raw Win32 `CreateWindowEx` for secondary windows instead of WinForms. → Use `[UnmanagedCallersOnly]` instead of `Marshal.GetFunctionPointerForDelegate` to minimize native-managed boundary complexity. → Smoke-test under Sikarugir early (Phase 2), not late.
- **Windows 7 is not a support target.** → Per-monitor DPI V2 and other Windows 10 APIs are fair game.
- **Target framework is `.NET 10 windows`.** `AllowUnsafeBlocks` is already enabled in `Editor.csproj` and `Core.csproj`. `[UnmanagedCallersOnly]` and `delegate* unmanaged<...>` function pointers are fully supported and AOT-ready.

---

## Related Documents

- `Plan_ProjectSettings.md` — `SymbolUi.WindowLayout` persists ImGui ini per-project; this plan extends that schema
- `.agentic/AGENT_INSTRUCTIONS.md` — no heap allocations in per-frame paths; callback bodies must respect this
- `.agentic/SOLUTION_OVERVIEW.md` — architecture map

## Status

Draft — not yet implemented. Phase 0 is safe to start immediately; later phases depend on confirming the Open Questions, especially the Wine / Sikarugir answer.
