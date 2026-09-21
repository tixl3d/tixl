using ImGuiNET;
using T3.Editor.Gui.UiHelpers;

// ReSharper disable InconsistentNaming

namespace T3.Editor.App;

/// <summary>
/// Helper class to detect pinch zoom on accumulate  scrolling / panning on touchpad devices
/// </summary>
internal static class MouseWheelPanning
{
    public static Vector2 PanDelta;
    internal static float PinchZoomDelta;

    internal static void ProcessFrame(float pixelsPerNotch)
    {
        PanDelta = new System.Numerics.Vector2(_wheelX, _wheelY) * pixelsPerNotch;
        PinchZoomDelta = _zoomNotches;

        _wheelX = 0;
        _wheelY = 0;
        _zoomNotches = 0;
    }

    /// <summary>
    /// Takes one wheel event with both axes, already in notches — fractional on precision devices.
    /// </summary>
    public static void ProcessWheel(float notchesX, float notchesY, bool ctrlHeld, ImGuiIOPtr io)
    {
        // Most Windows trackpad drivers present a pinch as a vertical wheel with Ctrl held. Once a zoom has
        // started, stray wheel events for a moment belong to it rather than to panning.
        var now = Environment.TickCount64;

        if (ctrlHeld && UserSettings.Config.UseTouchPadPanning || (now - _lastZoomTick) < 80)
        {
            _zoomNotches += notchesY * 2;
            _lastZoomTick = now;
            return;
        }

        io.MouseWheel += notchesY / 2;
        _wheelY += notchesY;

        io.MouseWheelH += notchesX / 2;
        _wheelX += notchesX;
    }


    private static long _lastZoomTick;
    private static float _wheelX; 
    private static float _wheelY;
    private static float _zoomNotches;
}