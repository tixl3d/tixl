using System.Windows.Forms;
using ImGuiNET;
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
    /// Needs to be called from WndProc for WM_MOUSEHWHEEL and WM_MOUSEWHEEL
    /// </summary>
    public static bool ProcessMouseWheelInput(Message m, ImGuiIOPtr imgGuiIo)
    {
        var wparam = (long)m.WParam;
        var keyFlags = (int)(wparam & 0xffff);
        var delta = (short)((wparam >> 16) & 0xffff); // can be != 120 on precision devices :contentReference[oaicite:1]{index=1}
        var notches = delta / (float)WHEEL_DELTA;

        var isCtrl = (keyFlags & MK_CONTROL) != 0;

        // Optional: sticky "zoom mode" for a short time to suppress stray pan events
        var now = Environment.TickCount64;
        var inZoomGesture = (now - _lastZoomTick) < 80;

        if (isCtrl || inZoomGesture)
        {
            _zoomNotches += notches;
            _zoomNotches += notches; // use vertical delta as zoom input
            _lastZoomTick = now;
            m.Result = IntPtr.Zero;
            return true;
        }

        if (m.Msg == WM_MOUSEWHEEL)
        {
            imgGuiIo.MouseWheel += notches / 2;
            _wheelY += notches;
        }
        else
        {
            imgGuiIo.MouseWheelH += notches / 2;
            _wheelX += notches;
        }

        m.Result = IntPtr.Zero;
        return false;
    }

    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_MOUSEHWHEEL = 0x020E;
    private const int MK_CONTROL = 0x0008;
    private const int WHEEL_DELTA = 120;

    private static long _lastZoomTick;
    private static float _wheelX; 
    private static float _wheelY;
    private static float _zoomNotches;
}