#nullable enable
using System.Collections.Generic;
using ImGuiNET;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.App.Gestures;

/// <summary>
/// What the trackpad is doing, in terms the editor's canvases use: a pan in pixels, a zoom around a point, and
/// whether the device scrolling is a trackpad at all. Platform sources push into it; the UI reads it per frame.
/// </summary>
/// <remarks>
/// A wheel reports whole notches, a trackpad a fraction of one per event, because it scrolls by pixels. That is
/// what identifies it — device names are no help, since Wayland presents every pointer as one device. Each
/// device is remembered, so a mouse plugged into the same machine keeps behaving like a wheel.
/// </remarks>
internal static class PointerGestures
{
    /// <summary>This frame's pan in pixels, including the glide after a flick.</summary>
    public static Vector2 PanDelta { get; private set; }

    /// <summary>This frame's zoom as a factor; 1 means none.</summary>
    public static float ZoomFactor { get; private set; } = 1;

    /// <summary>The point the zoom happened around, in pixels — the fingers' centre for a pinch.</summary>
    public static Vector2 ZoomFocus { get; private set; }

    /// <summary>Whether <see cref="ZoomFocus"/> means anything; Wayland reports no focus for a pinch.</summary>
    public static bool HasZoomFocus { get; private set; }

    /// <summary>True while a pinch is in progress, so panning can stay out of its way.</summary>
    public static bool IsPinching { get; private set; }

    /// <summary>
    /// True while the fingers are moving the view — scrolling, pinching, or gliding after a flick. Direct
    /// manipulation should land where the fingers put it, so the canvas skips the smoothing that makes discrete
    /// wheel steps glide and would only lag behind here.
    /// </summary>
    public static bool IsManipulating { get; private set; }

    /// <summary>
    /// Whether scrolling should pan the view instead of zooming it: what the setting says, or what the device
    /// last used turned out to be.
    /// </summary>
    public static bool PansWithScroll => UserSettings.Config.UseTouchPadPanning || _lastDeviceIsPrecise;

    internal static void SetSource(IPointerGestureSource source)
    {
        _source = source;
        Log.Debug($"Trackpad gestures: {source.Description} (pinch {(source.ProvidesPinch ? "from the system" : "emulated with Ctrl+scroll")})");
    }

    /// <summary>One scroll event, in notches. Fractional on a trackpad, whole on a wheel.</summary>
    /// <param name="wholeNotchesY">
    /// What the device accumulated into whole notches; a trackpad leaves it at 0 for most of its events.
    /// </param>
    public static void ReportScroll(float notchesX, float notchesY, int wholeNotchesY, uint deviceId, bool ctrlHeld, ImGuiIOPtr io)
    {
        _lastDeviceIsPrecise = ClassifyDevice(deviceId, notchesY, wholeNotchesY);

        // Windows trackpad drivers send a pinch as Ctrl + wheel, and for a moment afterwards further events
        // belong to that zoom rather than to panning. Where the system reports pinches this is not needed.
        var now = Environment.TickCount64;
        var emulatesPinch = _source is not { ProvidesPinch: true };
        if (emulatesPinch && PansWithScroll && (ctrlHeld || now - _lastEmulatedZoomTicks < 80))
        {
            _emulatedZoomNotches += notchesY * 2;
            _lastEmulatedZoomTicks = now;
            return;
        }

        io.MouseWheel += notchesY / 2;
        io.MouseWheelH += notchesX / 2;

        _scrollNotches += new Vector2(notchesX, notchesY);
        _scrolledThisFrame = true;
    }

    public static void ReportPinchBegin()
    {
        IsPinching = true;
        StopGliding();
    }

    /// <param name="scale">How much the fingers' distance grew since the last event; 1 means unchanged.</param>
    /// <param name="focusInPixels">
    /// The centre between the fingers, in window pixels, where the platform reports one. Null where it does not,
    /// and the caller zooms around the pointer instead.
    /// </param>
    public static void ReportPinchUpdate(float scale, Vector2? focusInPixels)
    {
        if (scale <= 0)
            return;

        IsPinching = true;

        // Exaggerated on purpose: following the fingers exactly reads as sluggish, because a pinch spans a few
        // centimetres while the view spans decades of zoom.
        _pinchScale *= MathF.Pow(scale, PinchSensitivity);

        if (focusInPixels is { } focus)
        {
            ZoomFocus = focus;
            HasZoomFocus = true;
        }
    }

    public static void ReportPinchEnd()
    {
        IsPinching = false;
        HasZoomFocus = false;
    }

    /// <summary>Ends the glide — called when something else takes over, such as a drag or a click.</summary>
    public static void StopGliding()
    {
        _glideVelocity = Vector2.Zero;
    }

    /// <summary>
    /// Turns what arrived since the last frame into this frame's pan and zoom. A flick keeps going afterwards,
    /// decaying, the way a trackpad behaves everywhere else.
    /// </summary>
    internal static void ProcessFrame(float secondsSinceLastFrame)
    {
        var deltaTime = Math.Clamp(secondsSinceLastFrame, 0.001f, 0.1f);
        var scrolled = _scrollNotches * PanPixelsPerNotch;

        if (_scrolledThisFrame)
        {
            var speed = scrolled / deltaTime;

            if (PansWithScroll)
            {
                // A trackpad sends one event per frame, and often only one axis of it, so a diagonal arrives as
                // alternating horizontal and vertical steps. Panning by the smoothed speed blends them back into
                // the direction the fingers took, at the cost of a frame or two of catch-up.
                // Time-based, so the feel does not change with the frame rate: at 120 Hz this is the same
                // 0.35 per frame it started as, and at 60 Hz it converges over the same milliseconds.
                var smoothing = 1 - MathF.Exp(-deltaTime / SpeedSmoothingSeconds);
                _glideVelocity = Vector2.Lerp(_glideVelocity, speed, smoothing);
                PanDelta = _glideVelocity * deltaTime;
            }
            else
            {
                PanDelta = scrolled;
                _glideVelocity = Vector2.Zero;
            }
        }
        else if (_glideVelocity.LengthSquared() > GlideStopSpeed * GlideStopSpeed)
        {
            PanDelta = _glideVelocity * deltaTime;
            _glideVelocity *= MathF.Exp(-deltaTime / GlideDecaySeconds);
        }
        else
        {
            PanDelta = Vector2.Zero;
            _glideVelocity = Vector2.Zero;
        }

        ZoomFactor = _pinchScale * ComputeZoomFromNotches(_emulatedZoomNotches);
        IsManipulating = PansWithScroll && (IsPinching || _scrolledThisFrame || PanDelta.LengthSquared() > 0.0001f);

        _scrollNotches = Vector2.Zero;
        _scrolledThisFrame = false;
        _pinchScale = 1;
        _emulatedZoomNotches = 0;
    }

    /// <summary>The wheel's own zoom curve, so an emulated pinch feels like the one a wheel gives.</summary>
    private static float ComputeZoomFromNotches(float notches)
    {
        const float zoomSpeed = 1.2f;
        return notches == 0 ? 1 : MathF.Pow(zoomSpeed, notches);
    }

    /// <summary>
    /// Remembers what a device is once it has sent a fraction of a notch. Wheels stay unclassified, which is
    /// the same answer as "not a trackpad".
    /// </summary>
    private static bool ClassifyDevice(uint deviceId, float notchesY, int wholeNotchesY)
    {
        if (_precisionDevices.TryGetValue(deviceId, out var wasPrecise) && wasPrecise)
            return true;

        var scrolled = MathF.Abs(notchesY) > 0.0001f;
        var isPrecise = scrolled && (wholeNotchesY == 0 || MathF.Abs(notchesY - MathF.Round(notchesY)) > 0.01f);

        if (isPrecise && !wasPrecise)
            Log.Debug($"Scrolling device #{deviceId} behaves like a trackpad; two fingers pan the view.");

        _precisionDevices[deviceId] = isPrecise;
        return isPrecise;
    }

    /// <summary>How much faster the view zooms than the fingers move; 1 would track them exactly.</summary>
    private const float PinchSensitivity = 2f;

    /// <summary>
    /// How far the view moves per scrolled notch. A trackpad sends a fraction of a notch per event, so this
    /// sets how closely the view follows the fingers.
    /// </summary>
    private const float PanPixelsPerNotch = 35f;

    /// <summary>How long the measured speed takes to catch up with the fingers.</summary>
    private const float SpeedSmoothingSeconds = 0.02f;

    /// <summary>How long a flick keeps gliding: one e-fold of its speed.</summary>
    private const float GlideDecaySeconds = 0.12f;

    /// <summary>Below this, in pixels per second, the glide stops instead of creeping.</summary>
    private const float GlideStopSpeed = 40f;

    private static IPointerGestureSource? _source;
    private static readonly Dictionary<uint, bool> _precisionDevices = [];
    private static bool _lastDeviceIsPrecise;
    private static Vector2 _scrollNotches;
    private static bool _scrolledThisFrame;
    private static Vector2 _glideVelocity;
    private static float _pinchScale = 1;
    private static float _emulatedZoomNotches;
    private static long _lastEmulatedZoomTicks;
}
