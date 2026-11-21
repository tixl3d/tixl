#nullable enable
using System.Runtime.InteropServices;

namespace Lib.io.video;

/// <summary>
/// macOS camera-control backend: raw ObjC interop with <c>AVCaptureDevice</c>, following
/// <see cref="MacCameraPermission"/>'s interop pattern (framework load at the absolute path,
/// <c>objc_getClass</c>/<c>sel_registerName</c>/<c>objc_msgSend</c>, CFString helpers) — but redeclaring
/// every DllImport here, because <c>MacCameraPermission.cs</c> is read-only for every work unit
/// (CameraControls-Design.md §6).
///
/// v1 scope (CameraControls-Design.md §5.3) is scalar selectors only and reports exactly what AVFoundation
/// exposes, honestly omitting everything it does not (no pan/tilt/roll/iris/gain/sharpness/gamma/
/// backlight/powerline — there are no such selectors):
/// <list type="bullet">
/// <item><description>Zoom — <c>videoZoomFactor</c>, reported as 100 = 1× (<see cref="CameraControlDescriptor.Scale"/>),
/// clamped to <c>minAvailableVideoZoomFactor</c>..<c>maxAvailableVideoZoomFactor</c>; no auto mode exists.</description></item>
/// <item><description>Focus — <c>focusMode</c> menu (0 Locked / 1 Auto / 2 Continuous, auto = mode ≠ 0),
/// gated by <c>isFocusModeSupported:</c>.</description></item>
/// <item><description>Exposure — <c>exposureMode</c> menu (0 Locked / 1 Auto / 2 Continuous / 3 Custom,
/// auto = 1 or 2), gated by <c>isExposureModeSupported:</c>.</description></item>
/// <item><description>WhiteBalance — <c>whiteBalanceMode</c> menu (0 Locked / 1 Auto / 2 Continuous,
/// auto = 1 or 2), gated by <c>isWhiteBalanceModeSupported:</c>.</description></item>
/// </list>
/// Custom exposure duration/ISO (a <c>CMTime</c> argument) and device white-balance gains (a struct return)
/// need stret/struct-by-value marshaling and stay on the documented follow-up ladder. All writes run under
/// <c>lockForConfiguration:</c> (NULL NSError out) / <c>unlockForConfiguration:</c>, and failures — a refused
/// lock, an unsupported mode — surface as a <c>false</c> (the worker turns that into a Status warning),
/// never a silent no-op. Devices resolve by <c>localizedName</c>, the same names WebcamScan's avfoundation
/// list prints; without camera permission the device list is empty and the catalog comes back empty, where
/// the op's existing permission messaging takes over.
/// </summary>
internal sealed class CameraControlsAvFoundation : ICameraControlBackend
{
    private const string BackendIdZoom = "avf:videoZoomFactor";
    private const string BackendIdFocus = "avf:focusMode";
    private const string BackendIdExposure = "avf:exposureMode";
    private const string BackendIdWhiteBalance = "avf:whiteBalanceMode";

    // AVCaptureFocusMode / AVCaptureExposureMode / AVCaptureWhiteBalanceMode — the enum values ARE the
    // menu values the mode menus report and accept (CameraControls-Design.md §5.3 table).
    private const long ModeLocked = 0;
    private const long ModeAuto = 1;
    private const long ModeContinuous = 2;
    private const long ModeExposureCustom = 3;

    // Zoom is reported in "100 = 1×" units (the UVC convention the fixed Zoom input was written against);
    // videoZoomFactor itself is a continuous CGFloat that starts at 1.0 (Apple docs — hence default 100).
    private const double ZoomUnitsPerFactor = 100.0;
    private const int ZoomDefaultUnits = 100;

    private const string LibObjc = "/usr/lib/libobjc.A.dylib";
    private const string LibCoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int KcfStringEncodingUtf8 = 0x08000100;

    private readonly bool _available;

    /** The zoom selectors are documented for iOS/Mac Catalyst only; sending one the macOS class lacks raises an
     * ObjC exception that aborts the process, so they are only used when the runtime reports them. */
    private readonly bool _supportsZoom;

    // Class + selector handles, resolved once per backend instance. These are instance fields (not static
    // initializers) on purpose: the module initializer below touches this type on every platform, and no
    // libobjc call may happen before the registry's macOS platform check has gated construction.
    private readonly IntPtr _captureDeviceClass;
    private readonly IntPtr _selDevices;
    private readonly IntPtr _selCount;
    private readonly IntPtr _selObjectAtIndex;
    private readonly IntPtr _selLocalizedName;
    private readonly IntPtr _selVideoZoomFactor;
    private readonly IntPtr _selSetVideoZoomFactor;
    private readonly IntPtr _selMinZoom;
    private readonly IntPtr _selMaxZoom;
    private readonly IntPtr _selLockForConfiguration;
    private readonly IntPtr _selUnlockForConfiguration;
    private readonly IntPtr _selFocusMode;
    private readonly IntPtr _selSetFocusMode;
    private readonly IntPtr _selFocusModeSupported;
    private readonly IntPtr _selExposureMode;
    private readonly IntPtr _selSetExposureMode;
    private readonly IntPtr _selExposureModeSupported;
    private readonly IntPtr _selWhiteBalanceMode;
    private readonly IntPtr _selSetWhiteBalanceMode;
    private readonly IntPtr _selWhiteBalanceModeSupported;

    public CameraControlsAvFoundation()
    {
        // FFmpeg's avfoundation demuxer has usually already pulled AVFoundation into the process, making
        // this a no-op; a run without FFmpeg still finds the framework at its absolute path (same decision
        // as MacCameraPermission.cs:44).
        if (!NativeLibrary.TryLoad("/System/Library/Frameworks/AVFoundation.framework/AVFoundation", out _))
            return;

        _captureDeviceClass = objc_getClass("AVCaptureDevice");
        if (_captureDeviceClass == IntPtr.Zero)
            return;

        _selDevices = sel_registerName("devices");
        _selCount = sel_registerName("count");
        _selObjectAtIndex = sel_registerName("objectAtIndex:");
        _selLocalizedName = sel_registerName("localizedName");
        _selVideoZoomFactor = sel_registerName("videoZoomFactor");
        _selSetVideoZoomFactor = sel_registerName("setVideoZoomFactor:");
        _selMinZoom = sel_registerName("minAvailableVideoZoomFactor");
        _selMaxZoom = sel_registerName("maxAvailableVideoZoomFactor");
        _selLockForConfiguration = sel_registerName("lockForConfiguration:");
        _selUnlockForConfiguration = sel_registerName("unlockForConfiguration");
        _selFocusMode = sel_registerName("focusMode");
        _selSetFocusMode = sel_registerName("setFocusMode:");
        _selFocusModeSupported = sel_registerName("isFocusModeSupported:");
        _selExposureMode = sel_registerName("exposureMode");
        _selSetExposureMode = sel_registerName("setExposureMode:");
        _selExposureModeSupported = sel_registerName("isExposureModeSupported:");
        _selWhiteBalanceMode = sel_registerName("whiteBalanceMode");
        _selSetWhiteBalanceMode = sel_registerName("setWhiteBalanceMode:");
        _selWhiteBalanceModeSupported = sel_registerName("isWhiteBalanceModeSupported:");

        var selInstancesRespond = sel_registerName("instancesRespondToSelector:");
        _supportsZoom = objc_msgSend_bool_pointer(_captureDeviceClass, selInstancesRespond, _selVideoZoomFactor) != 0
                        && objc_msgSend_bool_pointer(_captureDeviceClass, selInstancesRespond, _selSetVideoZoomFactor) != 0
                        && objc_msgSend_bool_pointer(_captureDeviceClass, selInstancesRespond, _selMinZoom) != 0
                        && objc_msgSend_bool_pointer(_captureDeviceClass, selInstancesRespond, _selMaxZoom) != 0;

        _available = true;
    }

    public CameraControlCatalog? EnumerateCatalog(WebcamWithIndex device)
    {
        if (!_available)
            return null;

        var pool = objc_autoreleasePoolPush();
        try
        {
            var avDevice = FindDevice(device.Name);
            if (avDevice == IntPtr.Zero)
                return null;

            var controls = new List<CameraControlDescriptor>(4);

            // Zoom — the only plain value control AVFoundation exposes. Range from the device itself;
            // on a camera without zoom min == max == 1.0 and the entry honestly reads 100..100.
            var minZoom = _supportsZoom ? objc_msgSend_double(avDevice, _selMinZoom) : double.NaN;
            var maxZoom = _supportsZoom ? objc_msgSend_double(avDevice, _selMaxZoom) : double.NaN;
            if (!double.IsNaN(minZoom) && !double.IsNaN(maxZoom) && minZoom > 0 && maxZoom >= minZoom)
            {
                controls.Add(new CameraControlDescriptor
                {
                    Name = CameraControlNames.Zoom,
                    Kind = CameraControlKind.Integer,
                    Minimum = ZoomToUnits(minZoom),
                    Maximum = ZoomToUnits(maxZoom),
                    Step = 1, // the factor is continuous; the integer surface rounds to 0.01×
                    DefaultValue = ZoomDefaultUnits,
                    Scale = (float)ZoomUnitsPerFactor,
                    BackendId = BackendIdZoom,
                });
            }

            // The mode menus — items only for modes the device reports as supported (honest catalog).
            AddModeMenu(avDevice, CameraControlNames.Focus, BackendIdFocus, _selFocusModeSupported, ModeContinuous, controls);
            AddModeMenu(avDevice, CameraControlNames.Exposure, BackendIdExposure, _selExposureModeSupported, ModeExposureCustom, controls);
            AddModeMenu(avDevice, CameraControlNames.WhiteBalance, BackendIdWhiteBalance, _selWhiteBalanceModeSupported, ModeContinuous, controls);

            return controls.Count == 0 ? null : new CameraControlCatalog(device, DateTime.Now, controls);
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    public bool TryGetValue(WebcamWithIndex device, CameraControlDescriptor control, out int value, out bool isAuto)
    {
        value = 0;
        isAuto = false;
        if (!_available)
            return false;

        var pool = objc_autoreleasePoolPush();
        try
        {
            var avDevice = FindDevice(device.Name);
            if (avDevice == IntPtr.Zero)
                return false;

            switch (control.BackendId)
            {
                case BackendIdZoom when _supportsZoom:
                    // CGFloat returns via objc_msgSend are ABI-fine — floats come back in xmm0 on both
                    // x86-64 and ARM64, matching the declared double return.
                    value = ZoomToUnits(objc_msgSend_double(avDevice, _selVideoZoomFactor));
                    isAuto = false; // zoom has no auto mode — always manual
                    return true;

                case BackendIdFocus:
                    var focus = objc_msgSend_long(avDevice, _selFocusMode);
                    value = (int)focus;
                    isAuto = focus != ModeLocked; // autoFocus or continuousAutoFocus
                    return true;

                case BackendIdExposure:
                    var exposure = objc_msgSend_long(avDevice, _selExposureMode);
                    value = (int)exposure;
                    isAuto = exposure is ModeAuto or ModeContinuous; // custom (3) is user-driven, not auto
                    return true;

                case BackendIdWhiteBalance:
                    var whiteBalance = objc_msgSend_long(avDevice, _selWhiteBalanceMode);
                    value = (int)whiteBalance;
                    isAuto = whiteBalance is ModeAuto or ModeContinuous;
                    return true;

                default:
                    return false;
            }
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    public bool TrySetValue(WebcamWithIndex device, CameraControlDescriptor control, int value)
    {
        if (!_available)
            return false;

        var pool = objc_autoreleasePoolPush();
        try
        {
            var avDevice = FindDevice(device.Name);
            if (avDevice == IntPtr.Zero)
                return false;

            // The shared value-implies-Manual rule (CameraControls.cs, ICameraControlBackend.TrySetValue)
            // needs no extra disengage step here: a value apply IS the scalar write (Zoom, always manual)
            // or the mode set itself — mode 0 (Locked) is precisely "manual" for the three mode menus.
            switch (control.BackendId)
            {
                case BackendIdZoom:
                    return WriteZoomFactor(avDevice, value / control.Scale);

                case BackendIdFocus:
                    return WriteMode(avDevice, _selFocusModeSupported, _selSetFocusMode, value);

                case BackendIdExposure:
                    return WriteMode(avDevice, _selExposureModeSupported, _selSetExposureMode, value);

                case BackendIdWhiteBalance:
                    return WriteMode(avDevice, _selWhiteBalanceModeSupported, _selSetWhiteBalanceMode, value);

                default:
                    return false;
            }
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    public bool TrySetAuto(WebcamWithIndex device, CameraControlDescriptor control, bool auto)
    {
        if (!_available)
            return false;

        var pool = objc_autoreleasePoolPush();
        try
        {
            var avDevice = FindDevice(device.Name);
            if (avDevice == IntPtr.Zero)
                return false;

            switch (control.BackendId)
            {
                case BackendIdZoom:
                    return false; // no auto mode exists — TrySetAuto honestly fails

                case BackendIdFocus:
                    return WriteMode(avDevice, _selFocusModeSupported, _selSetFocusMode,
                                     auto ? PreferredAutoMode(avDevice, _selFocusModeSupported) : ModeLocked);

                case BackendIdExposure:
                    return WriteMode(avDevice, _selExposureModeSupported, _selSetExposureMode,
                                     auto ? PreferredAutoMode(avDevice, _selExposureModeSupported) : ModeLocked);

                case BackendIdWhiteBalance:
                    return WriteMode(avDevice, _selWhiteBalanceModeSupported, _selSetWhiteBalanceMode,
                                     auto ? PreferredAutoMode(avDevice, _selWhiteBalanceModeSupported) : ModeLocked);

                default:
                    return false;
            }
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    public void InvalidateHandles(WebcamWithIndex device)
    {
        // Nothing to drop: every job re-resolves the AVCaptureDevice by name (macOS "fetches references per
        // job", CameraControls-Design.md §3), so a camera replaced under the same localizedName is picked up
        // on the next job without any invalidation.
    }

    /// <summary>
    /// Resolves the AVCaptureDevice behind a <see cref="WebcamWithIndex"/>. The op's macOS device names are
    /// FFmpeg's printed <c>localizedName</c>s (WebcamScan.ParseAvFoundationDeviceList), which the avfoundation
    /// demuxer itself matches by prefix (VideoDeviceInput.cs:688) — so exact match first, then the same prefix
    /// rule. Matching by index is not an option: WebcamScan's indices number FFmpeg's video+muxed list, not
    /// this array. Two identical cameras sharing a localizedName resolve to the first — the same ambiguity
    /// the demuxer has.
    /// </summary>
    private IntPtr FindDevice(string name)
    {
        if (string.IsNullOrEmpty(name))
            return IntPtr.Zero;

        // A fresh autoreleased NSArray on every call — one of the objects the autorelease pool covers.
        var devices = objc_msgSend_object(_captureDeviceClass, _selDevices); // [AVCaptureDevice devices]
        if (devices == IntPtr.Zero)
            return IntPtr.Zero;

        var count = objc_msgSend_long(devices, _selCount); // NSUInteger
        if (count == 0 && !MacCameraPermission.IsGrantedOrUnknown())
        {
            // An empty list is the macOS privacy gate's signature: without camera access AVFoundation hides
            // every device from the process. The op surfaces the permission messaging itself
            // (VideoDeviceInput.GetNoDevicesStatusMessage); this is only the diagnostic breadcrumb.
            Log.Debug("AVFoundation: no capture devices visible and camera permission is not granted — "
                      + "the control catalog stays empty.");
            return IntPtr.Zero;
        }

        for (var index = 0L; index < count; index++)
        {
            var avDevice = objc_msgSend_object_long(devices, _selObjectAtIndex, index);
            if (avDevice == IntPtr.Zero)
                continue;

            // localizedName is toll-free bridged to CFString, so CFStringGetCString reads it out.
            var localizedName = CFStringToString(objc_msgSend_object(avDevice, _selLocalizedName));
            if (localizedName == null)
                continue;

            if (string.Equals(localizedName, name, StringComparison.Ordinal) ||
                localizedName.StartsWith(name, StringComparison.Ordinal))
            {
                return avDevice;
            }
        }

        return IntPtr.Zero;
    }

    private void AddModeMenu(IntPtr avDevice, string name, string backendId, IntPtr supportedSel, long maxMode,
                             List<CameraControlDescriptor> controls)
    {
        var items = new List<CameraControlItem>((int)maxMode + 1);
        for (var mode = 0L; mode <= maxMode; mode++)
        {
            if (IsModeSupported(avDevice, supportedSel, mode))
                items.Add(new CameraControlItem((int)mode, ModeLabel(mode)));
        }

        // A menu with no supported mode is omitted — same policy as the controls AVFoundation does not
        // expose at all: the catalog never fakes an entry.
        if (items.Count == 0)
            return;

        controls.Add(new CameraControlDescriptor
        {
            Name = name,
            Kind = CameraControlKind.Menu,
            Minimum = items[0].Value,
            Maximum = items[^1].Value,
            Step = 1,
            // AVFoundation exposes no default-mode query; 0 (Locked) is the enum's neutral state and the
            // value TrySetAuto(false) returns the device to. Informational only.
            DefaultValue = (int)ModeLocked,
            Items = [.. items],
            BackendId = backendId,
        });
    }

    /// <summary>Writes a zoom factor, clamped into the device's available range: an out-of-range
    /// <c>setVideoZoomFactor:</c> raises an ObjC exception (which managed code cannot catch), so the clamp
    /// happens here where the value is still ours.</summary>
    private bool WriteZoomFactor(IntPtr avDevice, double factor)
    {
        if (!_supportsZoom)
            return false;

        var min = objc_msgSend_double(avDevice, _selMinZoom);
        var max = objc_msgSend_double(avDevice, _selMaxZoom);
        if (double.IsNaN(min) || double.IsNaN(max) || min <= 0 || max <= 0)
            return false;

        if (max < min)
            (min, max) = (max, min);

        return WriteUnderConfigurationLock(avDevice,
            () => objc_msgSend_void_double(avDevice, _selSetVideoZoomFactor, Math.Clamp(factor, min, max)));
    }

    /// <summary>Writes a capture mode. Setting a mode the device does not support raises an ObjC exception
    /// rather than returning an error — gated on <c>is…ModeSupported:</c> first and failed honestly instead
    /// (CameraControls-Design.md §5.3: surface failures, never silent no-ops).</summary>
    private bool WriteMode(IntPtr avDevice, IntPtr supportedSel, IntPtr setSel, long mode)
    {
        if (!IsModeSupported(avDevice, supportedSel, mode))
            return false;

        return WriteUnderConfigurationLock(avDevice, () => objc_msgSend_void_long(avDevice, setSel, mode));
    }

    private bool WriteUnderConfigurationLock(IntPtr avDevice, Action write)
    {
        // lockForConfiguration: with a NULL NSError out-parameter; NO means the device refused (busy/gone) —
        // reported as a failure so the worker's Status warning carries it.
        if (objc_msgSend_bool_pointer(avDevice, _selLockForConfiguration, IntPtr.Zero) == 0)
            return false;

        try
        {
            write();
            return true;
        }
        finally
        {
            objc_msgSend_void(avDevice, _selUnlockForConfiguration);
        }
    }

    private bool IsModeSupported(IntPtr avDevice, IntPtr supportedSel, long mode)
        => objc_msgSend_bool_object(avDevice, supportedSel, mode) != 0;

    /// <summary>Auto engagement prefers the continuous variant (the UVC "Focus Auto"-like behavior users
    /// expect) and falls back to the plain auto mode when continuous is unsupported.</summary>
    private long PreferredAutoMode(IntPtr avDevice, IntPtr supportedSel)
        => IsModeSupported(avDevice, supportedSel, ModeContinuous) ? ModeContinuous : ModeAuto;

    private static int ZoomToUnits(double factor) => (int)Math.Round(factor * ZoomUnitsPerFactor);

    private static string ModeLabel(long mode) => mode switch
    {
        ModeLocked => "Locked",
        ModeAuto => "Auto",
        ModeContinuous => "Continuous",
        ModeExposureCustom => "Custom",
        _ => mode.ToString(),
    };

    /// <summary>Reads a CFString (localizedName is toll-free bridged) into a C# string. Declared here
    /// because MacCameraPermission.cs is read-only for every work unit (CameraControls-Design.md §6).</summary>
    private static string? CFStringToString(IntPtr cfString)
    {
        if (cfString == IntPtr.Zero)
            return null;

        var buffer = new byte[1024]; // device labels are short — 1 KiB is generous
        if (CFStringGetCString(cfString, buffer, buffer.Length, KcfStringEncodingUtf8) == 0)
            return null;

        var length = Array.IndexOf(buffer, (byte)0);
        if (length < 0)
            length = buffer.Length;

        return Encoding.UTF8.GetString(buffer, 0, length);
    }

    // ---- objc_msgSend wrappers, one per ABI shape ----
    // Each wrapper's return/argument types must match the selector's true signature: objects and integers
    // return in the general registers, floats in xmm0, BOOL is a byte. A wrong shape silently corrupts
    // returns, which is why every selector use below goes through the wrapper of its exact shape.

    // id return, no arguments — devices, localizedName.
    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_object(IntPtr receiver, IntPtr selector);

    // NSInteger/NSUInteger return, no arguments — count, focusMode, exposureMode, whiteBalanceMode.
    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern long objc_msgSend_long(IntPtr receiver, IntPtr selector);

    // CGFloat return, no arguments — videoZoomFactor, min/maxAvailableVideoZoomFactor.
    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern double objc_msgSend_double(IntPtr receiver, IntPtr selector);

    // void return, no arguments — unlockForConfiguration.
    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void(IntPtr receiver, IntPtr selector);

    // id return, one NSInteger argument — objectAtIndex:.
    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_object_long(IntPtr receiver, IntPtr selector, long argument);

    // void return, one NSInteger argument — setFocusMode:, setExposureMode:, setWhiteBalanceMode:.
    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_long(IntPtr receiver, IntPtr selector, long argument);

    // void return, one CGFloat argument — setVideoZoomFactor:.
    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_double(IntPtr receiver, IntPtr selector, double argument);

    // BOOL return, one object argument — isFocusModeSupported: / isExposureModeSupported: /
    // isWhiteBalanceModeSupported: (the mode value is an NSInteger; declared long in the argument).
    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern byte objc_msgSend_bool_object(IntPtr receiver, IntPtr selector, long argument);

    // BOOL return, one pointer argument — lockForConfiguration: (NULL NSError** out).
    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern byte objc_msgSend_bool_pointer(IntPtr receiver, IntPtr selector, IntPtr argument);

    // objc_autoreleasePoolPush/Pop — plain C exports of libobjc. The control worker thread has no
    // autorelease pool, so every job body that consumes the devices NSArray, its objectAtIndex: elements
    // or the localizedName NSStrings runs wrapped: without it each autoreleased object leaks, with a
    // libobjc log line per object, growing across refreshes/applies (CameraControls-Design.md §5.3).
    [DllImport(LibObjc)]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(LibObjc)]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport(LibObjc)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(LibObjc)]
    private static extern IntPtr sel_registerName(string name);

    // CFStringGetCString reads localizedName out (toll-free bridged CFString). v1 creates no CFString of
    // its own — every selector here takes NSInteger/CGFloat arguments — so unlike MacCameraPermission there
    // is no CFStringCreateWithCString/CFRelease pair. CFIndex is a 64-bit signed count — declared nint so
    // the full register is passed (an int parameter would leave the upper half of the register unspecified).
    [DllImport(LibCoreFoundation)]
    private static extern byte CFStringGetCString(IntPtr cfString, byte[] buffer, nint bufferSize, int encoding);
}

/// <summary>
/// Registers the AVFoundation backend with <see cref="CameraControlBackendRegistry"/> when this package's
/// code first executes. In its own class deliberately: the initializer runs on every platform, and keeping
/// it separate means it never triggers any libobjc access — the backend only touches ObjC after the
/// registry's <see cref="OperatingSystem.IsMacOS"/> check has gated construction. A second [ModuleInitializer]
/// alongside VideoExportRegistration's is fine: the runtime runs every attributed method.
/// </summary>
internal static class CameraControlsAvFoundationRegistration
{
    // CA2255: a module initializer in a library is intentional here — same idiom as VideoExportRegistration.
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Initialize()
        => CameraControlBackendRegistry.Register(OperatingSystem.IsMacOS, () => new CameraControlsAvFoundation());
#pragma warning restore CA2255
}
