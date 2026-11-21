#nullable enable
namespace Lib.io.video;

/// <summary>
/// The data-driven camera-control model shared by all platform backends: one
/// <see cref="CameraControlDescriptor"/> per control (kind, range, default, auto link, menu items), one
/// <see cref="ICameraControlBackend"/> per OS, and a <see cref="CameraControlBackendRegistry"/> that
/// dispatches by platform. This replaces the old fixed <c>CameraControl</c> enum surface (whose hardcoded
/// V4L2 CIDs and per-call COM binds answered only "is control X supported") with what the device actually
/// reports — the cross-platform replacement for the DirectShow capture-filter dialog that went away with
/// the FFmpeg capture port.
/// </summary>
public enum CameraControlKind
{
    /// <summary>A plain integer control with a device-reported (or convention-estimated) range.</summary>
    Integer,

    /// <summary>A 0..1 toggle — V4L2 BOOLEAN; DirectShow controls whose reported range is exactly 0..1.</summary>
    Boolean,

    /// <summary>A named-item menu (V4L2 type 3); <see cref="CameraControlItem.Value"/> is the query index.</summary>
    Menu,

    /// <summary>
    /// An integer-valued menu (V4L2 type 9); <see cref="CameraControlItem.Value"/> is the driver's own
    /// integer (the QUERYMENU union value), never a position — the menu items may be non-contiguous.
    /// </summary>
    IntegerMenu,

    /// <summary>An action trigger (V4L2 BUTTON, type 4) — applies on trigger, no readable value.</summary>
    Button,
    // V4L2 types with no representation here are skipped during enumeration: 5 INTEGER64, 6 CTRL_CLASS
    // (the class markers 0x00980001/0x009A0001), 7 STRING, 8 BITMASK and every compound type (>= 0x0100)
    // (videodev2.h:1841-1849).
}

/// <summary>
/// Device-reported control state flags (V4L2 queryctrl flags; the DirectShow counterpart of the dialog
/// graying a slider). <see cref="EstimatedRange"/> marks a range that came from UVC class conventions
/// rather than the device.
/// </summary>
[Flags]
public enum CameraControlStates
{
    None = 0,

    /// <summary>Device reports read-only (V4L2 READ_ONLY) — the UI shows the value, no writes.</summary>
    ReadOnly = 1,

    /// <summary>V4L2 WRITE_ONLY — trigger-style, no readable value.</summary>
    WriteOnly = 2,

    /// <summary>V4L2 INACTIVE: a linked auto control is engaged (the DirectShow dialog grays the slider).</summary>
    Inactive = 4,

    /// <summary>V4L2 VOLATILE: the value changes out-of-band; never cache reads.</summary>
    Volatile = 8,

    /// <summary>V4L2 GRABBED: the control's resource is busy.</summary>
    Grabbed = 16,

    /// <summary>The range came from UVC conventions, not the device (the old "estimated" note).</summary>
    EstimatedRange = 32,
}

/// <summary>One menu item: the value a control is SET to (the query index for V4L2 MENU, the driver's own
/// integer for INTEGER_MENU) plus its display label.</summary>
public readonly record struct CameraControlItem(int Value, string Label);

/// <summary>
/// One control as the device reports it. Everything is immutable value data: backends produce descriptors,
/// the op and the control worker consume them. The canonical <see cref="Name"/> is what
/// <see cref="CameraControlCatalog.Find"/> and <see cref="AutoControlName"/> match on (ordinal
/// case-insensitive) — never a raw driver label.
/// </summary>
public sealed record CameraControlDescriptor
{
    /// <summary>Canonical, UI-facing name ("White Balance Temperature") from the shared vocabulary
    /// in <see cref="CameraControlNames"/>.</summary>
    public required string Name { get; init; }

    public required CameraControlKind Kind { get; init; }

    public int Minimum { get; init; }
    public int Maximum { get; init; }
    public int Step { get; init; }
    public int DefaultValue { get; init; }

    /// <summary>Device-units = physical * Scale — for float-based APIs (macOS zoom is exposed as 1.0 = 1x,
    /// reported as 100 with Scale = 100 to match the UVC convention).</summary>
    public float Scale { get; init; } = 1f;

    public CameraControlStates States { get; init; } = CameraControlStates.None;

    /// <summary>Menu items; may be empty even for Menu controls (uvcvideo returns no items for some menus) —
    /// the surface then shows raw values.</summary>
    public CameraControlItem[] Items { get; init; } = [];

    /// <summary>Canonical name of the paired auto control, "" = none (the normalized auto controls themselves
    /// are the auto side and carry no link — they are driven as ordinary Boolean/Menu controls).</summary>
    public string AutoControlName { get; init; } = "";

    /// <summary>Backend-internal identity ("v4l2:0x0098091A", "dshow:VideoProcAmp.WhiteBalance",
    /// "avf:videoZoomFactor") — how a backend maps a descriptor back onto its driver object.</summary>
    public required string BackendId { get; init; }

    /// <summary>Last observed value, filled by the control worker's read pass (not by enumeration);
    /// null = unread or not readable (write-only).</summary>
    public int? CurrentValue { get; init; }

    /// <summary>Last observed auto state of the control (or of its linked auto partner, for controls
    /// WITH an <see cref="AutoControlName"/>) — filled by the worker's read pass.</summary>
    public bool CurrentIsAuto { get; init; }
}

/// <summary>Everything one device reported in a single enumeration sweep — an immutable snapshot the op
/// reads while the worker may already be building the next one.</summary>
public sealed record CameraControlCatalog(
    WebcamWithIndex Device,
    DateTime CapturedAt,
    IReadOnlyList<CameraControlDescriptor> Controls)
{
    /// <summary>Ordinal case-insensitive match on the canonical descriptor name — never on raw driver labels.</summary>
    public CameraControlDescriptor? Find(string name)
    {
        foreach (var control in Controls)
        {
            if (string.Equals(control.Name, name, StringComparison.OrdinalIgnoreCase))
                return control;
        }

        return null;
    }
}

/// <summary>
/// The canonical control-name vocabulary shared by every backend — the DirectShow property names are the
/// base, plus the Linux-only normalized auto controls (V4L2 has explicit auto controls where DirectShow has
/// Auto/Manual flags; "White Balance, Automatic" normalizes to <see cref="WhiteBalanceAuto"/> etc.).
/// The fixed Pan/Tilt/Zoom/Focus/Exposure/Brightness inputs of [VideoDeviceInput] address these names.
/// </summary>
internal static class CameraControlNames
{
    public const string Pan = "Pan";
    public const string Tilt = "Tilt";
    public const string Roll = "Roll";
    public const string Zoom = "Zoom";
    public const string Focus = "Focus";
    public const string Iris = "Iris";
    public const string Exposure = "Exposure";
    public const string Brightness = "Brightness";
    public const string Contrast = "Contrast";
    public const string Hue = "Hue";
    public const string Saturation = "Saturation";
    public const string Sharpness = "Sharpness";
    public const string Gamma = "Gamma";
    public const string WhiteBalance = "WhiteBalance";
    public const string BacklightCompensation = "BacklightCompensation";
    public const string Gain = "Gain";
    public const string ColorEnable = "ColorEnable";
    public const string PowerLineFrequency = "PowerLineFrequency";

    // Linux-only normalized auto controls (first-class catalog entries, addressable via ControlName).
    public const string WhiteBalanceAuto = "WhiteBalanceAuto";
    public const string FocusAuto = "FocusAuto";
    public const string ExposureAuto = "ExposureAuto";
    public const string ExposureAutoPriority = "ExposureAutoPriority";
}

/// <summary>
/// One per-OS control backend. All methods run on the control worker's thread only — never on the
/// evaluation thread. Device handles (cached fd / COM filter) live inside the backend and are dropped
/// through <see cref="InvalidateHandles"/> when a device may have been replaced at the same index.
/// </summary>
internal interface ICameraControlBackend
{
    /// <summary>One-shot sweep of everything the device exposes. Returns null when the device could not be
    /// opened or has no controls.</summary>
    CameraControlCatalog? EnumerateCatalog(WebcamWithIndex device);

    /// <summary>Current value and auto state of one control. isAuto reports the control's linked auto
    /// partner's state (DirectShow: the Get flags out-parameter; Linux: the partner control's value).</summary>
    bool TryGetValue(WebcamWithIndex device, CameraControlDescriptor control, out int value, out bool isAuto);

    /// <summary>
    /// Writes a value. SHARED APPLY RULE: the write always lands as Manual — when the descriptor carries an
    /// <see cref="CameraControlDescriptor.AutoControlName"/>, the backend disengages that auto first
    /// (DirectShow implicitly via Set(prop, value, Manual); Linux via an explicit partner write; macOS via
    /// the mode set). Without it, a write against an auto-grayed (INACTIVE) control silently no-ops.
    /// </summary>
    bool TrySetValue(WebcamWithIndex device, CameraControlDescriptor control, int value);

    /// <summary>Engages/leaves the control's auto mode (DirectShow flag write, V4L2 partner control write,
    /// AVFoundation mode set).</summary>
    bool TrySetAuto(WebcamWithIndex device, CameraControlDescriptor control, bool auto);

    /// <summary>Drops any cached per-device handle (fd / COM filter) so the next job re-opens the device —
    /// called when a camera may have been replaced at the same index. The device name is informational;
    /// identity is the index.</summary>
    void InvalidateHandles(WebcamWithIndex device);
}

/// <summary>
/// Platform dispatch for control backends: each per-OS backend file registers itself from its
/// [ModuleInitializer] — <c>CameraControlBackendRegistry.Register(OperatingSystem.IsLinux,
/// () => new V4L2CameraControls())</c> — and <see cref="Create"/> returns the first backend whose platform
/// check passes. Until a platform registers anything (and on platforms without a backend) Create returns
/// null and every caller must degrade to honest "device controls are not available" messaging; a backend
/// whose native library is missing can simply not register.
/// </summary>
internal static class CameraControlBackendRegistry
{
    private static readonly object _lock = new();
    private static readonly List<(Func<bool> PlatformCheck, Func<ICameraControlBackend> CreateBackend)> _entries = [];

    /// <summary>Called by each per-OS backend file from its [ModuleInitializer]. Registration order is
    /// registration sequence order; platform checks are expected to be mutually exclusive anyway.</summary>
    public static void Register(Func<bool> platformCheck, Func<ICameraControlBackend> createBackend)
    {
        lock (_lock)
        {
            _entries.Add((platformCheck, createBackend));
        }
    }

    /// <summary>The first registered backend whose platformCheck() is true, or null when none is — every
    /// caller must handle null as "this platform has no control backend".</summary>
    public static ICameraControlBackend? Create()
    {
        lock (_lock)
        {
            foreach (var (platformCheck, createBackend) in _entries)
            {
                try
                {
                    if (platformCheck())
                        return createBackend();
                }
                catch (Exception e)
                {
                    // A backend that cannot even construct (missing native library) must not take the op
                    // down — the next registration, or the null "no backend" path, handles it.
                    Log.Debug("Camera control backend failed to initialize: " + e.Message);
                }
            }
        }

        return null;
    }
}
