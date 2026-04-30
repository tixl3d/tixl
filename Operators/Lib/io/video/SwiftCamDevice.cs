using System.Threading;
using System.Threading.Tasks;
using Lib.io.video.swiftcam;
using SharpDX;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using StatusLevel = T3.Core.Operator.Interfaces.IStatusProvider.StatusLevel;

namespace Lib.io.video;

/// <summary>
/// Captures live video from a Swift Imaging scientific USB camera via the vendor SDK.
/// Delivers BGRA8 frames into a Texture2D using the SDK's pull-mode callback.
/// All SDK lifecycle calls (Open, Stop, Close, StartPullModeWithCallback) run on a background
/// thread so the editor's render thread is never blocked by camera I/O.
/// Requires swiftcam.dll from the Swift Imaging SDK at %LOCALAPPDATA%\TiXL\NativeDeps\swiftcam.dll
/// (or pointed at via the TIXL_SWIFTCAM_DLL environment variable).
/// </summary>
[Guid("5ea3d627-e44b-4531-9f06-4503a1a4dd0e")]
public class SwiftCamDevice : Instance<SwiftCamDevice>, ICustomDropdownHolder, IStatusProvider
{
    [Output(Guid = "5f1552ab-e9e5-49ac-b2f1-3019becf6f96", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> Texture = new();

    [Output(Guid = "c5d201f4-020c-4f84-9ba2-1a3e59423f22", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Int2> Resolution = new();

    [Output(Guid = "d6862579-03df-48e8-a495-070fb6614497", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public new readonly Slot<string> Status = new();

    [Output(Guid = "083525a9-1c9d-4ac4-96dc-915a4c0b6aaa", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> UpdateCount = new();

    [Input(Guid = "d73af73f-e71c-4a6b-a4cf-f8069c1db30d")]
    public readonly InputSlot<bool> Active = new();

    [Input(Guid = "c44f7b21-0418-490f-a2c1-4d57b70bfa32")]
    public readonly InputSlot<string> DeviceName = new();

    [Input(Guid = "df2f5f6b-2b27-44b7-989d-6c8225113234")]
    public readonly InputSlot<int> ResolutionIndex = new();

    [Input(Guid = "e9bc114b-75ac-4bec-a337-5b16404da4de")]
    public readonly InputSlot<bool> AutoExposure = new();

    [Input(Guid = "48e138e2-c31f-45fe-81f2-cf3789fe7d10")]
    public readonly InputSlot<float> Exposure = new();

    [Input(Guid = "370fc7fa-29f8-492e-b443-cce968ece722")]
    public readonly InputSlot<float> AnalogGainFactor = new();

    [Input(Guid = "ea6b3503-9a68-405e-9b34-7f44a74d5b31")]
    public readonly InputSlot<Int2> RoiResolution = new();

    [Input(Guid = "3e021070-038f-453d-96ca-33c27bc8ddc4")]
    public readonly InputSlot<Vector2> RoiAlignment = new();

    [Input(Guid = "7965cea6-2fe4-4240-9f9c-ab2af0aedea8")]
    public readonly InputSlot<bool> LogMessages = new();

    [Input(Guid = "7cd0acca-d843-472e-9fcc-e8deaf129336")]
    public readonly InputSlot<bool> Reconnect = new();

    [Input(Guid = "caa5d946-060d-45eb-907b-601c4f0a677c")]
    public readonly InputSlot<bool> HardReset = new();

    static SwiftCamDevice()
    {
        // Pre-load swiftcam.dll into the process by full path. Later [DllImport("swiftcam.dll")] calls
        // resolve to the already-loaded module via the per-process module table, sidestepping TiXL's
        // load-context resolver (which only probes the assembly's directory + the application directory).
        TryPreloadSwiftcamDll();
    }

    public SwiftCamDevice()
    {
        Texture.UpdateAction = Update;
        Resolution.UpdateAction = Update;
        Status.UpdateAction = Update;
        UpdateCount.UpdateAction = Update;
    }

    public StatusLevel GetStatusLevel() => _statusLevel;

    public string GetStatusMessage() => _statusMessage;

    private static void TryPreloadSwiftcamDll()
    {
        try
        {
            var envPath = Environment.GetEnvironmentVariable("TIXL_SWIFTCAM_DLL");
            if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath)
                && NativeLibrary.TryLoad(envPath, out _))
            {
                Log.Debug($"SwiftCamDevice: pre-loaded swiftcam.dll from TIXL_SWIFTCAM_DLL='{envPath}'");
                return;
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userPath = Path.Combine(localAppData, "TiXL", "NativeDeps", "swiftcam.dll");
            if (File.Exists(userPath))
            {
                if (NativeLibrary.TryLoad(userPath, out _))
                    Log.Debug($"SwiftCamDevice: pre-loaded swiftcam.dll from '{userPath}'");
                else
                    Log.Warning($"SwiftCamDevice: file at '{userPath}' exists but NativeLibrary.TryLoad failed (likely bitness mismatch)");
            }
            else
            {
                Log.Debug($"SwiftCamDevice: '{userPath}' not found - SDK probe will fail unless DLL is on default search path");
            }
        }
        catch (Exception e)
        {
            Log.Warning($"SwiftCamDevice: pre-load threw - {e.Message}");
        }
    }

    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        if (inputId == DeviceName.Id)
            return DeviceName.Value;

        if (inputId == ResolutionIndex.Id)
        {
            var idx = ResolutionIndex.Value;
            if (TryFindDevice(DeviceName.Value, out var device)
                && device.model.res is { } res
                && idx >= 0 && idx < res.Length)
            {
                var w = res[idx].width;
                var h = res[idx].height;
                return idx == 0 ? $"{idx}: {w}x{h} (full)" : $"{idx}: {w}x{h}";
            }
            return idx.ToString();
        }

        return string.Empty;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        if (inputId == DeviceName.Id)
            return EnumerateDeviceOptions();

        if (inputId == ResolutionIndex.Id)
            return EnumerateResolutionOptions();

        return Array.Empty<string>();
    }

    private IEnumerable<string> EnumerateDeviceOptions()
    {
        if (_sdkAvailable == null)
            _sdkAvailable = TryProbeSdk();

        if (_sdkAvailable == false)
        {
            yield return SdkMissingMessage;
            yield break;
        }

        ScanDevices();

        if (_devices == null || _devices.Length == 0)
        {
            yield return "No Swift cameras found";
            yield break;
        }

        for (var i = 0; i < _devices.Length; i++)
            yield return _devices[i].displayname;
    }

    private IEnumerable<string> EnumerateResolutionOptions()
    {
        if (_sdkAvailable != true)
        {
            yield return "(SDK not available)";
            yield break;
        }

        ScanDevices();

        if (!TryFindDevice(DeviceName.Value, out var device))
        {
            yield return "(Pick a device first)";
            yield break;
        }

        var count = (int)device.model.preview;
        var resArray = device.model.res;
        if (resArray == null || count <= 0)
        {
            yield return "(No resolutions reported)";
            yield break;
        }

        for (var i = 0; i < count && i < resArray.Length; i++)
        {
            var w = resArray[i].width;
            var h = resArray[i].height;
            var label = i == 0
                            ? $"{i}: {w}x{h} (full)"
                            : $"{i}: {w}x{h}";
            yield return label;
        }
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string selected, bool isAListItem)
    {
        if (inputId == DeviceName.Id)
        {
            DeviceName.SetTypedInputValue(selected);
            return;
        }

        if (inputId == ResolutionIndex.Id && !string.IsNullOrEmpty(selected))
        {
            // Labels are formatted as "{index}: WxH ...". Parse the leading integer.
            var colon = selected.IndexOf(':');
            var head = colon > 0 ? selected[..colon] : selected;
            if (int.TryParse(head, out var index))
                ResolutionIndex.SetTypedInputValue(index);
        }
    }

    private void Update(EvaluationContext context)
    {
        // Cache verbose flag for the SDK callback thread.
        _logVerbose = LogMessages.GetValue(context);

        if (_sdkAvailable == null)
            _sdkAvailable = TryProbeSdk();

        if (Reconnect.GetValue(context))
        {
            Reconnect.SetTypedInputValue(false);
            Log.Debug("SwiftCamDevice: Reconnect triggered", this);
            BeginStop();
            _devices = null;
            _sdkAvailable = null;
            _backoffUntilTickMs = 0;
            _consecutiveSilentFailures = 0;
            SetStatus("Reconnecting…", StatusLevel.Notice);
            Status.Value = _statusMessage;
            return;
        }

        if (HardReset.GetValue(context))
        {
            HardReset.SetTypedInputValue(false);
            Log.Warning("SwiftCamDevice: HardReset triggered — sensor reset + flush + reopen", this);

            // Try a sensor-side reset and buffer flush first. These are fire-and-forget; the
            // camera may or may not actually be in a state to receive them, but it's worth
            // trying before the more disruptive Close/Open cycle.
            if (_camera != null)
            {
                try { _camera.put_Option(Swiftcam.eOPTION.OPTION_RESET_SENSOR, 1); }
                catch (Exception e) { Log.Debug($"SwiftCamDevice: OPTION_RESET_SENSOR failed - {e.Message}", this); }
                try { _camera.put_Option(Swiftcam.eOPTION.OPTION_FLUSH, 3); }
                catch (Exception e) { Log.Debug($"SwiftCamDevice: OPTION_FLUSH failed - {e.Message}", this); }
            }

            BeginStop();
            _devices = null;
            _sdkAvailable = null;
            _backoffUntilTickMs = 0;
            _consecutiveSilentFailures = 0;
            SetStatus("Hard reset…", StatusLevel.Notice);
            Status.Value = _statusMessage;
            return;
        }

        if (_sdkAvailable == false)
        {
            SetStatus(SdkMissingMessage, StatusLevel.Error);
            Status.Value = _statusMessage;
            return;
        }

        PollPendingTasks();
        HandleErrorFlags();

        ScanDevices();

        var deviceName = DeviceName.GetValue(context);
        if (string.IsNullOrEmpty(deviceName) && _devices != null && _devices.Length > 0)
            deviceName = _devices[0].displayname;

        var deviceFound = TryFindDeviceId(deviceName, out var deviceId);
        var active = Active.GetValue(context);

        if (!deviceFound)
        {
            if (_state == CamState.Streaming || _state == CamState.Starting)
                BeginStop();
            if (_state == CamState.Idle)
                SetStatus($"Camera '{deviceName}' not found.", StatusLevel.Warning);
        }
        else if (active)
        {
            var snap = SnapshotStartConfig(context, deviceId);

            if (_state == CamState.Streaming && snap.Config.RequiresRestart(_openedConfig))
            {
                Log.Debug("SwiftCamDevice: config changed during streaming - restarting", this);
                BeginStop();
                _backoffUntilTickMs = 0;  // user-driven change — restart promptly
                _consecutiveSilentFailures = 0;
            }
            else if (_state == CamState.Idle && snap.Config.RequiresRestart(_openedConfig))
            {
                // User changed config while we were idle (1 s backoff or suspended after repeated
                // failures). Clear the suspension so we retry promptly with the new config.
                _backoffUntilTickMs = 0;
                _consecutiveSilentFailures = 0;
            }

            if (_state == CamState.Idle && Environment.TickCount64 >= _backoffUntilTickMs)
            {
                BeginStart(snap);
            }
            else if (_state == CamState.Idle)
            {
                // In backoff window — keep last status (set when error fired).
            }

            if (_state == CamState.Streaming)
            {
                ApplyAutoExposureAndExposure(context);
                ApplyGain(context);
            }
        }
        else  // not active
        {
            if (_state == CamState.Streaming || _state == CamState.Starting)
                BeginStop();
            if (_state == CamState.Idle)
                SetStatus("Inactive.", StatusLevel.Notice);
        }

        UploadLatestFrame();
        Status.Value = _statusMessage;
    }

    private static bool TryProbeSdk()
    {
        try
        {
            var devices = Swiftcam.EnumV2();
            Log.Debug($"SwiftCamDevice: SDK probe succeeded, EnumV2 returned {devices?.Length ?? 0} device(s)");
            return true;
        }
        catch (DllNotFoundException e)
        {
            Log.Warning($"SwiftCamDevice: SDK probe failed - DllNotFoundException: {e.Message}");
            return false;
        }
        catch (BadImageFormatException e)
        {
            Log.Warning($"SwiftCamDevice: SDK probe failed - BadImageFormatException: {e.Message}");
            return false;
        }
    }

    private void ScanDevices()
    {
        if (_devices != null)
            return;

        try
        {
            _devices = Swiftcam.EnumV2() ?? Array.Empty<Swiftcam.DeviceV2>();
            if (_logVerbose)
            {
                Log.Debug($"SwiftCamDevice: enumerated {_devices.Length} device(s)", this);
                for (var i = 0; i < _devices.Length; i++)
                    Log.Debug($"SwiftCamDevice:   [{i}] '{_devices[i].displayname}' id='{_devices[i].id}'", this);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"SwiftCamDevice: enumeration failed - {e.Message}", this);
            _devices = Array.Empty<Swiftcam.DeviceV2>();
        }
    }

    private bool TryFindDeviceId(string displayName, out string id)
    {
        if (TryFindDevice(displayName, out var device))
        {
            id = device.id;
            return true;
        }

        id = null;
        return false;
    }

    private bool TryFindDevice(string displayName, out Swiftcam.DeviceV2 device)
    {
        device = default;
        if (_devices == null || string.IsNullOrEmpty(displayName))
            return false;

        for (var i = 0; i < _devices.Length; i++)
        {
            if (_devices[i].displayname == displayName)
            {
                device = _devices[i];
                return true;
            }
        }

        return false;
    }

    private StartSnapshot SnapshotStartConfig(EvaluationContext context, string deviceId)
    {
        return new StartSnapshot
                   {
                       Config = new StreamConfig
                                    {
                                        DeviceId = deviceId,
                                        ResolutionIndex = (uint)Math.Max(0, ResolutionIndex.GetValue(context)),
                                        RoiResolution = RoiResolution.GetValue(context),
                                        RoiAlignment = RoiAlignment.GetValue(context),
                                    },
                       InitialAutoExposure = AutoExposure.GetValue(context),
                       InitialExposureMs = Exposure.GetValue(context),
                       InitialGainFactor = AnalogGainFactor.GetValue(context),
                   };
    }

    private void BeginStart(StartSnapshot snap)
    {
        Log.Debug($"SwiftCamDevice: BeginStart resIndex={snap.Config.ResolutionIndex} "
                  + $"autoExpo={snap.InitialAutoExposure} exposureMs={snap.InitialExposureMs:F1} "
                  + $"gain={snap.InitialGainFactor:F2}x (ROI applied mid-stream after warm-up)", this);
        SetStatus("Starting…", StatusLevel.Notice);
        _state = CamState.Starting;
        _openedConfig = snap.Config;
        _loggedFirstFrame = false;
        _framesReceivedSinceStart = 0;

        var verbose = _logVerbose;
        // DoStart runs on a background thread so the render thread isn't blocked by Swiftcam.Open
        // (~1.3 s) or StartPullModeWithCallback (also slow on this SDK).
        _startTask = Task.Run(() => DoStart(snap, verbose));
    }

    private StartResult DoStart(StartSnapshot snap, bool verbose)
    {
        Swiftcam cam = null;
        try
        {
            cam = Swiftcam.Open(snap.Config.DeviceId);
            if (cam == null)
                return new StartResult { ErrorMessage = "Failed to open Swift camera." };

            // Real-time delivery — vendor sample calls this right after Open.
            var rtOk = cam.put_RealTime(1);
            if (verbose) Log.Debug($"SwiftCamDevice:   put_RealTime(1) = {rtOk}", this);

            // Note: deliberately NOT calling put_Option(OPTION_RGB, RGB32). The vendor's samples
            // never set this, and combining native RGB32 with non-zero ROI offsets seems to
            // trigger EVENT_ERROR shortly after streaming starts on this SDK family. We accept
            // a per-frame RGB24→RGB32 conversion inside the SDK (PullImage with bits=32) in
            // exchange for ROI working reliably.

            var sizeOk = cam.put_eSize(snap.Config.ResolutionIndex);
            if (verbose) Log.Debug($"SwiftCamDevice:   put_eSize({snap.Config.ResolutionIndex}) = {sizeOk}", this);

            // Reset any persisted ROI before measuring sensor dimensions. ROI bounds in this SDK
            // are relative to the *current* effective size, not the original sensor — if a prior
            // ROI carried over, get_Size returns the cropped dimensions and our offset math
            // computes against the wrong base.
            var resetOk = cam.put_Roi(0, 0, 0, 0);
            if (verbose) Log.Debug($"SwiftCamDevice:   put_Roi(0,0,0,0) reset = {resetOk}", this);

            if (!cam.get_Size(out var sensorW, out var sensorH) || sensorW <= 0 || sensorH <= 0)
            {
                cam.Close();
                return new StartResult { ErrorMessage = "Camera reported an invalid resolution." };
            }
            if (verbose) Log.Debug($"SwiftCamDevice:   get_Size = {sensorW}x{sensorH}", this);

            // Apply ROI before starting streaming. (Mid-stream ROI changes on the SC1003 cause
            // EVENT_ERROR — neither pre-start nor post-start works reliably, but pre-start is the
            // pattern the vendor's WinForms sample follows for resolution changes.)
            ComputeRoi(snap.Config.RoiResolution, snap.Config.RoiAlignment, sensorW, sensorH,
                       out var roiX, out var roiY, out var roiW, out var roiH);
            var roiOk = cam.put_Roi(roiX, roiY, roiW, roiH);
            if (verbose) Log.Debug($"SwiftCamDevice:   put_Roi({roiX},{roiY},{roiW},{roiH}) = {roiOk}", this);

            var aeOk = cam.put_AutoExpoEnable(snap.InitialAutoExposure);
            if (verbose) Log.Debug($"SwiftCamDevice:   put_AutoExpoEnable({snap.InitialAutoExposure}) = {aeOk}", this);

            int appliedExposureUs = -1;
            if (!snap.InitialAutoExposure && snap.InitialExposureMs > 0)
            {
                var us = (uint)Math.Max(1, (int)Math.Round(snap.InitialExposureMs * 1000f));
                cam.put_ExpoTime(us);
                appliedExposureUs = (int)us;
                if (verbose) Log.Debug($"SwiftCamDevice:   put_ExpoTime({us})", this);
            }

            // AnalogGainFactor: 1.0 = unity (= 100% in the SDK). Camera's EXPOGAIN_MIN is 100.
            var gainPercent = (int)Math.Round(snap.InitialGainFactor * 100f);
            int appliedGainPercent = -1;
            if (gainPercent >= Swiftcam.EXPOGAIN_MIN)
            {
                cam.put_ExpoAGain((ushort)gainPercent);
                appliedGainPercent = gainPercent;
                if (verbose) Log.Debug($"SwiftCamDevice:   put_ExpoAGain({gainPercent}%)", this);
            }

            cam.get_Size(out var effW, out var effH);
            if (effW <= 0) effW = sensorW;
            if (effH <= 0) effH = sensorH;

            // Start streaming on this background thread. StartPullModeWithCallback can take
            // 1–3 seconds on this SDK family — keeping it off the render thread is critical.
            var cb = new Swiftcam.DelegateEventCallback(OnEventCallback);
            if (!cam.StartPullModeWithCallback(cb))
            {
                cam.Close();
                return new StartResult { ErrorMessage = "Swift camera failed to start streaming." };
            }

            return new StartResult
                       {
                           Camera = cam,
                           EventCallback = cb,
                           SensorWidth = sensorW,
                           SensorHeight = sensorH,
                           EffectiveWidth = effW,
                           EffectiveHeight = effH,
                           AppliedAutoExposure = snap.InitialAutoExposure,
                           AppliedExposureUs = appliedExposureUs,
                           AppliedGainPercent = appliedGainPercent,
                           AppliedRoi = (roiX, roiY, roiW, roiH),
                       };
        }
        catch (Exception e)
        {
            try { cam?.Close(); } catch { /* best effort */ }
            return new StartResult { ErrorMessage = $"Start failed: {e.Message}" };
        }
    }

    private void PollPendingTasks()
    {
        if (_state == CamState.Starting && _startTask != null && _startTask.IsCompleted)
        {
            var r = _startTask.Result;
            _startTask = null;

            if (r.ErrorMessage != null)
            {
                Log.Warning($"SwiftCamDevice: {r.ErrorMessage}", this);
                SetStatus(r.ErrorMessage, StatusLevel.Error);
                _state = CamState.Idle;
                ScheduleStartRetry();
            }
            else
            {
                _camera = r.Camera;
                _eventCallback = r.EventCallback;
                _sensorWidth = r.SensorWidth;
                _sensorHeight = r.SensorHeight;
                _appliedAutoExposure = r.AppliedAutoExposure;
                _appliedExposureUs = r.AppliedExposureUs;
                _appliedGainPercent = r.AppliedGainPercent;
                _appliedRoi = r.AppliedRoi;
                EnsurePullBuffer(r.EffectiveWidth, r.EffectiveHeight);

                _state = CamState.Streaming;
                SetStatus("Streaming.", StatusLevel.Success);
                Log.Debug("SwiftCamDevice: streaming started", this);

                if (_stopRequestedDuringStart)
                {
                    _stopRequestedDuringStart = false;
                    BeginStop();
                }
            }
        }

        if (_state == CamState.Stopping && _stopTask != null && _stopTask.IsCompleted)
        {
            _stopTask = null;
            _state = CamState.Idle;
            Log.Debug("SwiftCamDevice: stopped", this);

            // Force a settling period before the next Open. Stop/Open in rapid succession is
            // what wedges the SC1003 camera firmware most reliably.
            var settleUntil = Environment.TickCount64 + PostStopCooldownMs;
            if (settleUntil > _backoffUntilTickMs)
                _backoffUntilTickMs = settleUntil;
        }
    }

    private void HandleErrorFlags()
    {
        if (_disconnected)
        {
            _disconnected = false;
            Log.Warning("SwiftCamDevice: disconnect detected, auto-retrying", this);
            SetStatus("Camera disconnected. Auto-retrying…", StatusLevel.Warning);
            BeginStop();
            ScheduleStartRetry();
            return;
        }

        if (_hasError)
        {
            _hasError = false;

            // If the start succeeded but no frame ever arrived before EVENT_ERROR fired, the
            // current config (most often a non-zero RoiResolution) is unsupported by this
            // camera/SDK. Bail after a few attempts rather than retry-looping forever.
            if (_framesReceivedSinceStart == 0)
            {
                _consecutiveSilentFailures++;
                Log.Warning($"SwiftCamDevice: SDK error before first frame "
                            + $"(attempt {_consecutiveSilentFailures}/{MaxSilentFailures})", this);

                if (_consecutiveSilentFailures >= MaxSilentFailures)
                {
                    SetStatus("Camera configuration not supported (likely a non-zero RoiResolution "
                              + "on a camera whose SDK rejects mid-stream ROI). Set RoiResolution "
                              + "to (0, 0) and try Reconnect.",
                              StatusLevel.Error);
                    BeginStop();
                    _backoffUntilTickMs = long.MaxValue;  // suspend retries until config changes
                    return;
                }
            }
            else
            {
                Log.Warning("SwiftCamDevice: SDK error after streaming, auto-retrying", this);
            }

            SetStatus("Camera reported an error. Auto-retrying…", StatusLevel.Error);
            BeginStop();
            ScheduleStartRetry();
        }
    }

    private void BeginStop()
    {
        switch (_state)
        {
            case CamState.Idle:
            case CamState.Stopping:
                return;
            case CamState.Starting:
                // Open/Start is in-flight on a background thread; can't cancel cleanly.
                // Defer the stop to when the start task completes.
                _stopRequestedDuringStart = true;
                return;
            case CamState.Streaming:
                DoBeginStop();
                return;
        }
    }

    private void DoBeginStop()
    {
        var cam = _camera;
        _camera = null;
        _eventCallback = null;
        _hasNewFrame = false;
        _state = CamState.Stopping;
        Log.Debug("SwiftCamDevice: BeginStop", this);

        _stopTask = Task.Run(() => SafeStopAndClose(cam));
    }

    private static void SafeStopAndClose(Swiftcam cam)
    {
        if (cam == null)
            return;
        try
        {
            cam.Stop();
            cam.Close();
        }
        catch (Exception e)
        {
            Log.Warning($"SwiftCamDevice: Stop/Close failed - {e.Message}");
        }
    }

    private void ScheduleStartRetry()
    {
        // Force re-enumeration on next attempt — picks up "camera unplugged" rather than
        // blindly re-trying the cached id.
        _devices = null;
        _backoffUntilTickMs = Environment.TickCount64 + StartBackoffMs;
    }

    private void EnsurePullBuffer(int width, int height)
    {
        var stride = width * BytesPerPixel;
        var totalBytes = stride * height;
        lock (_frameLock)
        {
            if (_frameBuffer == IntPtr.Zero || _frameBufferCapacity < totalBytes)
            {
                if (_frameBuffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(_frameBuffer);
                _frameBuffer = Marshal.AllocHGlobal(totalBytes);
                _frameBufferCapacity = totalBytes;
            }
            _expectedWidth = width;
            _expectedHeight = height;
            _expectedStride = stride;
        }
    }

    /// <summary>
    /// Maps user-facing (RoiResolution, RoiAlignment) into hardware (x, y, w, h) snapped to
    /// 2-pixel boundaries (Bayer-pattern alignment). RoiResolution X or Y &lt;= 0, or a crop
    /// equal/greater than the sensor in both axes, disables ROI (returns 0,0,0,0).
    ///
    /// The camera ultimately decides which sizes are valid. The vendor's own app surfaces
    /// "Out of range" errors for many seemingly-reasonable sizes (e.g. 1920×1080 isn't
    /// accepted on the SC1003 — try 1954×1062 instead). When put_Roi succeeds at the SDK
    /// level but the camera silently rejects the size, the operator's silent-failure
    /// detection kicks in and surfaces a clear error to the user.
    /// </summary>
    private static void ComputeRoi(Int2 roiRes, Vector2 alignment, int sensorW, int sensorH,
                                   out uint x, out uint y, out uint w, out uint h)
    {
        if (roiRes.X <= 0 || roiRes.Y <= 0
            || (roiRes.X >= sensorW && roiRes.Y >= sensorH))
        {
            x = 0;
            y = 0;
            w = 0;
            h = 0;
            return;
        }

        var snapW = SnapDownTo2(Math.Min(sensorW, roiRes.X));
        var snapH = SnapDownTo2(Math.Min(sensorH, roiRes.Y));
        var slackX = (sensorW - snapW) / 2;
        var slackY = (sensorH - snapH) / 2;

        var ax = Math.Clamp(alignment.X, -1f, 1f);
        var ay = Math.Clamp(alignment.Y, -1f, 1f);

        var offsetX = slackX + (int)Math.Round(ax * slackX);
        var offsetY = slackY + (int)Math.Round(ay * slackY);

        offsetX = SnapDownTo2(Math.Clamp(offsetX, 0, sensorW - snapW));
        offsetY = SnapDownTo2(Math.Clamp(offsetY, 0, sensorH - snapH));

        x = (uint)offsetX;
        y = (uint)offsetY;
        w = (uint)snapW;
        h = (uint)snapH;
    }

    private static int SnapDownTo2(int v) => Math.Max(0, (v / 2) * 2);

    private void ApplyAutoExposureAndExposure(EvaluationContext context)
    {
        if (_camera == null)
            return;

        var auto = AutoExposure.GetValue(context);
        if (_appliedAutoExposure != auto)
        {
            try
            {
                _camera.put_AutoExpoEnable(auto);
                _appliedAutoExposure = auto;
                LogVerbose($"put_AutoExpoEnable({auto})");
            }
            catch (Exception e)
            {
                Log.Warning($"SwiftCamDevice: put_AutoExpoEnable failed - {e.Message}", this);
            }
        }

        if (auto)
            return;  // SDK manages exposure; ignore the slider while AE is on.

        var ms = Exposure.GetValue(context);
        if (!(ms > 0f))
            return;

        var us = (int)Math.Round(ms * 1000f);
        if (us == _appliedExposureUs)
            return;

        try
        {
            _camera.put_ExpoTime((uint)Math.Max(1, us));
            _appliedExposureUs = us;
        }
        catch (Exception e)
        {
            Log.Warning($"SwiftCamDevice: put_ExpoTime failed - {e.Message}", this);
        }
    }

    private void ApplyGain(EvaluationContext context)
    {
        if (_camera == null)
            return;

        // AnalogGainFactor: 1.0 = unity, 5.0 = 5x. SDK takes percent (100 = unity).
        var gainPercent = (int)Math.Round(AnalogGainFactor.GetValue(context) * 100f);
        if (gainPercent < Swiftcam.EXPOGAIN_MIN || gainPercent == _appliedGainPercent)
            return;

        try
        {
            _camera.put_ExpoAGain((ushort)gainPercent);
            _appliedGainPercent = gainPercent;
        }
        catch (Exception e)
        {
            Log.Warning($"SwiftCamDevice: put_ExpoAGain failed - {e.Message}", this);
        }
    }

    private void OnEventCallback(Swiftcam.eEVENT nEvent)
    {
        // Runs on SDK thread.
        switch (nEvent)
        {
            case Swiftcam.eEVENT.EVENT_IMAGE:
                if (_logVerbose)
                    Log.Debug($"SwiftCamDevice: event {nEvent}", this);
                PullFrame();
                break;
            case Swiftcam.eEVENT.EVENT_DISCONNECTED:
                Log.Warning("SwiftCamDevice: event EVENT_DISCONNECTED", this);
                _disconnected = true;
                break;
            case Swiftcam.eEVENT.EVENT_ERROR:
                Log.Warning("SwiftCamDevice: event EVENT_ERROR", this);
                _hasError = true;
                break;
            case Swiftcam.eEVENT.EVENT_NOFRAMETIMEOUT:
                Log.Warning("SwiftCamDevice: event EVENT_NOFRAMETIMEOUT (no frame received within timeout)", this);
                _hasError = true;
                break;
            case Swiftcam.eEVENT.EVENT_NOPACKETTIMEOUT:
                Log.Warning("SwiftCamDevice: event EVENT_NOPACKETTIMEOUT (no USB packet within timeout)", this);
                _hasError = true;
                break;
            default:
                if (_logVerbose)
                    Log.Debug($"SwiftCamDevice: event {nEvent}", this);
                break;
        }
    }

    private void PullFrame()
    {
        var cam = _camera;
        if (cam == null)
            return;

        IntPtr buffer;
        int width, height, stride, capacity;
        lock (_frameLock)
        {
            buffer = _frameBuffer;
            width = _expectedWidth;
            height = _expectedHeight;
            stride = _expectedStride;
            capacity = _frameBufferCapacity;
        }

        if (buffer == IntPtr.Zero || width <= 0 || height <= 0 || capacity < stride * height)
        {
            LogVerbose($"PullFrame skipped - buffer not ready (capacity={capacity}, want={stride * height})");
            return;
        }

        try
        {
            if (!cam.PullImage(buffer, 0, BitsPerPixel, stride, out var info))
            {
                LogVerbose("PullImage returned false");
                return;
            }

            if (!_loggedFirstFrame)
            {
                Log.Debug($"SwiftCamDevice: first frame received - {info.v3.width}x{info.v3.height}, "
                          + $"seq={info.v3.seq}, flag=0x{info.v3.flag:X}, expotime={info.v3.expotime}us, "
                          + $"expogain={info.v3.expogain}", this);
                _loggedFirstFrame = true;
                _consecutiveSilentFailures = 0;  // a successful frame breaks the give-up loop
            }
            _framesReceivedSinceStart++;

            lock (_frameLock)
            {
                _frameWidth = (int)info.v3.width;
                _frameHeight = (int)info.v3.height;
                _frameStride = stride;
                _frameSeq = info.v3.seq;
                _hasNewFrame = true;
            }
        }
        catch (Exception e)
        {
            Log.Warning($"SwiftCamDevice: PullImage threw - {e.Message}", this);
        }
    }

    private void UploadLatestFrame()
    {
        IntPtr buffer;
        int width, height, stride;
        lock (_frameLock)
        {
            if (!_hasNewFrame)
                return;

            buffer = _frameBuffer;
            width = _frameWidth;
            height = _frameHeight;
            stride = _frameStride;
            _hasNewFrame = false;
        }

        EnsureGpuTexture(width, height, Format.B8G8R8A8_UNorm);

        var dataBox = new DataBox(buffer, stride, 0);
        ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, _gpuTexture);

        Texture.Value = _gpuTexture;
        Resolution.Value = new Int2(width, height);
        UpdateCount.Value++;
    }

    private void EnsureGpuTexture(int width, int height, Format format)
    {
        if (_gpuTexture != null && _gpuWidth == width && _gpuHeight == height && _gpuFormat == format)
            return;

        Log.Debug($"SwiftCamDevice: allocating GPU texture {width}x{height} {format}", this);
        _gpuTexture?.Dispose();

        var desc = new Texture2DDescription
                       {
                           Width = width,
                           Height = height,
                           MipLevels = 1,
                           ArraySize = 1,
                           Format = format,
                           SampleDescription = new SampleDescription(1, 0),
                           Usage = ResourceUsage.Default,
                           BindFlags = BindFlags.ShaderResource,
                           CpuAccessFlags = CpuAccessFlags.None,
                           OptionFlags = ResourceOptionFlags.None,
                       };
        _gpuTexture = new Texture2D(new SharpDX.Direct3D11.Texture2D(ResourceManager.Device, desc));
        _gpuWidth = width;
        _gpuHeight = height;
        _gpuFormat = format;
    }

    private void SetStatus(string message, StatusLevel level)
    {
        _statusMessage = message;
        _statusLevel = level;
    }

    private void LogVerbose(string msg)
    {
        if (_logVerbose)
            Log.Debug($"SwiftCamDevice: {msg}", this);
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        BeginStop();

        // Wait for any pending lifecycle task so we don't leak the camera handle.
        var pending = new List<Task>();
        if (_startTask != null)
            pending.Add(_startTask);
        if (_stopTask != null)
            pending.Add(_stopTask);
        if (pending.Count > 0)
        {
            try
            {
                Task.WaitAll(pending.ToArray(), TimeSpan.FromSeconds(3));
            }
            catch
            {
                // Best effort; if a task fails or times out, fall through and free what we own.
            }
        }

        // If the start task succeeded but we didn't pick up its result, the camera handle is in
        // the result. Drain it.
        if (_startTask != null && _startTask.IsCompletedSuccessfully)
        {
            try { _startTask.Result?.Camera?.Close(); } catch { /* best effort */ }
        }

        lock (_frameLock)
        {
            if (_frameBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_frameBuffer);
                _frameBuffer = IntPtr.Zero;
                _frameBufferCapacity = 0;
            }
        }

        _gpuTexture?.Dispose();
        _gpuTexture = null;
    }

    private const string SdkMissingMessage =
        "swiftcam.dll not found. Copy the x64 DLL from the Swift Imaging SDK to "
        + "%LOCALAPPDATA%\\TiXL\\NativeDeps\\swiftcam.dll, then trigger Reconnect.";

    private const int BitsPerPixel = 32;
    private const int BytesPerPixel = 4;

    // Cooldown after a failed start or runtime error so we don't hammer Swiftcam.Open.
    // Short enough that a transient USB hiccup auto-recovers within a second or two.
    private const long StartBackoffMs = 1000;

    // After this many "started but no frame before EVENT_ERROR" cycles, suspend retries until
    // the user changes config or triggers Reconnect. Indicates the current config is unsupported
    // (typically a non-zero RoiResolution on a camera whose SDK rejects mid-stream ROI changes).
    private const int MaxSilentFailures = 3;

    // After a Stop/Close completes, wait this long before the next Open. The Swift firmware seems
    // to need a settling period — quick Stop+Open cycles tend to wedge the camera into a state
    // only physical USB unplug can recover. This is conservative; faster cameras may not need it.
    private const long PostStopCooldownMs = 3000;

    private enum CamState
    {
        Idle,
        Starting,
        Streaming,
        Stopping,
    }

    private struct StreamConfig
    {
        public string DeviceId;
        public uint ResolutionIndex;
        public Int2 RoiResolution;
        public Vector2 RoiAlignment;

        // Any change here triggers a clean Stop + cooldown + Open cycle. Mid-stream ROI changes
        // proved unreliable on the SC1003 (EVENT_ERROR ~1s after EVENT_ROI), so we treat ROI like
        // any other restart-required setting.

        public bool RequiresRestart(StreamConfig other)
        {
            return DeviceId != other.DeviceId
                   || ResolutionIndex != other.ResolutionIndex
                   || RoiResolution != other.RoiResolution
                   || RoiAlignment != other.RoiAlignment;
        }
    }

    private struct StartSnapshot
    {
        public StreamConfig Config;
        public bool InitialAutoExposure;
        public float InitialExposureMs;
        public float InitialGainFactor;
    }

    private class StartResult
    {
        public Swiftcam Camera;
        public Swiftcam.DelegateEventCallback EventCallback;
        public int SensorWidth;
        public int SensorHeight;
        public int EffectiveWidth;
        public int EffectiveHeight;
        public bool AppliedAutoExposure;
        public int AppliedExposureUs;
        public int AppliedGainPercent;
        public (uint X, uint Y, uint W, uint H) AppliedRoi;
        public string ErrorMessage;
    }

    // State machine + lifecycle tasks
    private CamState _state = CamState.Idle;
    private Task<StartResult> _startTask;
    private Task _stopTask;
    private bool _stopRequestedDuringStart;
    private long _backoffUntilTickMs;

    // SDK + device cache
    private bool? _sdkAvailable;
    private Swiftcam.DeviceV2[] _devices;
    private volatile bool _logVerbose;

    // Streaming-state fields (only valid while _state == CamState.Streaming)
    private Swiftcam _camera;
    private Swiftcam.DelegateEventCallback _eventCallback;
    private StreamConfig _openedConfig;
    private int _sensorWidth;
    private int _sensorHeight;
    private (uint X, uint Y, uint W, uint H) _appliedRoi;
    private bool? _appliedAutoExposure;
    private int _appliedExposureUs = -1;
    private int _appliedGainPercent = -1;
    private volatile bool _disconnected;
    private volatile bool _hasError;
    private volatile bool _loggedFirstFrame;
    private volatile int _framesReceivedSinceStart;
    private int _consecutiveSilentFailures;

    // Frame buffer (filled by PullFrame on SDK thread, read by UploadLatestFrame on render thread)
    private readonly Lock _frameLock = new();
    private IntPtr _frameBuffer;
    private int _frameBufferCapacity;
    private int _expectedWidth;
    private int _expectedHeight;
    private int _expectedStride;
    private int _frameWidth;
    private int _frameHeight;
    private int _frameStride;
    private uint _frameSeq;
    private bool _hasNewFrame;

    // GPU texture (recreated when dimensions change)
    private Texture2D _gpuTexture;
    private int _gpuWidth;
    private int _gpuHeight;
    private Format _gpuFormat;

    // Status (read by IStatusProvider)
    private string _statusMessage = "";
    private StatusLevel _statusLevel = StatusLevel.Undefined;
}
