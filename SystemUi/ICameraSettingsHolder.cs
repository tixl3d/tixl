namespace T3.SystemUi;

/// <summary>Which tab of the camera settings window a control belongs to.</summary>
public enum CameraSettingGroup
{
    /// <summary>Image controls — brightness, contrast, white balance... (DirectShow's "Video Proc Amp").</summary>
    Image,

    /// <summary>Optics and motion — pan, tilt, zoom, focus, exposure...</summary>
    Camera,

    /// <summary>Anything else the driver reports (mostly extra V4L2 controls on Linux).</summary>
    Other,
}

public enum CameraSettingKind
{
    Integer,
    Boolean,
    Menu,
    Button,
}

/// <summary>One menu entry: the value written to the camera and its label.</summary>
public readonly record struct CameraSettingItem(int Value, string Label);

/// <summary>
/// One camera control as the settings window shows it, with the range, default and live state the device
/// reports. Standard controls the camera lacks are listed too, with <see cref="IsSupported"/> false, so the
/// window keeps the familiar layout and grays them out. Immutable; a holder hands out a new list when anything
/// changes.
/// </summary>
public sealed record CameraSetting(
    string Name,
    CameraSettingGroup Group,
    CameraSettingKind Kind,
    int Minimum,
    int Maximum,
    int Step,
    int DefaultValue,
    int? CurrentValue,
    bool SupportsAuto,
    bool IsAuto,
    bool IsReadOnly,
    IReadOnlyList<CameraSettingItem> Items,
    bool IsSupported = true);

/// <summary>
/// Where an operator keeps its capture format, so the settings window can change it as an undoable edit.
/// The mode-type input switches between the device default (<see cref="DefaultModeType"/>) and the
/// resolution / frame rate inputs (<see cref="CustomModeType"/>); a zero size or frame rate leaves that
/// part to the camera.
/// </summary>
public readonly record struct CameraFormatInputs(
    Guid ModeTypeInputId,
    int DefaultModeType,
    int CustomModeType,
    Guid ResolutionInputId,
    Guid FpsInputId);

/// <summary>
/// An operator whose camera can be adjusted in the editor's camera settings window — the cross-platform
/// stand-in for the Windows driver's "Video Capture Filter Properties" dialog.
/// All members are called on the main thread.
/// </summary>
public interface ICameraSettingsHolder
{
    /// <summary>Name of the camera currently being controlled, empty while none is resolved.</summary>
    string CameraSettingsDeviceName { get; }

    /// <summary>Why <see cref="GetCameraSettings"/> is empty, or the outcome of the last change.</summary>
    string CameraSettingsStatus { get; }

    /// <summary>
    /// The controls the camera reports, or an empty list while they are still being read. The list instance
    /// stays the same until something changes, so callers may cache derived data by reference.
    /// </summary>
    IReadOnlyList<CameraSetting> GetCameraSettings();

    /// <summary>Sends a value (lands as manual) or an auto-mode switch to the camera right away.</summary>
    void ApplyCameraSetting(string name, int? value, bool? auto);

    /// <summary>
    /// The settings stored with the project — the values the window commits with OK / Apply and that are
    /// re-applied whenever the camera opens. Serialized by the holder; the window treats it as opaque.
    /// </summary>
    Guid StoredCameraSettingsInputId { get; }

    /// <summary>Serializes the given state in the format <see cref="StoredCameraSettingsInputId"/> stores.</summary>
    string SerializeCameraSettings(IReadOnlyDictionary<string, (int Value, bool IsAuto)> settings);

    /// <summary>
    /// The capture modes the camera offers natively, largest first. Read in the background: empty until the
    /// first read finished, and the same list instance until something changes.
    /// </summary>
    IReadOnlyList<CameraMode> GetCameraModes();

    /// <summary>The mode the operator asks for; a zero size or frame rate means the camera's default.</summary>
    CameraMode RequestedCameraMode { get; }

    /// <summary>The size frames actually arrive in, zero before the first frame.</summary>
    CameraMode ActiveCameraMode { get; }

    CameraFormatInputs FormatInputs { get; }
}

/// <summary>
/// Lets an operator ask the editor to open its camera settings window. The editor sets <see cref="OpenWindow"/>
/// at startup; in the Player there is no window and <see cref="TryOpen"/> returns false.
/// </summary>
public static class CameraSettingsWindow
{
    public static bool TryOpen(ICameraSettingsHolder holder)
    {
        var open = OpenWindow;
        if (open == null)
            return false;

        open(holder);
        return true;
    }

    public static Action<ICameraSettingsHolder>? OpenWindow { get; set; }
}
