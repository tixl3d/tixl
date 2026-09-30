#nullable enable
using ImGuiNET;
using SDL;

namespace T3.Editor.App.Gestures;

/// <summary>
/// Trackpad gestures as SDL reports them: Wayland's pointer gestures on Linux and the magnification gesture on
/// macOS arrive as pinch events, and two-finger scrolling arrives as a wheel with fractional notches.
/// </summary>
/// <remarks>
/// Windows has no pinch through SDL — its drivers send Ctrl + wheel instead, which
/// <see cref="PointerGestures"/> handles as the fallback. A Win32 source reading WM_GESTURE could replace that.
/// </remarks>
internal sealed class SdlPointerGestureSource : IPointerGestureSource
{
    public bool ProvidesPinch { get; } = !OperatingSystem.IsWindows();

    public string Description => "SDL";

    /// <summary>Handles the gesture events; returns false for anything it does not deal with.</summary>
    public bool TryProcessEvent(in SDL_Event sdlEvent, float pixelDensity)
    {
        switch (sdlEvent.Type)
        {
            case SDL_EventType.SDL_EVENT_PINCH_BEGIN:
                _lastScale = 1;
                PointerGestures.ReportPinchBegin();
                return true;

            case SDL_EventType.SDL_EVENT_PINCH_UPDATE:
            {
                // Wayland reports the scale against the gesture's start, not against the previous event, and it
                // sends no finger positions to measure a span with. So the step is the ratio between them.
                var scale = sdlEvent.pinch.scale;
                var step = _lastScale > 0 ? scale / _lastScale : 1;
                _lastScale = scale;

                // Wayland sends no finger positions and reports -1 for the focus; then the zoom belongs at the
                // pointer, which sits between the fingers anyway.
                var hasFocus = sdlEvent.pinch.focus_x >= 0 && sdlEvent.pinch.focus_y >= 0;
                var focus = new Vector2(sdlEvent.pinch.focus_x, sdlEvent.pinch.focus_y) * pixelDensity;

                PointerGestures.ReportPinchUpdate(step, hasFocus ? focus : null);
                return true;
            }

            case SDL_EventType.SDL_EVENT_PINCH_END:
                PointerGestures.ReportPinchEnd();
                return true;

            default:
                return false;
        }
    }

    /// <summary>One wheel event, with the whole-notch counters SDL derives from it.</summary>
    public void ProcessWheel(in SDL_MouseWheelEvent wheel, bool ctrlHeld, ImGuiIOPtr io)
    {
        PointerGestures.ReportScroll(wheel.x, wheel.y, wheel.integer_y, (uint)wheel.which, ctrlHeld, io);
    }

    private float _lastScale = 1;
}
