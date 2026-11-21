#nullable enable
using System.Text.Json;
using T3.SystemUi;

namespace Lib.io.video;

/// <summary>
/// Everything [WebcamInput] does with camera controls: finds the native device behind the SDL camera name,
/// owns the <see cref="CameraControlWorker"/>, re-applies the settings stored with the project, drives the
/// Control1..4 slots, and serves the settings window. Frames keep coming through SDL; this only talks to the
/// driver's control interface, and only once something actually asks for a control.
/// <para>Main thread only — device I/O happens on the worker.</para>
/// </summary>
internal sealed class CameraControlSession : IDisposable
{
    public const int SlotCount = 4;
    public const string NoControlOption = "(None)";

    /// <summary>The control's value mapped to 0..1 over its range — what a slot receives when the
    /// operator's NormalizedControlValues is on.</summary>
    public static float Normalize(CameraControlDescriptor control, int value)
    {
        var span = control.Maximum - control.Minimum;
        return span > 0 ? Math.Clamp((value - control.Minimum) / (float)span, 0f, 1f) : 0f;
    }

    /// <summary>Human-readable list of every reported control with range and live value.</summary>
    public string CatalogText => _worker?.CatalogText ?? string.Empty;

    /// <summary>The latest control failure, empty if none — surfaced in the operator status.</summary>
    public string LastWarning { get; private set; } = string.Empty;

    public string DeviceName => _device?.Name ?? string.Empty;

    public string Status
    {
        get
        {
            if (_device == null)
            {
                return _resolveFailed
                           ? "No camera with adjustable controls was found for this input."
                           : "Waiting for the camera...";
            }

            var worker = GetOrCreateWorker();
            var message = worker.LastControlMessage;
            if (message.Length > 0)
                return message;

            return worker.CurrentCatalog == null
                       ? worker.CatalogText.Length > 0 ? worker.CatalogText : "Reading camera controls..."
                       : string.Empty;
        }
    }

    /// <summary>Called once per evaluation. Allocation-free unless the device or the stored settings change.</summary>
    public void Update(string? cameraName, bool reconnect, string? storedSettings,
                       string?[] slotNames, float[] slotValues, bool normalized)
    {
        // Nothing asks for a control yet: leave the driver alone entirely.
        if (!_isWanted && string.IsNullOrEmpty(storedSettings) && !HasAnySlot(slotNames))
            return;

        _isWanted = true;
        cameraName ??= string.Empty;

        CompleteResolve();

        var now = Environment.TickCount64;
        var nameChanged = !string.Equals(cameraName, _resolvedForName, StringComparison.Ordinal);
        var retryDue = _device == null && now - _lastResolveAttemptMs > ResolveRetryIntervalMs;
        _isReconnectPending |= reconnect;
        if ((_isReconnectPending || nameChanged || retryDue) && _resolveTask == null)
            StartResolve(cameraName, now);

        if (_device == null || _worker == null)
            return;

        if (!string.Equals(storedSettings, _appliedStoredSettings, StringComparison.Ordinal))
        {
            ApplyStoredSettings(storedSettings);
            _appliedStoredSettings = storedSettings;
        }

        // Slots go after the stored settings so a slot wins when both target one control in the same frame —
        // the worker keeps only the newest apply per control.
        ApplySlots(slotNames, slotValues, normalized);

        var message = _worker.LastControlMessage;
        if (!ReferenceEquals(message, _lastSeenControlMessage))
        {
            _lastSeenControlMessage = message;
            LastWarning = message.StartsWith("Warning", StringComparison.Ordinal) ? message : string.Empty;
        }
    }

    /// <summary>Makes the session read the camera's controls even if no slot or stored setting needs them yet.</summary>
    public void RequestControls() => _isWanted = true;

    public CameraControlDescriptor? FindControl(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        return _worker?.CurrentCatalog?.Find(name);
    }

    public IEnumerable<string> GetControlNames()
    {
        RequestControls();
        yield return NoControlOption;

        var catalog = _worker?.CurrentCatalog;
        if (catalog == null)
            yield break;

        foreach (var control in catalog.Controls)
        {
            if ((control.States & CameraControlStates.ReadOnly) == 0)
                yield return control.Name;
        }
    }

    public IReadOnlyList<CameraSetting> GetSettings()
    {
        RequestControls();
        var catalog = _worker?.CurrentCatalog;
        if (catalog == null)
            return [];

        if (!ReferenceEquals(catalog, _settingsSource))
        {
            _settingsSource = catalog;
            _settings = BuildSettings(catalog);
        }

        return _settings;
    }

    public void Apply(string name, int? value, bool? auto)
    {
        if (_device == null)
            return;

        GetOrCreateWorker().EnqueueApply(_device, name, value, auto);
    }

    public static string Serialize(IReadOnlyDictionary<string, (int Value, bool IsAuto)> settings)
    {
        var stored = new SortedDictionary<string, StoredSetting>(StringComparer.Ordinal);
        foreach (var (name, (value, isAuto)) in settings)
            stored[name] = new StoredSetting(value, isAuto);

        return JsonSerializer.Serialize(stored);
    }

    public void Dispose()
    {
        // Not joined: a DirectShow call into a wedged camera can block, and the worker exits on its own.
        _worker?.Stop();
        _worker = null;
    }

    private static bool HasAnySlot(string?[] slotNames)
    {
        foreach (var name in slotNames)
        {
            if (!string.IsNullOrEmpty(name))
                return true;
        }

        return false;
    }

    /** Device listing talks to the OS (DirectShow, sysfs, AVFoundation) and can stall on a bad driver, so it
     * runs on the thread pool; the evaluation thread only picks up the result. */
    private void StartResolve(string cameraName, long now)
    {
        _lastResolveAttemptMs = now;
        _resolvedForName = cameraName;
        _isReconnectPending = false;
        _resolveTask = Task.Run(() => CameraDeviceDirectory.TryResolve(cameraName, out var device) ? device : null);
    }

    private void CompleteResolve()
    {
        var task = _resolveTask;
        if (task is not { IsCompleted: true })
            return;

        _resolveTask = null;
        var device = task.IsCompletedSuccessfully ? task.Result : null;

        var previous = _device;
        _resolveFailed = device == null;
        _device = device;
        if (device == null)
            return;

        var worker = GetOrCreateWorker();
        if (previous != null)
            worker.InvalidateHandles(previous);

        // A camera replaced at the same index must not be driven through the old handle.
        worker.InvalidateHandles(device);
        worker.EnqueueRefresh(device);

        // A new or reconnected camera starts from its own state: send the stored settings and slots again.
        _appliedStoredSettings = null;
        for (var slotIndex = 0; slotIndex < SlotCount; slotIndex++)
        {
            _lastSentSlotNames[slotIndex] = null;
            _lastSentSlotValues[slotIndex] = int.MinValue;
        }
    }

    private void ApplyStoredSettings(string? storedSettings)
    {
        if (string.IsNullOrWhiteSpace(storedSettings))
            return;

        Dictionary<string, StoredSetting>? stored;
        try
        {
            stored = JsonSerializer.Deserialize<Dictionary<string, StoredSetting>>(storedSettings);
        }
        catch (JsonException e)
        {
            LastWarning = $"Warning: Stored camera settings could not be read: {e.Message}";
            return;
        }

        if (stored == null)
            return;

        foreach (var (name, setting) in stored)
        {
            if (setting.Auto)
                _worker!.EnqueueApply(_device!, name, null, true);
            else
                _worker!.EnqueueApply(_device!, name, setting.Value, null);
        }
    }

    private void ApplySlots(string?[] slotNames, float[] slotValues, bool normalized)
    {
        for (var slotIndex = 0; slotIndex < SlotCount; slotIndex++)
        {
            var name = slotNames[slotIndex];
            if (string.IsNullOrEmpty(name) || name == NoControlOption)
            {
                _lastSentSlotNames[slotIndex] = null;
                continue;
            }

            int rawValue;
            if (normalized)
            {
                // The range comes from the device, so normalized slots wait for the catalog.
                var control = _worker!.CurrentCatalog?.Find(name);
                if (control == null || control.Maximum <= control.Minimum)
                    continue;

                var step = Math.Max(control.Step, 1);
                var unsnapped = control.Minimum + Math.Clamp(slotValues[slotIndex], 0f, 1f) * (control.Maximum - control.Minimum);
                var steps = (int)MathF.Round((unsnapped - control.Minimum) / step);
                rawValue = Math.Min(control.Minimum + steps * step, control.Maximum);
            }
            else
            {
                rawValue = (int)MathF.Round(slotValues[slotIndex]);
            }

            if (rawValue == _lastSentSlotValues[slotIndex]
                && string.Equals(name, _lastSentSlotNames[slotIndex], StringComparison.Ordinal))
                continue;

            _lastSentSlotNames[slotIndex] = name;
            _lastSentSlotValues[slotIndex] = rawValue;
            _worker!.EnqueueApply(_device!, name, rawValue, null);
        }
    }

    private CameraControlWorker GetOrCreateWorker() => _worker ??= new CameraControlWorker();

    /** The rows of the driver's own property pages, in their order; rows the camera lacks are shown grayed out. */
    private static readonly string[] _standardControlNames =
        [
            CameraControlNames.Brightness, CameraControlNames.Contrast, CameraControlNames.Hue, CameraControlNames.Saturation,
            CameraControlNames.Sharpness, CameraControlNames.Gamma, CameraControlNames.WhiteBalance,
            CameraControlNames.BacklightCompensation, CameraControlNames.Gain, CameraControlNames.ColorEnable,
            CameraControlNames.PowerLineFrequency,
            CameraControlNames.Zoom, CameraControlNames.Focus, CameraControlNames.Exposure, CameraControlNames.Iris,
            CameraControlNames.Pan, CameraControlNames.Tilt, CameraControlNames.Roll,
        ];

    private static CameraSetting[] BuildSettings(CameraControlCatalog catalog)
    {
        // Linux reports auto modes as controls of their own; the window shows them as the Auto checkbox of
        // the control they belong to instead of a second row.
        var autoPartners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var control in catalog.Controls)
        {
            if (control.AutoControlName.Length > 0)
                autoPartners.Add(control.AutoControlName);
        }

        var settings = new List<CameraSetting>(catalog.Controls.Count + _standardControlNames.Length);
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in _standardControlNames)
        {
            listed.Add(name);
            var control = catalog.Find(name);
            settings.Add(control != null
                             ? ToSetting(control)
                             : new CameraSetting(name, GetGroup(name), CameraSettingKind.Integer, 0, 0, 1, 0, null,
                                                 false, false, true, [], IsSupported: false));
        }

        foreach (var control in catalog.Controls)
        {
            if (!listed.Contains(control.Name) && !autoPartners.Contains(control.Name))
                settings.Add(ToSetting(control));
        }

        return [.. settings];
    }

    private static CameraSetting ToSetting(CameraControlDescriptor control)
    {
        var items = new CameraSettingItem[control.Items.Length];
        for (var itemIndex = 0; itemIndex < items.Length; itemIndex++)
            items[itemIndex] = new CameraSettingItem(control.Items[itemIndex].Value, control.Items[itemIndex].Label);

        var kind = control.Kind switch
                       {
                           CameraControlKind.Boolean                              => CameraSettingKind.Boolean,
                           CameraControlKind.Menu or CameraControlKind.IntegerMenu => CameraSettingKind.Menu,
                           CameraControlKind.Button                               => CameraSettingKind.Button,
                           _                                                      => CameraSettingKind.Integer,
                       };

        return new CameraSetting(control.Name,
                                 GetGroup(control.Name),
                                 kind,
                                 control.Minimum,
                                 control.Maximum,
                                 Math.Max(control.Step, 1),
                                 control.DefaultValue,
                                 control.CurrentValue,
                                 control.SupportsAuto,
                                 control.CurrentIsAuto,
                                 (control.States & CameraControlStates.ReadOnly) != 0,
                                 items);
    }

    private static CameraSettingGroup GetGroup(string name)
    {
        switch (name)
        {
            case CameraControlNames.Brightness:
            case CameraControlNames.Contrast:
            case CameraControlNames.Hue:
            case CameraControlNames.Saturation:
            case CameraControlNames.Sharpness:
            case CameraControlNames.Gamma:
            case CameraControlNames.WhiteBalance:
            case CameraControlNames.WhiteBalanceAuto:
            case CameraControlNames.BacklightCompensation:
            case CameraControlNames.Gain:
            case CameraControlNames.ColorEnable:
            case CameraControlNames.PowerLineFrequency:
                return CameraSettingGroup.Image;

            case CameraControlNames.Pan:
            case CameraControlNames.Tilt:
            case CameraControlNames.Roll:
            case CameraControlNames.Zoom:
            case CameraControlNames.Focus:
            case CameraControlNames.FocusAuto:
            case CameraControlNames.Iris:
            case CameraControlNames.Exposure:
            case CameraControlNames.ExposureAuto:
            case CameraControlNames.ExposureAutoPriority:
                return CameraSettingGroup.Camera;

            default:
                return CameraSettingGroup.Other;
        }
    }

    private sealed record StoredSetting(int Value, bool Auto);

    private const long ResolveRetryIntervalMs = 2000;

    private CameraControlWorker? _worker;
    private WebcamWithIndex? _device;
    private bool _isWanted;
    private bool _resolveFailed;
    private string? _resolvedForName;
    private long _lastResolveAttemptMs = long.MinValue / 2;
    private Task<WebcamWithIndex?>? _resolveTask;
    private bool _isReconnectPending;
    private string? _appliedStoredSettings;
    private string _lastSeenControlMessage = string.Empty;

    private readonly string?[] _lastSentSlotNames = new string?[SlotCount];
    private readonly int[] _lastSentSlotValues = [int.MinValue, int.MinValue, int.MinValue, int.MinValue];

    private CameraControlCatalog? _settingsSource;
    private IReadOnlyList<CameraSetting> _settings = [];
}
