#nullable enable
using DirectShowLib;

namespace Lib.io.video;

/// <summary>
/// The Windows camera-control backend: a fixed-catalog sweep over DirectShow's two control interfaces —
/// all ten <c>IAMVideoProcAmp</c> properties (including ColorEnable, which the old fixed enum backend never
/// mapped) and all seven <c>IAMCameraControl</c> properties — asking each for its range via
/// <c>GetRange</c> and omitting whatever the device answers E_PROP_ID_UNSUPPORTED to. Auto/Manual is a
/// per-property flag in DirectShow (there are no separate auto controls): <c>Get</c>'s flags out-parameter
/// is the live auto state — the checkbox the old code discarded — and <c>Set(prop, value, Manual)</c>
/// lands the value as Manual, implicitly disengaging auto, so the shared "value implies Manual" apply rule
/// needs no partner step here.
///
/// Scope is exactly the old per-call reach, minus the per-call chattiness: monikers from
/// <c>DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice)</c> (index identity = moniker order, the
/// same order WebcamScan caches) bound to <c>IBaseFilter</c> and cached per device index. The control
/// worker is this class's only caller (the backend contract) and it CoInitializeEx(COINIT_MULTITHREADED)s
/// its thread at start and releases the cached filters through <see cref="InvalidateHandles"/> at exit —
/// so every COM object here is created, queried and released on that one thread. A cached filter that
/// errors is dropped and reopened once (a camera swapped at the same index invalidates it); a
/// GetRange/Get into a wedged or just-unplugged filter can block far past the worker's join — that is
/// covered by the worker's orphaned-thread treatment, not here. No property-page dialog
/// (ISpecifyPropertyPages is a follow-up), no MediaFoundation.
/// </summary>
internal sealed class DirectShowCameraControls : ICameraControlBackend
{
    private const string BackendIdPrefix = "dshow:";

    /// <summary>Which interface family a <see cref="CameraControlDescriptor.BackendId"/> addresses, and the
    /// property's underlying enum value.</summary>
    private enum ControlFamily
    {
        VideoProcAmp,
        CameraControl,
    }

    private readonly record struct DirectShowProperty(ControlFamily Family, int Value);

    // The bound capture filters, keyed by device index. Created, used and released on the control worker's
    // thread only — the backend contract — so the dictionary is deliberately not thread-safe and unlocked.
    private readonly Dictionary<int, IBaseFilter> _filters = new();

    public CameraControlCatalog? EnumerateCatalog(WebcamWithIndex device)
    {
        var controls = SweepProperties(device.Index, out var sweepFailed);
        if (!sweepFailed)
        {
            if (controls.Count > 0)
                return new CameraControlCatalog(device, DateTime.Now, controls);

            // An empty sweep from a cached filter is ambiguous: a camera replaced at the same index leaves the
            // cached filter dead, and a dead filter reports nothing. Rebind once before concluding the device
            // has no controls — a freshly bound filter answering empty is authoritative.
            if (!_filters.ContainsKey(device.Index))
                return null;
        }

        // A sweep that threw mid-way (e.g. a stale RCW on one of the QI casts) leaves a PARTIAL list —
        // publishing it as the catalog would silently drop the remaining control family. It gets the same
        // rebind-once retry the empty path uses; the fresh bind's answer (complete list, or empty) is the
        // authoritative catalog.
        ReleaseFilter(device.Index);
        controls = SweepProperties(device.Index, out _);
        return controls.Count > 0 ? new CameraControlCatalog(device, DateTime.Now, controls) : null;
    }

    public bool TryGetValue(WebcamWithIndex device, CameraControlDescriptor control, out int value, out bool isAuto)
    {
        value = 0;
        isAuto = false;
        if (!TryParseBackendId(control.BackendId, out var property))
            return false;

        var readValue = 0;
        var readIsAuto = false;
        var ok = RunWithFilter(device, filter => ReadValue(filter, property, out readValue, out readIsAuto));
        if (ok)
        {
            value = readValue;
            isAuto = readIsAuto;
        }

        return ok;
    }

    public bool TrySetValue(WebcamWithIndex device, CameraControlDescriptor control, int value)
    {
        if (!TryParseBackendId(control.BackendId, out var property))
            return false;

        // Set(prop, value, Manual) IS the shared apply rule: the Manual flag disengages the property's auto
        // mode in the same call — DirectShow has no separate auto control to write first.
        return RunWithFilter(device, filter => WriteValue(filter, property, value, useAuto: false));
    }

    public bool TrySetAuto(WebcamWithIndex device, CameraControlDescriptor control, bool auto)
    {
        if (!TryParseBackendId(control.BackendId, out var property))
            return false;

        return RunWithFilter(device, filter =>
        {
            if (auto)
                return WriteValue(filter, property, 0, useAuto: true);

            // DirectShow has no "leave auto" primitive — manual requires a value, so re-apply the live
            // value as Manual (Get answers even while auto is engaged); the descriptor's default as fallback.
            if (!ReadValue(filter, property, out var current, out _))
                current = control.DefaultValue;

            return WriteValue(filter, property, current, useAuto: false);
        });
    }

    public void InvalidateHandles(WebcamWithIndex device)
        => ReleaseFilter(device.Index);

    private static bool ReadValue(IBaseFilter filter, DirectShowProperty property, out int value, out bool isAuto)
    {
        value = 0;
        isAuto = false;

        if (property.Family == ControlFamily.CameraControl)
        {
            var cameraControl = filter as IAMCameraControl; // QI cast; null = family not offered by this filter
            if (cameraControl == null)
                return false;

            var hr = cameraControl.Get((CameraControlProperty)property.Value, out value, out var flags);
            isAuto = (flags & CameraControlFlags.Auto) != 0; // the live auto state, read back to the catalog
            return hr == 0;
        }

        var procAmp = filter as IAMVideoProcAmp;
        if (procAmp == null)
            return false;

        var procAmpHr = procAmp.Get((VideoProcAmpProperty)property.Value, out value, out var procAmpFlags);
        isAuto = (procAmpFlags & VideoProcAmpFlags.Auto) != 0;
        return procAmpHr == 0;
    }

    private static bool WriteValue(IBaseFilter filter, DirectShowProperty property, int value, bool useAuto)
    {
        if (property.Family == ControlFamily.CameraControl)
        {
            var cameraControl = filter as IAMCameraControl;
            if (cameraControl == null)
                return false;

            return cameraControl.Set((CameraControlProperty)property.Value, value,
                                     useAuto ? CameraControlFlags.Auto : CameraControlFlags.Manual) == 0;
        }

        var procAmp = filter as IAMVideoProcAmp;
        if (procAmp == null)
            return false;

        return procAmp.Set((VideoProcAmpProperty)property.Value, value,
                           useAuto ? VideoProcAmpFlags.Auto : VideoProcAmpFlags.Manual) == 0;
    }

    private List<CameraControlDescriptor> SweepProperties(int deviceIndex, out bool threw)
    {
        // The fixed catalog: every property both interfaces define, in enum order. What the device actually
        // implements is whatever survives GetRange — the rest answer E_PROP_ID_UNSUPPORTED and are omitted.
        var controls = new List<CameraControlDescriptor>(capacity: 17);
        threw = false;
        try
        {
            var filter = GetOrBindFilter(deviceIndex);
            if (filter == null)
                return controls;

            var procAmp = filter as IAMVideoProcAmp;
            if (procAmp != null)
            {
                foreach (var property in Enum.GetValues<VideoProcAmpProperty>()) // Brightness..Gain, incl. ColorEnable
                {
                    if (TryDescribeVideoProcAmp(procAmp, property, out var descriptor))
                        controls.Add(descriptor);
                }
            }

            var cameraControl = filter as IAMCameraControl;
            if (cameraControl != null)
            {
                foreach (var property in Enum.GetValues<CameraControlProperty>()) // Pan..Focus
                {
                    if (TryDescribeCameraControl(cameraControl, property, out var descriptor))
                        controls.Add(descriptor);
                }
            }
        }
        catch (Exception e)
        {
            // A wholesale failure usually means the cached filter died (device swapped at the same index).
            // Flag it so EnumerateCatalog rebinds and retries once for exactly this case — returning the
            // partial list as if authoritative would drop the remaining control family until the next
            // refresh trigger.
            threw = true;
            Log.Debug($"DirectShow: control sweep of device {deviceIndex} failed: {e.Message}");
        }

        return controls;
    }

    private static bool TryDescribeVideoProcAmp(IAMVideoProcAmp procAmp, VideoProcAmpProperty property,
                                                [NotNullWhen(true)] out CameraControlDescriptor? descriptor)
    {
        descriptor = null;
        try
        {
            var hr = procAmp.GetRange(property, out var min, out var max, out var step, out var defaultValue, out _);
            if (hr != 0)
                return false; // E_PROP_ID_UNSUPPORTED and friends — the device does not offer this property

            descriptor = new CameraControlDescriptor
            {
                // The enum names are the canonical vocabulary, 1:1 with CameraControlNames.
                Name = property.ToString(),
                Kind = IsBooleanRange(min, max, step) ? CameraControlKind.Boolean : CameraControlKind.Integer,
                Minimum = min,
                Maximum = max,
                Step = Math.Max(step, 1), // some drivers report a zero stepping delta
                DefaultValue = defaultValue,
                AutoControlName = "", // DirectShow auto state lives in each property's flag, not a partner
                BackendId = $"{BackendIdPrefix}VideoProcAmp.{property}",
            };
            return true;
        }
        catch (Exception e)
        {
            Log.Debug($"DirectShow: GetRange({property}) failed: {e.Message}");
            return false;
        }
    }

    private static bool TryDescribeCameraControl(IAMCameraControl cameraControl, CameraControlProperty property,
                                                 [NotNullWhen(true)] out CameraControlDescriptor? descriptor)
    {
        descriptor = null;
        try
        {
            var hr = cameraControl.GetRange(property, out var min, out var max, out var step, out var defaultValue, out _);
            if (hr != 0)
                return false;

            descriptor = new CameraControlDescriptor
            {
                Name = property.ToString(),
                Kind = IsBooleanRange(min, max, step) ? CameraControlKind.Boolean : CameraControlKind.Integer,
                Minimum = min,
                Maximum = max,
                Step = Math.Max(step, 1),
                DefaultValue = defaultValue,
                AutoControlName = "",
                BackendId = $"{BackendIdPrefix}CameraControl.{property}",
            };
            return true;
        }
        catch (Exception e)
        {
            Log.Debug($"DirectShow: GetRange({property}) failed: {e.Message}");
            return false;
        }
    }

    // A reported range of exactly 0..1 step 1 is a checkbox — how ColorEnable and low-end
    // BacklightCompensation implementations present.
    private static bool IsBooleanRange(int min, int max, int step)
        => min == 0 && max == 1 && step == 1;

    private static bool TryParseBackendId(string backendId, out DirectShowProperty property)
    {
        property = default;
        // "dshow:VideoProcAmp.WhiteBalance" / "dshow:CameraControl.Pan" — the exact shape the mint sites
        // in this file produce ($"{BackendIdPrefix}{family}.{property}" with BackendIdPrefix = "dshow:").
        var prefixSplit = backendId.Split(':');
        if (prefixSplit.Length != 2 || prefixSplit[0] != "dshow")
            return false;

        var parts = prefixSplit[1].Split('.');
        if (parts.Length != 2)
            return false;

        if (parts[0] == "VideoProcAmp" && Enum.TryParse(parts[1], out VideoProcAmpProperty procAmpProperty))
        {
            property = new DirectShowProperty(ControlFamily.VideoProcAmp, (int)procAmpProperty);
            return true;
        }

        if (parts[0] == "CameraControl" && Enum.TryParse(parts[1], out CameraControlProperty cameraProperty))
        {
            property = new DirectShowProperty(ControlFamily.CameraControl, (int)cameraProperty);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Runs one control operation against the device's cached (or freshly bound) filter. A clean "no" from
    /// the device (unsupported property, failed HRESULT) is accepted as false; a thrown exception is treated
    /// as a possibly-dead cached handle — the filter is released and the operation retried once against a
    /// fresh bind, so a same-index camera swap costs one failed call instead of failing forever.
    /// </summary>
    private bool RunWithFilter(WebcamWithIndex device, Func<IBaseFilter, bool> action)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var filter = GetOrBindFilter(device.Index);
            if (filter == null)
                return false;

            var ok = false;
            var fatal = false;
            try
            {
                ok = action(filter);
            }
            catch (Exception e)
            {
                Log.Debug($"DirectShow: control call on '{device.Name}' failed: {e.Message}");
                fatal = true; // a COM-level failure suggests a stale or wedged filter, not an absent property
            }

            if (ok)
                return true;

            if (!fatal)
                return false; // the device answered — rebinding would not change its answer

            ReleaseFilter(device.Index);
        }

        return false;
    }

    private IBaseFilter? GetOrBindFilter(int deviceIndex)
    {
        if (_filters.TryGetValue(deviceIndex, out var cached))
            return cached;

        try
        {
            // The same enumeration WebcamScan caches — index identity is moniker order on both sides.
            var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            if ((uint)deviceIndex >= (uint)devices.Length)
                return null;

            var filterGuid = typeof(IBaseFilter).GUID;
            devices[deviceIndex].Mon.BindToObject(null!, null!, ref filterGuid, out var filter);
            if (filter is not IBaseFilter captureFilter)
                return null;

            _filters.Add(deviceIndex, captureFilter);
            return captureFilter;
        }
        catch (Exception e)
        {
            Log.Debug($"DirectShow: failed to bind capture filter for device {deviceIndex}: {e.Message}");
            return null;
        }
    }

    /// <summary>Releases and forgets the cached filter — idempotent; the single release path for every
    /// cached instance (invalidate, dead-handle retry, worker shutdown).</summary>
    private void ReleaseFilter(int deviceIndex)
    {
        if (!_filters.Remove(deviceIndex, out var filter))
            return;

        try
        {
            Marshal.ReleaseComObject(filter);
        }
        catch (Exception e)
        {
            // A stale RCW that was already disconnected refuses the release — nothing to salvage.
            Log.Debug($"DirectShow: releasing capture filter for device {deviceIndex} failed: {e.Message}");
        }
    }

    /// <summary>
    /// Registers this backend for Windows. Runs once at assembly load on every platform and only stores
    /// delegates — DirectShow-specific work starts at the first <see cref="CameraControlBackendRegistry.Create"/>
    /// inside a Windows process (the whole file compiles on every platform; it only ever executes on Windows).
    /// </summary>
    [ModuleInitializer]
    internal static void RegisterBackend()
        => CameraControlBackendRegistry.Register(OperatingSystem.IsWindows, () => new DirectShowCameraControls());
}
