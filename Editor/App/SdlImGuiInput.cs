using System.Runtime.InteropServices;
using ImGuiNET;
using SDL;
using T3.Editor.Gui.UiHelpers;
using T3.SdlPlatform;
using T3.SystemUi;
using static SDL.SDL3;

namespace T3.Editor.App;

/// <summary>
/// Feeds SDL's input events to ImGui and to TiXL's own key state — the job <c>imgui_impl_sdl3</c> does, shaped
/// after the Win32 message handling it replaces so that input behaves the same on every platform.
/// </summary>
/// <remarks>
/// SDL reports the pointer in window points, and the editor sizes ImGui in pixels, so positions are scaled by the
/// window's pixel density. On a HiDPI display an unscaled position lands every click in the wrong place.
/// </remarks>
internal static unsafe class SdlImGuiInput
{
    /// <summary>Handles one event. Events for windows the editor does not own are ignored.</summary>
    public static void ProcessEvent(in SDL_Event sdlEvent)
    {
        // Events can arrive before ImGui has a context, and ImGui asserts on GetIO() without one.
        if (ImGui.GetCurrentContext() == IntPtr.Zero)
            return;

        var io = ImGui.GetIO();

        switch (sdlEvent.Type)
        {
            case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                // The viewer shows the output; pointer input belongs to the main window only.
                if (IsMainWindow(sdlEvent.motion.windowID))
                    io.MousePos = ToPixels(sdlEvent.motion.x, sdlEvent.motion.y);

                break;

            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
            {
                var down = sdlEvent.Type == SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN;

                // A press in the viewer is ignored, a release is not: a drag that ends over it must still end.
                if (down && !IsMainWindow(sdlEvent.button.windowID))
                    break;

                var button = sdlEvent.button.Button switch
                                 {
                                     SDLButton.SDL_BUTTON_LEFT   => 0,
                                     SDLButton.SDL_BUTTON_RIGHT  => 1,
                                     SDLButton.SDL_BUTTON_MIDDLE => 2,
                                     _                           => -1,
                                 };

                if (button >= 0)
                    io.MouseDown[button] = down;

                break;
            }

            case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                if (IsMainWindow(sdlEvent.wheel.windowID))
                    MouseWheelPanning.ProcessWheel(sdlEvent.wheel.x, sdlEvent.wheel.y, IsCtrlDown(), io);

                break;

            case SDL_EventType.SDL_EVENT_KEY_DOWN:
            case SDL_EventType.SDL_EVENT_KEY_UP:
            {
                var down = sdlEvent.Type == SDL_EventType.SDL_EVENT_KEY_DOWN;

                // The keyboard belongs to the editor even while a viewer on a projector has focus; releases are
                // kept so a key held while focus moved does not stick.
                if (down && !IsMainWindow(sdlEvent.key.windowID))
                    break;

                var virtualKey = SdlKeyMap.ToVirtualKey(sdlEvent.key.key);
                if (virtualKey == 0)
                    break;

                // TiXL's own shortcuts read KeyHandler, which is indexed by virtual key as WinForms' Keys were.
                if (down)
                {
                    KeyHandler.SetKeyDown(virtualKey);
                }
                else
                {
                    KeyHandler.SetKeyUp(virtualKey);
                }

                HandleKeyEvent(io, virtualKey, down);
                break;
            }

            case SDL_EventType.SDL_EVENT_TEXT_INPUT:
            {
                var text = Marshal.PtrToStringUTF8((IntPtr)sdlEvent.text.text);
                if (!string.IsNullOrEmpty(text))
                    io.AddInputCharactersUTF8(text);

                break;
            }

            case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_GAINED:
                io.AddFocusEvent(true);
                ReleaseModifiersNotHeld(io);
                break;

            case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
                // Key-up events are lost while unfocused; clear everything so nothing stays stuck, Alt especially.
                io.AddFocusEvent(false);
                io.ClearInputKeys();
                KeyHandler.ReleaseAllKeys();
                break;

            case SDL_EventType.SDL_EVENT_DROP_BEGIN:
                _droppedFiles.Clear();
                if (IsMainWindow(sdlEvent.drop.windowID))
                    DragAndDropHandling.StartExternalDrag(DragAndDropHandling.DragTypes.ExternalFile, "External Files");

                break;

            case SDL_EventType.SDL_EVENT_DROP_POSITION:
                if (IsMainWindow(sdlEvent.drop.windowID))
                    io.MousePos = ToPixels(sdlEvent.drop.x, sdlEvent.drop.y);

                break;

            case SDL_EventType.SDL_EVENT_DROP_FILE:
            {
                var path = Marshal.PtrToStringUTF8((IntPtr)sdlEvent.drop.data);
                if (!string.IsNullOrEmpty(path))
                    _droppedFiles.Add(path);

                break;
            }

            case SDL_EventType.SDL_EVENT_DROP_COMPLETE:
                CompleteDrop(sdlEvent.drop.windowID);
                break;
        }
    }

    /// <summary>
    /// Per-frame safety net for a modifier whose key-up never arrived — Alt+Tab between the editor's own windows
    /// sends the release to the task switcher. Release-only: a modifier still held reads as down and is kept.
    /// </summary>
    public static void ReconcileStuckModifiers()
    {
        ReleaseModifiersNotHeld(ImGui.GetIO());
    }

    /// <summary>
    /// Applies the cursor ImGui asked for this frame at once, rather than on the next pointer movement, so the
    /// shape does not trail the hover state on small drag handles and splitters.
    /// </summary>
    public static void ApplyRequestedCursor()
    {
        var io = ImGui.GetIO();
        if ((io.ConfigFlags & ImGuiConfigFlags.NoMouseCursorChange) != 0)
            return;

        var requested = ImGui.GetMouseCursor();
        if (requested == _lastRequestedCursor)
            return;

        _lastRequestedCursor = requested;

        if (requested == ImGuiMouseCursor.None || io.MouseDrawCursor)
        {
            SDL_HideCursor();
            return;
        }

        SDL_SetCursor(GetSystemCursor(requested));
        SDL_ShowCursor();
    }

    /// <summary>
    /// Forwards a key to ImGui, keeping both its modifier events (shortcut matching) and its legacy modifier
    /// flags (InputText) in step. Alt is also mirrored into <see cref="KeyHandler"/>.
    /// </summary>
    private static void HandleKeyEvent(ImGuiIOPtr io, int virtualKey, bool down)
    {
        switch (virtualKey)
        {
            case VirtualKeyShift:
                io.KeyShift = down;
                io.AddKeyEvent(ImGuiKey.ModShift, down);
                break;
            case VirtualKeyControl:
                io.KeyCtrl = down;
                io.AddKeyEvent(ImGuiKey.ModCtrl, down);
                break;
            case VirtualKeyAlt:
                io.KeyAlt = down;
                io.AddKeyEvent(ImGuiKey.ModAlt, down);
                if (down)
                {
                    KeyHandler.SetKeyDown(Key.Alt);
                }
                else
                {
                    KeyHandler.SetKeyUp(Key.Alt);
                }

                break;
        }

        var imguiKey = ((Key)virtualKey).ToImGuiKey();
        if (imguiKey != ImGuiKey.None)
            io.AddKeyEvent(imguiKey, down);
    }

    /// <summary>
    /// Releases modifiers SDL no longer reports as held. Only ever releases: synthesising a key-down from a state
    /// read right after refocusing can stick a modifier that never gets its key-up.
    /// </summary>
    private static void ReleaseModifiersNotHeld(ImGuiIOPtr io)
    {
        var modifiers = SDL_GetModState();

        if (io.KeyShift && (modifiers & SDL_Keymod.SDL_KMOD_SHIFT) == 0)
        {
            io.KeyShift = false;
            io.AddKeyEvent(ImGuiKey.ModShift, false);
            KeyHandler.SetKeyUp(Key.ShiftKey);
        }

        if (io.KeyCtrl && (modifiers & SDL_Keymod.SDL_KMOD_CTRL) == 0)
        {
            io.KeyCtrl = false;
            io.AddKeyEvent(ImGuiKey.ModCtrl, false);
            KeyHandler.SetKeyUp(Key.CtrlKey);
        }

        if (io.KeyAlt && (modifiers & SDL_Keymod.SDL_KMOD_ALT) == 0)
        {
            io.KeyAlt = false;
            io.AddKeyEvent(ImGuiKey.ModAlt, false);
            KeyHandler.SetKeyUp(Key.Alt);
        }
    }

    private static void CompleteDrop(SDL_WindowID windowId)
    {
        if (!IsMainWindow(windowId) || _droppedFiles.Count == 0)
        {
            // A drag that left the window, or was dropped on the viewer.
            DragAndDropHandling.CancelExternalDrag();
            _droppedFiles.Clear();
            return;
        }

        // A single dropped folder opens the import dialog instead of the per-file drop zones.
        if (_droppedFiles.Count == 1 && System.IO.Directory.Exists(_droppedFiles[0]))
        {
            DragAndDropHandling.CancelExternalDrag();
            Gui.Windows.AssetLib.FolderImportDialog.ShowForFolder(_droppedFiles[0]);
        }
        else
        {
            DragAndDropHandling.CompleteExternalDrop(DragAndDropHandling.DragTypes.ExternalFile, string.Join("|", _droppedFiles));
        }

        _droppedFiles.Clear();
    }

    private static bool IsMainWindow(SDL_WindowID windowId) => ProgramWindows.Main != null && windowId == ProgramWindows.Main.Id;

    private static bool IsCtrlDown() => (SDL_GetModState() & SDL_Keymod.SDL_KMOD_CTRL) != 0;

    private static System.Numerics.Vector2 ToPixels(float x, float y)
    {
        var density = ProgramWindows.Main?.PixelDensity ?? 1f;
        return new System.Numerics.Vector2(x * density, y * density);
    }

    private static SDL_Cursor* GetSystemCursor(ImGuiMouseCursor cursor)
    {
        var shape = cursor switch
                        {
                            ImGuiMouseCursor.TextInput  => SDL_SystemCursor.SDL_SYSTEM_CURSOR_TEXT,
                            ImGuiMouseCursor.ResizeAll  => SDL_SystemCursor.SDL_SYSTEM_CURSOR_MOVE,
                            ImGuiMouseCursor.ResizeEW   => SDL_SystemCursor.SDL_SYSTEM_CURSOR_EW_RESIZE,
                            ImGuiMouseCursor.ResizeNS   => SDL_SystemCursor.SDL_SYSTEM_CURSOR_NS_RESIZE,
                            ImGuiMouseCursor.ResizeNESW => SDL_SystemCursor.SDL_SYSTEM_CURSOR_NESW_RESIZE,
                            ImGuiMouseCursor.ResizeNWSE => SDL_SystemCursor.SDL_SYSTEM_CURSOR_NWSE_RESIZE,
                            ImGuiMouseCursor.Hand       => SDL_SystemCursor.SDL_SYSTEM_CURSOR_POINTER,
                            ImGuiMouseCursor.NotAllowed => SDL_SystemCursor.SDL_SYSTEM_CURSOR_NOT_ALLOWED,
                            _                           => SDL_SystemCursor.SDL_SYSTEM_CURSOR_DEFAULT,
                        };

        // Created once per shape and kept: SDL cursors are cheap to hold and costly to recreate per frame.
        if (!_cursors.TryGetValue(shape, out var handle))
        {
            handle = (IntPtr)SDL_CreateSystemCursor(shape);
            _cursors[shape] = handle;
        }

        return (SDL_Cursor*)handle;
    }

    private const int VirtualKeyShift = 0x10;
    private const int VirtualKeyControl = 0x11;
    private const int VirtualKeyAlt = 0x12;

    private static ImGuiMouseCursor _lastRequestedCursor = ImGuiMouseCursor.Arrow;
    private static readonly Dictionary<SDL_SystemCursor, IntPtr> _cursors = [];
    private static readonly List<string> _droppedFiles = [];
}
