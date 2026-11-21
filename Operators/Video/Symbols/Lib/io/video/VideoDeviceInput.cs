#nullable enable
using System.Threading;
using Sdcb.FFmpeg.Utils;
using T3.Core.Utils;
using T3.Core.Video;
using T3.Graphics.Compat;
using T3.SystemUi;
using T3.VideoServices;

namespace Lib.io.video;

/// <summary>
/// Captures a webcam or capture card into a texture. Devices are opened through FFmpeg — DirectShow on
/// Windows, V4L2 on Linux, AVFoundation on macOS — with software decode, mirroring
/// <see cref="VideoStreamInput"/>. This replaces the OpenCV/DirectShowLib capture, which crashed in OpenCV's
/// native initialization and could not run on Linux at all.
/// </summary>
[Guid("34c2d58d-ae76-47b6-9713-02911a783341")]
public class VideoDeviceInput : Instance<VideoDeviceInput>, ICustomDropdownHolder, IStatusProvider, IRemoteVideoInput, ICameraSettingsHolder
{
    // Output slots
    [Output(Guid = "1d0159cc-33d2-46b1-9c0c-7054aa560df5", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> Texture = new();

    [Output(Guid = "9C2E4C11-09B6-4F4D-8F99-4A7372D5F2B5", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Int2> Resolution = new();

    [Output(Guid = "A1B2C3D4-5678-90EF-1234-567890ABCDEF", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public new readonly Slot<string> Status = new();

    /// <summary>Everything the camera reports as controls — kind, range, step, default, current value and
    /// auto/inactive state. Built by the control worker at refresh; not status-classified.</summary>
    [Output(Guid = "60e8db59-6f3a-4891-9232-3ff40c338432", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> ControlsCatalog = new();

    [Output(Guid = "868D5FFE-032C-4522-B56B-D96B30841DB7", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> UpdateCount = new();

    private const float Epsilon = 1e-4f;

    private readonly Lock _lockObject = new();

    [Input(Guid = "f5b900ec-ee17-123e-9972-cdd0580c104e")]
    public readonly InputSlot<string> InputDeviceName = new();

    [Input(Guid = "236D4C5C-0022-4416-A22C-D6DF73C306E2")]
    public readonly InputSlot<bool> Active = new();

    [Input(Guid = "49019D29-873E-4B7C-A897-C575A384A650", MappedType = typeof(ResolutionFpsTypeEnum))]
    public readonly InputSlot<int> ResolutionFpsType = new();

    [Input(Guid = "C9E1C1F6-3A18-4A1C-8A5E-4B4119965B6E")]
    public readonly InputSlot<Int2> CustomResolution = new();

    [Input(Guid = "11F7432D-31F1-44B9-8F75-B1569B314B13")]
    public readonly InputSlot<int> CustomFps = new();

    [Input(Guid = "8D2C28C7-1234-40E2-9388-75574519543D")]
    public readonly InputSlot<bool> OpenSettings = new();

    /// <summary>Values committed in the camera settings window, re-applied whenever the camera opens.</summary>
    [Input(Guid = "32F597E7-EB25-45BE-B37A-153214B1119E")]
    public readonly InputSlot<string> CameraSettings = new();

    [Input(Guid = "67E149A5-F7B6-47BC-B147-3B9B11C19C29")]
    public readonly InputSlot<bool> Reconnect = new();

    [Input(Guid = "22513B82-E77A-417A-8A46-24E677F072D4")]
    public readonly InputSlot<bool> DeactivateWhenNotShowing = new();

    [Input(Guid = "F187A997-7E4A-48C6-81F9-2A27F150A68A")]
    public readonly InputSlot<Vector2> Reposition = new();

    [Input(Guid = "805602D5-52B2-4A73-A337-12E00C3C91F2")]
    public readonly InputSlot<Vector2> Scale = new();

    [Input(Guid = "A57E815D-70C9-4D3B-998C-D13506B8F56E")]
    public readonly InputSlot<bool> FlipHorizontally = new();

    [Input(Guid = "3022DE8A-5D88-4A37-9799-780F2A838A5F")]
    public readonly InputSlot<bool> FlipVertically = new();

    [Input(Guid = "3022DE8A-5D88-4A37-9799-780F2A838A6E")]
    public readonly InputSlot<float> ApplyRotationData = new();

    /// <summary>Trigger that sends the ControlName/ControlValue/ControlAuto triple to the camera; self-clears.</summary>
    [Input(Guid = "723a1471-c002-44a5-9b9e-f325d716671f")]
    public readonly InputSlot<bool> ApplyControl = new();

    /// <summary>Strict two-way switch for the generic control surface: true engages the selected control's
    /// auto mode (ControlValue is ignored); false, the default, applies ControlValue as a manual value —
    /// which disengages the auto mode on every backend. There is deliberately no "leave auto untouched".</summary>
    [Input(Guid = "145615c4-c65b-49e9-abb5-78338579e7a3")]
    public readonly InputSlot<bool> ControlAuto = new();

    /// <summary>The device control to drive, chosen from everything the camera reports (see ControlsCatalog).</summary>
    [Input(Guid = "694ce2cc-cbdc-49d2-a591-6ac0d5fa16bd")]
    public readonly InputSlot<string> ControlName = new();

    /// <summary>Value in device units for the selected control; for menu controls, the selected item's
    /// CameraControlItem.Value (query index for MENU, the driver's own integer for integer menus).</summary>
    [Input(Guid = "06c894a8-fe69-4cf6-bf54-b9c155b8f035")]
    public readonly InputSlot<float> ControlValue = new();

    /// <summary>Brightness in device units; applied to the camera when the value changes.</summary>
    [Input(Guid = "D7A2F851-4C93-46B8-8E71-3B95C6E2D740")]
    public readonly InputSlot<float> Brightness = new();

    [Input(Guid = "84B6E3C9-1A52-4F7D-9E28-67D3F1A5B496")]
    public readonly InputSlot<float> Exposure = new();

    [Input(Guid = "A5F4D2B8-63E9-4C17-8D40-52B7E9C6F183")]
    public readonly InputSlot<float> Focus = new();

    [Input(Guid = "D91C4A7E-5B36-4E82-9F10-7C64A2D8E513")]
    public readonly InputSlot<float> Pan = new();

    [Input(Guid = "2E75B9C3-8D41-4A6F-B520-93E78F1C4A62")]
    public readonly InputSlot<float> Tilt = new();

    [Input(Guid = "6C38A1F4-92D7-4B5E-8C63-1F94B6D3A758")]
    public readonly InputSlot<float> Zoom = new();

    private CancellationTokenSource? _cancellationTokenSource;
    private Thread? _captureThread;

    // Edge-trigger latches (MathUtils.WasTriggered): a connected input held at true must not restart the
    // camera or re-open the settings window on every frame.
    private bool _reconnectWasTrue;
    private bool _openSettingsWasTrue;

    // DeactivateWhenNotShowing, done like WebcamInput: an operator that stops being evaluated cannot be
    // told about it, so the capture thread times the evaluation gap itself and releases the camera.
    private volatile bool _deactivateWhenIdle;
    private long _lastEvaluationTicks = Environment.TickCount64;

    // Format the running capture thread was started with — a format change needs a device reopen, while
    // flips / rotation / scale / reposition flow to the running thread through _sharedSettings.
    private (ResolutionFpsTypeEnum Type, Int2 Resolution, int Fps)? _runningFormat;

    // Latest CaptureThreadSettings snapshot, written by the evaluation thread and re-read by the capture
    // thread per frame, so transform edits apply without a restart.
    private volatile CaptureThreadSettings? _sharedSettings;

    // Size frames actually arrive in, for the camera settings window.
    private CameraMode _activeCameraMode;

    // Capture threads whose join timed out. They observe cancellation and exit on their own; the references
    // keep their token sources alive and let a restart wait for the device to be released.
    private readonly List<Thread> _orphanedThreads = [];

    // Instance variables
    private Texture2D? _gpuTexture;
    private int _height;
    private volatile string _lastStatusMessage = "";
    private int _storeDeviceIndex = -1;
    private int _width;

    // Owns ALL camera-control I/O on its own thread; created lazily on first use (no thread until the
    // first enqueue) and disposed with the op. Only touched from the evaluation thread.
    private CameraControlWorker? _controlWorker;
    private string _lastSeenControlMessage = "";

    // The settings dialog reads GetCameraModes/GetCameraSettings every frame and caches derived lists
    // by reference (ICameraSettingsHolder promises the same instance until something changes), so the
    // projections are rebuilt only when their source instance changes.
    private IReadOnlyList<CameraSetting> _cachedSettings = [];
    private CameraControlCatalog? _cachedSettingsCatalog;
    private IReadOnlyList<(int Width, int Height, double Fps)>? _cachedModesSource;
    private string? _cachedModesDevice;
    private IReadOnlyList<CameraMode> _cachedModesResult = [];

    // The most recent frame as packed RGBA, shared between the capture thread and the render thread.
    private byte[]? _sharedRgba;
    private int _sharedWidth;
    private int _sharedHeight;
    private bool _hasFrame;
    private string? _lastLoggedOpenError;

    // Frame handoff bookkeeping: the capture thread bumps the counter under the lock; the eval thread
    // uploads only when it sees a counter it has not uploaded yet.
    private int _sharedFrameCounter;
    private int _uploadedFrameCounter;
    private byte[]? _uploadBuffer;

    // Boygroup video replication. On a render node _remoteActive keeps the camera closed and the
    // master's received frame is presented instead.
    private string? _relayKey;
    private string _relayDisplayName = "Camera";
    private bool _remoteActive;
    private bool _remoteWasActive;
    private Texture2D? _remoteTexture;
    private int _remoteFrameCounter = -1;
    private int _presentedRemoteFrameCounter = -1;
    private string _remoteStatus = "";

    public VideoDeviceInput()
    {
        _basicConfigs =
            [
                (Pan, CameraControlNames.Pan),
                (Tilt, CameraControlNames.Tilt),
                (Zoom, CameraControlNames.Zoom),
                (Focus, CameraControlNames.Focus),
                (Exposure, CameraControlNames.Exposure),
                (Brightness, CameraControlNames.Brightness),
            ];

        Texture.UpdateAction = Update;
        UpdateCount.UpdateAction = Update;
        Resolution.UpdateAction = Update;
        Status.UpdateAction = Update;
        ControlsCatalog.UpdateAction += Update;

        // A boygroup master publishes this camera's frame; a render node gets the frame from the master.
        RemoteVideoInputBus.RegisterInput(this);
    }

    /// <summary>
    /// Identity shared by the master's and the render node's copy of this operator. The deployed project
    /// keeps its package id and instance path, so the two sides match without a manual mapping.
    /// </summary>
    public string RelayKey => _relayKey ??= BuildRelayKey();

    public string RelayDisplayName => _relayDisplayName;

    /// <summary>Master side: the frame captured locally, which the boygroup relay publishes over OMT.</summary>
    public Texture2D? RelayTexture => _gpuTexture;

    /// <summary>The local camera's own status, shown by the master when there is no frame to relay yet.</summary>
    public string RelayStatus => _lastStatusMessage;

    /// <summary>Render-node side: the master's frame replaces this machine's camera entirely.</summary>
    public void ApplyRemoteFrame(Texture2D? texture, int frameCounter, string status)
    {
        _remoteActive = true;
        _remoteTexture = texture;
        _remoteFrameCounter = frameCounter;
        _remoteStatus = status;
    }

    public void EndRemoteFrames()
    {
        _remoteActive = false;
        _remoteTexture = null;
        _remoteFrameCounter = -1;
        _remoteStatus = "";
    }

    private string BuildRelayKey()
    {
        var packageId = Symbol?.SymbolPackage?.Id ?? Guid.Empty;
        return packageId.ToString("N") + "/" + string.Join("/", InstancePath.Select(id => id.ToString("N")));
    }


    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return _lastStatusMessage.StartsWith("Error") ? IStatusProvider.StatusLevel.Error
                   : _lastStatusMessage.StartsWith("Warning") ? IStatusProvider.StatusLevel.Warning
                   : IStatusProvider.StatusLevel.Success;
    }

    public string GetStatusMessage()
    {
        return _lastStatusMessage;
    }

    private void SetStatus(string message)
    {
        _lastStatusMessage = message;
    }

    // An empty device list is generic on Windows/Linux but usually means the macOS privacy gate: without
    // camera access, AVFoundation hides every device from the process, which reads like a missing camera.
    private static string GetNoDevicesStatusMessage()
    {
        if (OperatingSystem.IsMacOS() && MacCameraPermission.TryGetVideoAuthorizationStatus(out var status))
        {
            switch (status)
            {
                case MacCameraPermission.AuthorizationStatus.Denied:
                case MacCameraPermission.AuthorizationStatus.Restricted:
                    return "Error: No capture devices found — macOS camera access is denied. "
                         + "Allow the editor under System Settings → Privacy & Security → Camera, then trigger Reconnect.";
                case MacCameraPermission.AuthorizationStatus.NotDetermined:
                    return "Error: No capture devices found — macOS camera permission has not been granted yet. "
                         + "Allow the editor under System Settings → Privacy & Security → Camera, then trigger Reconnect.";
            }
        }

        return "Error: No capture devices found.";
    }

    private void Update(EvaluationContext context)
    {
        // The capture thread watches this to honour DeactivateWhenNotShowing: an operator that stops being
        // evaluated cannot be told about it, so the thread times the gap itself.
        _lastEvaluationTicks = Environment.TickCount64;
        var deactivateWhenNotShowing = DeactivateWhenNotShowing.GetValue(context);
        _deactivateWhenIdle = deactivateWhenNotShowing;

        var configuredDevice = InputDeviceName.GetValue(context);
        var waitingForPeerMachine = ResolvePeerSelection(ref configuredDevice);
        if (!string.IsNullOrWhiteSpace(configuredDevice))
            _relayDisplayName = configuredDevice;

        // A boygroup render node presents the master's stream instead of opening a local camera.
        if (UpdateRemoteFrames())
            return;

        // A camera of another TiXL client is selected but its stream has not arrived yet: keep the local camera closed.
        if (waitingForPeerMachine != null)
        {
            StopCaptureThread();
            _controlWorker?.Dispose();
            _controlWorker = null;
            var waiting = $"Waiting for the camera on {waitingForPeerMachine}";
            SetStatus(waiting);
            Status.Value = waiting;
            return;
        }

        WebcamScan.Scan();

        var deviceName = configuredDevice;
        if (string.IsNullOrEmpty(deviceName) && WebcamScan.Devices.Count > 0)
        {
            deviceName = WebcamScan.Devices[0].Name;
        }

        // Edge-triggered: a connected input held at true must not restart the camera on every frame.
        var reconnectTriggered = MathUtils.WasTriggered(Reconnect.GetValue(context), ref _reconnectWasTrue);
        if (reconnectTriggered)
        {
            Reconnect.SetTypedInputValue(false);

            // The device scan is cached once per session; a reconnect is also the chance to pick up a
            // camera that was plugged in after startup.
            WebcamScan.Invalidate();
            WebcamScan.Scan();

            // A camera replaced at the same index (routine on Linux, where /dev/videoN is reused) would
            // otherwise leave the control worker holding a dead fd/filter — drop the cached handles and
            // re-enumerate what the (possibly new) camera exposes.
            if (WebcamScan.TryGetIndexForName(deviceName, out var reconnectedIndex))
            {
                // The name matched a device, so it is non-null here.
                var worker = GetOrCreateControlWorker();
                var device = new WebcamWithIndex(deviceName!, reconnectedIndex);
                worker.InvalidateHandles(device);
                worker.EnqueueRefresh(device);

                // Settings window values re-apply after the device's catalog refresh completes.
                ApplyStoredCameraSettings(context, device);
            }
        }

        // Read once, consumed in either branch below — the trigger always self-clears.
        var applyControlTriggered = ApplyControl.GetValue(context);

        if (!WebcamScan.TryGetIndexForName(deviceName, out var selectedDeviceIndex))
        {
            SetStatus(WebcamScan.Devices.Count > 0
                          ? $"Error: Camera '{deviceName}' not found."
                          : GetNoDevicesStatusMessage());
            StopCaptureThread();

            // Consume the trigger; the status above already explains the situation.
            if (applyControlTriggered)
                ApplyControl.SetTypedInputValue(false);
        }
        else
        {
            if (_lastSeenDeviceIndex != selectedDeviceIndex)
            {
                // A different camera: forget what was applied and send the configured values to it once.
                // (Tracked separately from _storeDeviceIndex, which only moves when capture actually
                // starts — the re-apply must also work while the capture is idle.)
                var hadPreviousDevice = _lastSeenDeviceIndex != -1;
                var previousDeviceIndex = _lastSeenDeviceIndex;
                _lastSeenDeviceIndex = selectedDeviceIndex;
                for (var i = 0; i < _lastAppliedConfigValues.Length; i++)
                    _lastAppliedConfigValues[i] = float.NaN;
                if (hadPreviousDevice)
                    _forceConfigApply = true;

                // Refresh the control catalog for the new device — also on the first sighting after
                // start — and drop the worker's cached handles for both devices: a camera replaced at
                // the same index must not be driven through a stale handle. The force-applied values
                // (below) are deferred by the worker until this refresh's snapshot arrives. The name
                // matched the device above, so it is non-null here.
                var worker = GetOrCreateControlWorker();
                if (hadPreviousDevice)
                    worker.InvalidateHandles(new WebcamWithIndex(deviceName!, previousDeviceIndex));
                var selectedDevice = new WebcamWithIndex(deviceName!, selectedDeviceIndex);
                worker.InvalidateHandles(selectedDevice);
                worker.EnqueueRefresh(selectedDevice);

                // Settings window values re-apply after the new device's catalog refresh completes.
                ApplyStoredCameraSettings(context, selectedDevice);
            }

            ApplyBasicConfigs(context, deviceName!, selectedDeviceIndex);

            var deviceIsActive = Active.GetValue(context);

            // One settings snapshot per evaluation: shared with the capture thread so flips / rotation /
            // scale / reposition edits apply live, while format changes (needing a device reopen) restart.
            var settings = new CaptureThreadSettings(this, context);
            _sharedSettings = settings;
            bool shouldBeCapturing = deviceIsActive && (!deactivateWhenNotShowing || IsActiveInGraph());

            if (shouldBeCapturing && IsCaptureThreadRunning())
            {
                var format = (settings.ResolutionFpsType, settings.CustomResolution, settings.CustomFps);
                if (format != _runningFormat)
                {
                    SetStatus("Restarting capture for the new format...");
                    StopCaptureThread();
                }
            }

            if (shouldBeCapturing)
            {
                if (IsCaptureThreadRunning() && (selectedDeviceIndex != _storeDeviceIndex || reconnectTriggered))
                {
                    StopCaptureThread();
                }

                if (!IsCaptureThreadRunning())
                {
                    _storeDeviceIndex = selectedDeviceIndex;
                    StartCaptureThread(settings);
                }
            }
            else
            {
                var wasCapturing = IsCaptureThreadRunning();
                StopCaptureThread();
                if (wasCapturing)
                    SetStatus("Stopped");
            }

            if (applyControlTriggered)
            {
                ApplyControl.SetTypedInputValue(false);
                EnqueueGenericControlApply(context, deviceName!, selectedDeviceIndex);
            }
        }

        if (MathUtils.WasTriggered(OpenSettings.GetValue(context), ref _openSettingsWasTrue))
        {
            OpenSettings.SetTypedInputValue(false);
            var openDeviceName = InputDeviceName.Value;
            if (!string.IsNullOrWhiteSpace(openDeviceName)
                && WebcamScan.TryGetIndexForName(openDeviceName, out var openDeviceIndex))
            {
                // Populate the control catalog for the window; the read runs on the control worker.
                var openDevice = new WebcamWithIndex(openDeviceName, openDeviceIndex);
                GetOrCreateControlWorker().EnqueueRefresh(openDevice);
            }

            if (!CameraSettingsWindow.TryOpen(this))
                SetStatus("Warning: The camera settings window is only available in the editor.");
        }

        // New frame? The camera delivers ~30 fps while the editor evaluates ~60 — uploading every update
        // would send each frame twice. The handoff copies under the lock (cheap memcpy) so the GPU work
        // below never blocks the capture thread mid-copy.
        var hasNewFrame = false;
        var frameWidth = 0;
        var frameHeight = 0;
        lock (_lockObject)
        {
            if (_hasFrame && _sharedRgba != null && _sharedFrameCounter != _uploadedFrameCounter)
            {
                var byteCount = _sharedWidth * _sharedHeight * 4;
                if (_uploadBuffer == null || _uploadBuffer.Length < byteCount)
                    _uploadBuffer = new byte[byteCount];

                Array.Copy(_sharedRgba, _uploadBuffer, byteCount);
                frameWidth = _sharedWidth;
                frameHeight = _sharedHeight;
                _uploadedFrameCounter = _sharedFrameCounter;
                hasNewFrame = true;
            }
        }

        if (hasNewFrame)
        {
            try
            {
                UploadFrameToGpu(_uploadBuffer!, frameWidth, frameHeight);
                Texture.Value = _gpuTexture!;
                UpdateCount.Value++;
                Resolution.Value = new Int2(frameWidth, frameHeight);
                _activeCameraMode = new CameraMode(frameWidth, frameHeight, 0);
            }
            catch (Exception e)
            {
                // A lost device must not escape the evaluation; keep showing the previous frame.
                SetStatus($"Error: Uploading the camera frame failed - {e.Message}");
            }
        }

        // Control-worker publications: a new control status message is copied into the op status once
        // (the capture loop keeps overwriting with "Running", like the old synchronous applies did), and
        // the catalog text feeds the ControlsCatalog output. No slot is ever written from the worker
        // thread — these are volatile reads of the worker's publications.
        if (_controlWorker != null)
        {
            var controlMessage = _controlWorker.LastControlMessage;
            if (controlMessage.Length > 0 && controlMessage != _lastSeenControlMessage)
            {
                _lastSeenControlMessage = controlMessage;
                SetStatus(controlMessage);
            }

            var catalogText = _controlWorker.CatalogText;
            if (ControlsCatalog.Value != catalogText)
                ControlsCatalog.Value = catalogText;
        }

        Status.Value = _lastStatusMessage;
    }

    /// <summary>
    /// Presents the frame a boygroup master sent for this operator. Returns true while remote control is
    /// active, in which case the local camera must stay closed.
    /// </summary>
    /// <summary>
    /// "Camera @ Machine" selects a camera of another TiXL client: the stream is requested through
    /// <see cref="PeerCameraBus"/>. A reference to this machine resolves to its own camera. Returns the machine
    /// being waited for, or null when the selection is a local camera.
    /// </summary>
    private string? ResolvePeerSelection(ref string? configuredDevice)
    {
        if (!PeerCameraDirectory.TryParse(configuredDevice, out var camera, out var machine))
        {
            if (_peerWantSet)
            {
                _peerWantSet = false;
                PeerCameraBus.ClearWant(RelayKey);
            }

            return null;
        }

        if (string.Equals(machine, PeerCameraDirectory.LocalMachine, StringComparison.OrdinalIgnoreCase))
        {
            configuredDevice = camera;
            if (_peerWantSet)
            {
                _peerWantSet = false;
                PeerCameraBus.ClearWant(RelayKey);
            }

            return null;
        }

        _peerWantSet = true;
        PeerCameraBus.SetWant(RelayKey, machine, camera);
        return machine;
    }

    private bool _peerWantSet;

    private bool UpdateRemoteFrames()
    {
        if (!_remoteActive)
        {
            if (_remoteWasActive)
            {
                _remoteWasActive = false;
                _presentedRemoteFrameCounter = -1;
                Texture.Value = null!;
                Resolution.Value = default;
                UpdateCount.Value++;
                SetStatus("Remote stream ended — resuming the local camera");
                Status.Value = _lastStatusMessage;
            }

            return false;
        }

        if (!_remoteWasActive)
        {
            _remoteWasActive = true;

            // Release the camera completely while the master drives this input; the worker is created
            // again if the stream ends and local capture resumes.
            StopCaptureThread();
            _controlWorker?.Dispose();
            _controlWorker = null;
        }

        var status = string.IsNullOrWhiteSpace(_remoteStatus) ? "Receiving video from the boygroup master" : _remoteStatus;
        SetStatus(status);
        Status.Value = status;
        ControlsCatalog.Value = "";

        Texture.Value = _remoteTexture!;
        if (_remoteTexture != null)
        {
            var description = _remoteTexture.Description;
            if (description.Width > 0 && description.Height > 0)
                Resolution.Value = new Int2(description.Width, description.Height);
        }
        else
        {
            Resolution.Value = default;
        }

        if (_remoteFrameCounter != _presentedRemoteFrameCounter)
        {
            _presentedRemoteFrameCounter = _remoteFrameCounter;
            UpdateCount.Value++;
        }

        return true;
    }

    private bool IsCaptureThreadRunning()
    {
        return _captureThread != null && _captureThread.IsAlive;
    }

    // The basic hardware configs, applied automatically whenever their input changes. Each entry remembers
    // the last value sent to the device (NaN = not sent yet), so unchanged inputs never touch the camera —
    // including at graph load, until a value is actually edited or patched. All entries are independent, so
    // several settings can be configured at once. Initialized in the constructor (references inputs).
    // Controls are addressed by canonical name (see CameraControlNames) instead of the old enum.
    private readonly (InputSlot<float> Input, string ControlName)[] _basicConfigs = null!;

    private readonly float[] _lastAppliedConfigValues =
        [float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN];

    // Set when the device changed: the next ApplyBasicConfigs sends the configured values even though they
    // are fresh baselines for the new camera.
    private bool _forceConfigApply;
    private int _lastSeenDeviceIndex = -1;

    private void ApplyBasicConfigs(EvaluationContext context, string deviceName, int deviceIndex)
    {
        var forceApply = _forceConfigApply;
        _forceConfigApply = false;

        for (var i = 0; i < _basicConfigs.Length; i++)
        {
            // GetValue (not .Value): the input's slot only picks up edited/patched values when evaluated.
            var value = _basicConfigs[i].Input.GetValue(context);
            if (float.IsNaN(value))
                continue;

            // The first sighting is the baseline, not a command — untouched inputs never force anything
            // onto the camera (and graph-load defaults like Focus=0 stay harmless).
            var last = _lastAppliedConfigValues[i];
            if (float.IsNaN(last) && !forceApply)
            {
                _lastAppliedConfigValues[i] = value;
                continue;
            }

            if (value == last)
                continue;

            // The apply itself runs on the control worker: the value lands as Manual (disengaging the
            // control's linked auto mode), with read-back and status text picked up a later evaluation —
            // success text, clamp notes and unsupported warnings no longer land in the same frame as the
            // edit. A changed value is enqueued even for a control the device lacks; the unsupported
            // warning comes from the worker's name resolution instead of a synchronous support gate. An
            // animated input coalesces into one pending apply instead of one device write per frame.
            var requestedValue = (int)MathF.Round(value);
            GetOrCreateControlWorker().EnqueueApply(new WebcamWithIndex(deviceName, deviceIndex),
                                                    _basicConfigs[i].ControlName, requestedValue, auto: null);
            _lastAppliedConfigValues[i] = value;
        }
    }

    private CameraControlWorker GetOrCreateControlWorker()
        => _controlWorker ??= new CameraControlWorker();

    // The generic control surface: apply whatever control the user picked from the catalog. Runs on the
    // eval thread only up to the enqueue — the device I/O happens on the worker.
    private void EnqueueGenericControlApply(EvaluationContext context, string deviceName, int deviceIndex)
    {
        var controlName = ControlName.GetValue(context);
        if (string.IsNullOrWhiteSpace(controlName))
        {
            SetStatus("Warning: Pick a control name (ControlName dropdown, see ControlsCatalog) before applying.");
            return;
        }

        var auto = ControlAuto.GetValue(context);
        var value = (int)MathF.Round(ControlValue.GetValue(context));
        // Strict two-way switch: engaging auto ignores the value; otherwise the value lands as Manual,
        // which disengages the control's auto mode on every backend.
        GetOrCreateControlWorker().EnqueueApply(new WebcamWithIndex(deviceName, deviceIndex), controlName,
                                                auto ? null : value, auto);
    }

    /// <summary>
    /// True once DeactivateWhenNotShowing is on and no evaluation has run for a while — the only way an
    /// operator can notice that nothing renders it, since an unrendered operator is never updated again.
    /// </summary>
    private bool IsPausedForIdleFrame()
        => _deactivateWhenIdle && Environment.TickCount64 - _lastEvaluationTicks > DeactivateTimeoutMs;

    private bool IsActiveInGraph() => !IsPausedForIdleFrame();

    private const int DeactivateTimeoutMs = 3000;
    private const int IdlePollIntervalMs = 250;

    private void StartCaptureThread(CaptureThreadSettings settings)
    {
        if (IsCaptureThreadRunning()) return;

        // A timed-out capture thread from a previous stop may still hold the device. Waiting for it
        // here would stall the evaluation thread for up to seconds per orphan; the thread observes
        // cancellation within ~100 ms, so deferring the start to the next evaluation costs one or
        // two frames instead - and the editor never blocks on a wedged driver call.
        _orphanedThreads.RemoveAll(t => !t.IsAlive);
        if (_orphanedThreads.Count > 0)
        {
            SetStatus("Waiting for the previous capture to release the camera...");
            return;
        }

        var device = WebcamScan.Devices.FirstOrDefault(d => d.Index == _storeDeviceIndex);
        if (device == null)
            return;

        _sharedSettings = settings;
        _runningFormat = (settings.ResolutionFpsType, settings.CustomResolution, settings.CustomFps);
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;

        _captureThread = new Thread(() => CaptureLoop(settings, device, token))
                         {
                             IsBackground = true,
                             Name = "Video Capture Thread",
                             Priority = ThreadPriority.BelowNormal
                         };
        _captureThread.Start();
    }

    private void StopCaptureThread()
    {
        if (!IsCaptureThreadRunning()) return;

        _cancellationTokenSource?.Cancel();

        // A short join keeps the evaluation thread responsive: normal cancellation is observed
        // within ~100 ms, so the common path still exits inline. A thread stuck in slow device
        // teardown joins as an orphan instead of stalling the editor for seconds — the start path
        // defers until it has released the camera, and disposing its token source under a running
        // thread is the one way this op could ever take the editor down, so the orphan keeps it.
        if (!_captureThread!.Join(CaptureThreadJoinMs))
        {
            Log.Debug("Video capture thread did not exit in time; leaving it to finish on its own.", this);
            _orphanedThreads.Add(_captureThread);
        }
        else
        {
            _cancellationTokenSource?.Dispose();
        }

        _captureThread = null;
        _cancellationTokenSource = null;
    }

    private const int CaptureThreadJoinMs = 100;

    private void CaptureLoop(CaptureThreadSettings settings, WebcamWithIndex device, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            // DeactivateWhenNotShowing: nothing evaluates this op any more, so stop holding the camera.
            // The thread parks here instead of exiting, ready to reopen the moment evaluation resumes.
            if (IsPausedForIdleFrame())
            {
                SetStatus("Paused (not rendered)");
                for (var waited = 0; waited < 1000 && !token.IsCancellationRequested; waited += 100)
                    Thread.Sleep(100);
                continue;
            }

            // Transforms are re-read per attempt; the frame loop below re-reads them per frame.
            settings = _sharedSettings ?? settings;
            var shouldRetry = TryCaptureFromDevice(settings, device, token);
            if (!shouldRetry)
                return;

            // The device never opened or died mid-capture (unplugged, driver hiccup). The old OpenCV
            // implementation gave up here and needed a manual reconnect; retrying keeps live sessions alive.
            SetStatus(_lastStatusMessage + " Retrying...");
            // Poll instead of WaitOne: unlike a wait handle, reading the token is safe even if its source
            // got disposed on the eval thread while this thread was busy.
            for (var waited = 0; waited < 1000 && !token.IsCancellationRequested; waited += 100)
                Thread.Sleep(100);
        }
    }

    /// <summary>Returns true when the device should be retried (open/read failure); false when done.</summary>
    private bool TryCaptureFromDevice(CaptureThreadSettings settings, WebcamWithIndex device, CancellationToken token)
    {
        VideoDecoderSession? session = null;
        SoftwareFrameConverter? converter = null;
        try
        {
            session = TryOpenDevice(settings, device, out var customFormatAccepted, out var error);
            if (session == null)
            {
                // Another capture op or application streaming the camera is the common cause of a busy
                // rejection — say so, or the retry loop reads like a mystery.
                if (error != null && (error.Contains("-16") || error.Contains("busy", StringComparison.OrdinalIgnoreCase)))
                    error += " — the camera is in use by another capture op or application.";
                else if (OperatingSystem.IsMacOS() && !MacCameraPermission.IsGrantedOrUnknown())
                    error = "macOS camera access is not granted — allow the editor under "
                          + "System Settings → Privacy & Security → Camera, then trigger Reconnect.";

                SetStatus($"Error: Failed to open camera '{device.Name}'. {error}");
                // The retry loop can run for a long time (device unplugged); log only state changes.
                if (_lastLoggedOpenError != error)
                {
                    _lastLoggedOpenError = error;
                    Log.Warning($"VideoDeviceInput: Could not open '{device.Name}': {error}", this);
                }

                return true;
            }
            _lastLoggedOpenError = null;

            // Capture devices deliver SDR; forcing RGBA8 keeps flips/warp and the texture upload on one
            // well-defined 4-byte layout.
            converter = new SoftwareFrameConverter(hdr: false);

            var requestedWidth = settings.CustomResolution.X;
            var requestedHeight = settings.CustomResolution.Y;
            // The Custom inputs default to 0 — an unset part means "no preference", not a 0x0@0fps request.
            var hasRequestedSize = requestedWidth > 0 && requestedHeight > 0;
            var hasRequestedFps = settings.CustomFps > 0;
            var fpsText = session.FrameRate > 0 ? $"@{session.FrameRate:F2}fps" : "";

            if (settings.ResolutionFpsType == ResolutionFpsTypeEnum.Custom && (hasRequestedSize || hasRequestedFps))
            {
                if (!hasRequestedSize || (session.Width == requestedWidth && session.Height == requestedHeight))
                {
                    SetStatus($"Success: Running at {session.Width}x{session.Height}{fpsText}" +
                              (hasRequestedFps ? $" (requested {requestedWidth}x{requestedHeight}@{settings.CustomFps}fps)."
                                               : " (requested device default)."));
                }
                else
                {
                    SetStatus($"Warning: Using {session.Width}x{session.Height}{fpsText} " +
                              $"(requested {requestedWidth}x{requestedHeight}@{settings.CustomFps}fps). (Software resize)");
                }
            }
            else
            {
                SetStatus($"Running at {session.Width}x{session.Height}{fpsText} (device default).");
            }

            // When the device didn't take the requested size, swscale resizes each frame during conversion.
            var wantsResize = settings.ResolutionFpsType == ResolutionFpsTypeEnum.Custom
                              && hasRequestedSize
                              && (session.Width != requestedWidth || session.Height != requestedHeight);
            var targetWidth = wantsResize ? requestedWidth : session.Width;
            var targetHeight = wantsResize ? requestedHeight : session.Height;

            while (!token.IsCancellationRequested)
            {
                // Pause exit: the thread ends and the camera is released; the next evaluation restarts it.
                if (IsPausedForIdleFrame())
                    return false;

                if (!session.TryReadNextFrame(out _))
                {
                    SetStatus($"Error: Lost camera '{device.Name}'.");
                    return true;
                }

                // Re-read the latest snapshot per frame: flip / warp edits apply while the camera runs.
                var frameSettings = _sharedSettings ?? settings;
                var rgba = converter.Convert(session.CurrentFrame, targetWidth, targetHeight);
                var byteCount = rgba.Width * rgba.Height * converter.BytesPerPixel;

                lock (_lockObject)
                {
                    if (_sharedRgba == null || _sharedRgba.Length < byteCount)
                        _sharedRgba = new byte[byteCount];

                    CopyTransformedPixels(rgba, converter.BytesPerPixel, frameSettings, _sharedRgba);
                    _sharedWidth = rgba.Width;
                    _sharedHeight = rgba.Height;
                    _hasFrame = true;
                    _sharedFrameCounter++;
                }

                // Only rewrite on transition — keep the last notable event visible instead of
                // stomping it 30x per second with a static message.
                if (_lastStatusMessage != "Running")
                    SetStatus("Running");
            }

            return false;
        }
        catch (Exception e)
        {
            SetStatus($"Error: Capture failed - {e.Message}");
            Log.Debug($"Video capture thread failed: {e}", this);
            return true;
        }
        finally
        {
            converter?.Dispose();
            session?.Dispose();
        }
    }

    private static VideoDecoderSession? TryOpenDevice(CaptureThreadSettings settings, WebcamWithIndex device,
                                                      out bool customFormatAccepted, out string? error)
    {
        customFormatAccepted = false;
        var url = BuildCaptureUrl(device, out var inputFormat);

        if (settings.ResolutionFpsType == ResolutionFpsTypeEnum.Custom)
        {
            var session = VideoDecoderSession.TryOpen(url, VideoPlaybackOptimization.FastSeeking, out error,
                                                       BuildDemuxerOptions(settings, includeFormatSettings: true), inputFormat,
                                                       lowLatency: true);
            if (session != null)
            {
                customFormatAccepted = true;
                return session;
            }

            // V4L2 cameras commonly offer their higher resolutions only as MJPEG; YUYV is bandwidth-limited.
            // (dshow negotiates sizes itself, and avfoundation either negotiates or hard-fails the open —
            // neither wants the extra rawvideo attempt; the default open below handles both.)
            if (OperatingSystem.IsLinux())
            {
                var mjpegOptions = BuildDemuxerOptions(settings, includeFormatSettings: true)!;
                mjpegOptions["input_format"] = "mjpeg";
                session = VideoDecoderSession.TryOpen(url, VideoPlaybackOptimization.FastSeeking, out error, mjpegOptions, inputFormat,
                                                      lowLatency: true);
                if (session != null)
                {
                    customFormatAccepted = true;
                    return session;
                }
            }
        }

        // Device-default format — when a custom size was requested it is resized in software instead.
        return VideoDecoderSession.TryOpen(url, VideoPlaybackOptimization.FastSeeking, out error,
                                           BuildDefaultDemuxerOptions(), inputFormat,
                                           lowLatency: true);
    }

    internal static string BuildCaptureUrl(WebcamWithIndex device, out string inputFormat)
    {
        if (OperatingSystem.IsWindows())
        {
            inputFormat = "dshow";
            return $"video={device.Name}";
        }

        if (OperatingSystem.IsMacOS())
        {
            // avfoundation's URL is "[[VIDEO]:[AUDIO]]" — each field a device NAME or index; a bare name
            // selects the video input by prefix match against the device's localizedName. The name beats
            // the index here: without camera permission the device list is empty, which silently
            // re-numbers indices — and an out-of-range index can even fall into FFmpeg's screen-capture
            // range instead of failing the open.
            inputFormat = "avfoundation";
            return device.Name;
        }

        inputFormat = "v4l2";
        return $"/dev/video{device.Index}";
    }

    private static Dictionary<string, string>? BuildDemuxerOptions(CaptureThreadSettings settings, bool includeFormatSettings)
    {
        if (!includeFormatSettings || settings.ResolutionFpsType != ResolutionFpsTypeEnum.Custom)
            return null;

        // Omit unset parts (0) instead of requesting 0x0@0 — the demuxer rejects those outright.
        var options = new Dictionary<string, string>();
        var (width, height) = (settings.CustomResolution.X, settings.CustomResolution.Y);
        if (width > 0 && height > 0)
            options["video_size"] = $"{width}x{height}";
        if (settings.CustomFps > 0)
            options["framerate"] = settings.CustomFps.ToString();
        else if (OperatingSystem.IsMacOS())
            options["framerate"] = DefaultMacFramerate;

        return options.Count > 0 ? options : null;
    }

    private const string DefaultMacFramerate = "30";

    /// <summary>
    /// Options for the device-default open. On macOS a fully default open cannot work: avfoundation's
    /// built-in framerate default is ntsc (29.97), which most 30 fps webcams reject with "Selected
    /// framerate ... is not supported" — that demuxer hard-fails instead of negotiating (ffmpeg's
    /// avfoundation quirk), so the open needs an explicit framerate. 30 is the sensible webcam default;
    /// the pixel format stays at its default (yuv420p, auto-overridden by the demuxer if the device
    /// cannot deliver it).
    /// </summary>
    internal static Dictionary<string, string>? BuildDefaultDemuxerOptions()
    {
        return OperatingSystem.IsMacOS()
                   ? new Dictionary<string, string> { ["framerate"] = DefaultMacFramerate }
                   : null;
    }

    #region Frame transforms (replaces the OpenCV flip/resize/warp pipeline)

    /// <summary>
    /// Copies the converted frame into the shared buffer, applying flips and — when a rotation/scale/
    /// reposition transform is active — the affine warp. Resizing is handled upstream by swscale.
    /// </summary>
    private static unsafe void CopyTransformedPixels(Frame rgba, int bytesPerPixel, CaptureThreadSettings settings, byte[] destination)
    {
        var stride = rgba.Linesize[0];
        var source = (byte*)rgba.Data[0];
        var rowBytes = rgba.Width * bytesPerPixel;

        if (settings.HasTransformation() && bytesPerPixel == 4)
        {
            WarpAffine(source, stride, rgba.Width, rgba.Height, settings, destination);
            return;
        }

        fixed (byte* target = destination)
        {
            if (stride == rowBytes && !settings.FlipHorizontally && !settings.FlipVertically)
            {
                var total = (long)rowBytes * rgba.Height;
                System.Buffer.MemoryCopy(source, target, total, total);
                return;
            }

            for (var y = 0; y < rgba.Height; y++)
            {
                var srcRow = source + (long)(settings.FlipVertically ? rgba.Height - 1 - y : y) * stride;
                var dstRow = target + (long)y * rowBytes;

                if (!settings.FlipHorizontally)
                {
                    System.Buffer.MemoryCopy(srcRow, dstRow, rowBytes, rowBytes);
                    continue;
                }

                // RGBA pixels are 4-byte aligned — reverse as uints instead of per-byte (4x fewer loops).
                if (bytesPerPixel == 4)
                {
                    var src32 = (uint*)srcRow;
                    var dst32 = (uint*)dstRow;
                    for (var x = 0; x < rgba.Width; x++)
                        dst32[x] = src32[rgba.Width - 1 - x];
                    continue;
                }

                for (var x = 0; x < rgba.Width; x++)
                {
                    var s = srcRow + (rgba.Width - 1 - x) * bytesPerPixel;
                    var d = dstRow + (long)x * bytesPerPixel;
                    for (var b = 0; b < bytesPerPixel; b++)
                        d[b] = s[b];
                }
            }
        }
    }

    /// <summary>
    /// Bilinear affine warp with the same destination-to-source matrix the old OpenCV implementation built
    /// (GetRotationMatrix2D-style with separate X/Y scale, plus reposition). The flips are composed in as
    /// coordinate inversions — they used to run as a separate pass before the warp, which this matches.
    /// Samples outside the source read as transparent black, like OpenCV's constant border. Pure managed
    /// code, so it only runs when a transform is actually requested; the identity path stays a memcpy.
    /// </summary>
    private static unsafe void WarpAffine(byte* source, int stride, int width, int height,
                                          CaptureThreadSettings settings, byte[] destination)
    {
        var centerX = width / 2f;
        var centerY = height / 2f;
        var angleRad = settings.Rotation * Math.PI / 180.0;
        var cos = (float)Math.Cos(angleRad);
        var sin = (float)Math.Sin(angleRad);

        var m00 = cos * settings.Scale.X;
        var m01 = sin * settings.Scale.Y;
        var m10 = -sin * settings.Scale.X;
        var m11 = cos * settings.Scale.Y;
        var m02 = settings.Reposition.X + (1 - m00) * centerX - m01 * centerY;
        var m12 = settings.Reposition.Y + (1 - m11) * centerY - m10 * centerX;

        var flipHorizontally = settings.FlipHorizontally;
        var flipVertically = settings.FlipVertically;

        fixed (byte* target = destination)
        {
            var dstRow = target;
            for (var y = 0; y < height; y++, dstRow += width * 4)
            {
                var baseU = m01 * y + m02;
                var baseV = m11 * y + m12;
                var dstPixel = dstRow;
                for (var x = 0; x < width; x++, dstPixel += 4)
                {
                    var u = baseU + m00 * x;
                    var v = baseV + m10 * x;
                    if (flipHorizontally) u = width - 1 - u;
                    if (flipVertically) v = height - 1 - v;

                    if (u < 0 || v < 0 || u > width - 1 || v > height - 1)
                    {
                        *(uint*)dstPixel = 0;
                        continue;
                    }

                    var x0 = (int)u;
                    var y0 = (int)v;
                    var x1 = x0 + 1 < width ? x0 + 1 : x0;
                    var y1 = y0 + 1 < height ? y0 + 1 : y0;
                    var fx = u - x0;
                    var fy = v - y0;
                    var w00 = (1 - fx) * (1 - fy);
                    var w10 = fx * (1 - fy);
                    var w01 = (1 - fx) * fy;
                    var w11 = fx * fy;

                    var p00 = source + (long)y0 * stride + (long)x0 * 4;
                    var p10 = source + (long)y0 * stride + (long)x1 * 4;
                    var p01 = source + (long)y1 * stride + (long)x0 * 4;
                    var p11 = source + (long)y1 * stride + (long)x1 * 4;

                    for (var c = 0; c < 4; c++)
                    {
                        var value = p00[c] * w00 + p10[c] * w10 + p01[c] * w01 + p11[c] * w11;
                        dstPixel[c] = (byte)(value + 0.5f);
                    }
                }
            }
        }
    }

    #endregion

    private unsafe void UploadFrameToGpu(byte[] buffer, int width, int height)
    {
        if (width <= 0 || height <= 0) return;

        if (_gpuTexture == null || _width != width || _height != height)
        {
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _gpuTexture);
            _width = width;
            _height = height;

            _gpuTexture = Texture2D.CreateTexture2D(new Texture2DDescription
                                                       {
                                                           Width = width,
                                                           Height = height,
                                                           MipLevels = 1,
                                                           ArraySize = 1,
                                                           Format = Format.R8G8B8A8_UNorm,
                                                           SampleDescription = new SampleDescription(1, 0),
                                                           Usage = ResourceUsage.Default,
                                                           BindFlags = BindFlags.ShaderResource,
                                                           CpuAccessFlags = CpuAccessFlags.None,
                                                           OptionFlags = ResourceOptionFlags.None
                                                       });
        }

        fixed (byte* pixels = buffer)
        {
            var dataBox = new DataBox((IntPtr)pixels, width * 4, 0);
            ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, _gpuTexture, 0);
        }
    }

    protected override void Dispose(bool disposing)
    {
        // No IsDisposed guard: the instance is flagged disposed before this runs, so a guard here skips
        // the whole teardown and leaves the camera open with its capture thread running.
        if (disposing)
        {
            RemoteVideoInputBus.UnregisterInput(this);
            if (_peerWantSet && _relayKey != null)
                PeerCameraBus.ClearWant(_relayKey);

            StopCaptureThread();
            _controlWorker?.Dispose();
            _controlWorker = null;
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _gpuTexture);

            lock (_lockObject)
            {
                _sharedRgba = null;
                _hasFrame = false;
            }
        }

        base.Dispose(disposing);
    }

    private sealed record CaptureThreadSettings
    {
        public readonly int CustomFps;
        public readonly Int2 CustomResolution;
        public readonly bool FlipHorizontally;
        public readonly bool FlipVertically;
        public readonly Vector2 Reposition;
        public readonly ResolutionFpsTypeEnum ResolutionFpsType;
        public readonly float Rotation;
        public readonly Vector2 Scale;

        public CaptureThreadSettings(VideoDeviceInput owner, EvaluationContext context)
        {
            ResolutionFpsType = (ResolutionFpsTypeEnum)owner.ResolutionFpsType.GetValue(context);
            CustomResolution = owner.CustomResolution.GetValue(context);
            CustomFps = owner.CustomFps.GetValue(context);
            FlipVertically = owner.FlipVertically.GetValue(context);
            FlipHorizontally = owner.FlipHorizontally.GetValue(context);
            Rotation = owner.ApplyRotationData.GetValue(context);
            Reposition = owner.Reposition.GetValue(context);
            Scale = owner.Scale.GetValue(context);
        }

        public bool HasTransformation()
        {
            return Math.Abs(Rotation) > Epsilon ||
                   Math.Abs(Scale.X - 1.0f) > Epsilon || Math.Abs(Scale.Y - 1.0f) > Epsilon ||
                   Math.Abs(Reposition.X) > Epsilon || Math.Abs(Reposition.Y) > Epsilon;
        }
    }

    private enum ResolutionFpsTypeEnum
    {
        DeviceDefault = 0,
        Custom = 1
    }

    #region Device Configuration UI
    /// <summary>
    /// Re-applies the values committed in the camera settings window (the <see cref="CameraSettings"/>
    /// input) after a camera opens, reconnects or the device changes. The worker defers these applies
    /// until the device's catalog snapshot exists, so they resolve against the right control set.
    /// </summary>
    private void ApplyStoredCameraSettings(EvaluationContext context, WebcamWithIndex device)
    {
        var stored = CameraSettings.GetValue(context);
        if (string.IsNullOrWhiteSpace(stored))
            return;

        foreach (var line in stored.Split('\n'))
        {
            var parts = line.Split('\t');
            if (parts.Length != 3 || !int.TryParse(parts[1], out var value))
                continue;

            var isAuto = string.Equals(parts[2], "auto", StringComparison.OrdinalIgnoreCase);
            if (isAuto)
                GetOrCreateControlWorker().EnqueueApply(device, parts[0], null, true);
            else if (!string.Equals(parts[2], "off", StringComparison.OrdinalIgnoreCase))
                GetOrCreateControlWorker().EnqueueApply(device, parts[0], value, null);
            else
                GetOrCreateControlWorker().EnqueueApply(device, parts[0], null, false);
        }
    }

    public void OpenCrossbarSettings()
    {
        Log.Debug("Crossbar settings not implemented for this version.", this);
    }
    #endregion

    #region Camera settings window
    string ICameraSettingsHolder.CameraSettingsDeviceName => InputDeviceName.Value ?? "";

    string ICameraSettingsHolder.CameraSettingsStatus
        => _controlWorker != null && _controlWorker.LastControlMessage.Length > 0
               ? _controlWorker.LastControlMessage
               : "Reading device controls...";

    IReadOnlyList<CameraSetting> ICameraSettingsHolder.GetCameraSettings()
    {
        var catalog = _controlWorker?.CurrentCatalog;
        if (catalog == null || catalog.Controls.Count == 0)
            return [];

        // The settings dialog reads this every frame and caches derived lists by reference
        // (ICameraSettingsHolder promises the same instance until something changes) — rebuild
        // only when the worker publishes a new catalog.
        if (!ReferenceEquals(catalog, _cachedSettingsCatalog))
        {
            _cachedSettingsCatalog = catalog;
            _cachedSettings = BuildCameraSettingsList(catalog);
        }

        return _cachedSettings;
    }

    /** Flattens the control worker's catalog into the settings dialog's row model. */
    private static IReadOnlyList<CameraSetting> BuildCameraSettingsList(CameraControlCatalog catalog)
    {
        var settings = new List<CameraSetting>(catalog.Controls.Count);
        foreach (var control in catalog.Controls)
        {
            settings.Add(new CameraSetting(
                control.Name,
                GroupForControl(control.Name),
                control.Kind switch
                {
                    CameraControlKind.Integer => CameraSettingKind.Integer,
                    CameraControlKind.Boolean => CameraSettingKind.Boolean,
                    CameraControlKind.Menu or CameraControlKind.IntegerMenu => CameraSettingKind.Menu,
                    _ => CameraSettingKind.Button,
                },
                control.Minimum,
                control.Maximum,
                control.Step,
                control.DefaultValue,
                control.CurrentValue,
                !string.IsNullOrEmpty(control.AutoControlName),
                control.CurrentIsAuto,
                (control.States & CameraControlStates.ReadOnly) != 0,
                control.Items.Select(item => new CameraSettingItem(item.Value, item.Label)).ToList(),
                IsSupported: true));
        }

        return settings;
    }

    void ICameraSettingsHolder.ApplyCameraSetting(string name, int? value, bool? auto)
    {
        var deviceName = InputDeviceName.Value;
        if (string.IsNullOrWhiteSpace(deviceName)
            || !WebcamScan.TryGetIndexForName(deviceName, out var deviceIndex))
        {
            SetStatus($"Warning: Cannot apply '{name}' — no camera selected or the device list is stale.");
            return;
        }

        GetOrCreateControlWorker().EnqueueApply(new WebcamWithIndex(deviceName, deviceIndex), name, value, auto);
    }

    Guid ICameraSettingsHolder.StoredCameraSettingsInputId => CameraSettings.Id;

    /** Tab-separated lines: Name<TAB>Value<TAB>auto|manual|off. "off" = manual without a value. */
    string ICameraSettingsHolder.SerializeCameraSettings(IReadOnlyDictionary<string, (int Value, bool IsAuto)> settings)
        => string.Join("\n", settings.Select(kv => $"{kv.Key}\t{kv.Value.Value}\t{(kv.Value.IsAuto ? "auto" : "manual")}"));

    IReadOnlyList<CameraMode> ICameraSettingsHolder.GetCameraModes()
    {
        // WebcamScan caches the capability sweep; the dialog reads this per frame and caches derived
        // lists by reference, so rebuild only when the capabilities instance changes (hot-plug rescan)
        // or the selected device changes.
        var device = InputDeviceName.Value;
        var capabilities = WebcamScan.GetCapabilities(device);
        if (capabilities.Count == 0)
            return [];

        if (!ReferenceEquals(capabilities, _cachedModesSource)
            || !string.Equals(device, _cachedModesDevice, StringComparison.Ordinal))
        {
            _cachedModesSource = capabilities;
            _cachedModesDevice = device;
            _cachedModesResult = capabilities
                                 .DistinctBy(c => (c.Width, c.Height, c.Fps))
                                 .OrderByDescending(c => c.Width * c.Height)
                                 .ThenByDescending(c => c.Fps)
                                 .Select(c => new CameraMode(c.Width, c.Height, (int)Math.Round(c.Fps)))
                                 .ToList();
        }

        return _cachedModesResult;
    }

    CameraMode ICameraSettingsHolder.RequestedCameraMode
        => (ResolutionFpsTypeEnum)ResolutionFpsType.Value == ResolutionFpsTypeEnum.Custom
               ? new CameraMode(CustomResolution.Value.X, CustomResolution.Value.Y, CustomFps.Value)
               : default;

    CameraMode ICameraSettingsHolder.ActiveCameraMode => _activeCameraMode;

    CameraFormatInputs ICameraSettingsHolder.FormatInputs
        => new(ResolutionFpsType.Id, (int)ResolutionFpsTypeEnum.DeviceDefault, (int)ResolutionFpsTypeEnum.Custom,
               CustomResolution.Id, CustomFps.Id);

    /** Image vs camera vs driver-extra grouping for the settings window tabs. */
    private static CameraSettingGroup GroupForControl(string name)
    {
        foreach (var imageControl in ImageControlNames)
        {
            if (string.Equals(name, imageControl, StringComparison.OrdinalIgnoreCase))
                return CameraSettingGroup.Image;
        }

        foreach (var cameraControl in OpticsControlNames)
        {
            if (string.Equals(name, cameraControl, StringComparison.OrdinalIgnoreCase))
                return CameraSettingGroup.Camera;
        }

        return CameraSettingGroup.Other;
    }

    private static readonly string[] ImageControlNames =
        [
            CameraControlNames.Brightness, CameraControlNames.Contrast, CameraControlNames.Hue,
            CameraControlNames.Saturation, CameraControlNames.Sharpness, CameraControlNames.Gamma,
            CameraControlNames.WhiteBalance, CameraControlNames.BacklightCompensation,
            CameraControlNames.Gain, CameraControlNames.ColorEnable, CameraControlNames.PowerLineFrequency,
        ];

    private static readonly string[] OpticsControlNames =
        [
            CameraControlNames.Pan, CameraControlNames.Tilt, CameraControlNames.Roll,
            CameraControlNames.Zoom, CameraControlNames.Focus, CameraControlNames.Iris,
            CameraControlNames.Exposure,
        ];
    #endregion

    #region Dropdown UI
    string? ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        if (inputId == InputDeviceName.Id) return InputDeviceName.Value;
        if (inputId == CustomResolution.Id) return $"{CustomResolution.Value.X}x{CustomResolution.Value.Y}";
        if (inputId == CustomFps.Id) return CustomFps.Value.ToString();
        if (inputId == ControlName.Id) return ControlName.Value;
        if (inputId == ControlValue.Id)
        {
            // Menu-kind controls display their selected item as "value — label"; anything else the raw value.
            var control = FindSelectedControl();
            if (control is { Kind: CameraControlKind.Menu or CameraControlKind.IntegerMenu })
            {
                var value = (int)ControlValue.Value;
                foreach (var item in control.Items)
                {
                    if (item.Value != value)
                        continue;

                    return item.Label.Length > 0 ? $"{item.Value} — {item.Label}" : item.Value.ToString();
                }
            }

            return ((int)ControlValue.Value).ToString();
        }

        return string.Empty;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        if (inputId == InputDeviceName.Id)
        {
            var peerCameras = new List<string>();
            PeerCameraDirectory.AppendReferences(peerCameras);
            if (WebcamScan.Devices.Count == 0 && peerCameras.Count == 0)
            {
                yield return "No devices found";
                yield break;
            }

            foreach (var (webcam, _) in WebcamScan.Devices)
                yield return webcam;

            // Cameras of the other TiXL clients in a collab, boygroup or failover session.
            foreach (var reference in peerCameras)
                yield return reference;
        }
        else if (inputId == CustomResolution.Id)
        {
            var deviceName = InputDeviceName.Value;
            foreach (var (w, h, _) in WebcamScan.GetCapabilities(deviceName).DistinctBy(c => (c.Width, c.Height)))
                yield return $"{w}x{h}";

            if (!WebcamScan.GetCapabilities(deviceName).Any())
                yield return "640x480";
        }
        else if (inputId == CustomFps.Id)
        {
            var deviceName = InputDeviceName.Value;
            foreach (var (_, _, fps) in WebcamScan.GetCapabilities(deviceName).DistinctBy(c => c.Fps))
                yield return $"{fps:F0}";

            if (!WebcamScan.GetCapabilities(deviceName).Any())
                yield return "30";
        }
        else if (inputId == ControlName.Id)
        {
            // One flat list of the device's controls in catalog order — the dropdown idiom has no grouping.
            var catalog = _controlWorker?.CurrentCatalog;
            if (catalog == null || catalog.Controls.Count == 0)
            {
                yield return "No controls found";
                yield break;
            }

            foreach (var control in catalog.Controls)
                yield return control.Name;
        }
        else if (inputId == ControlValue.Id)
        {
            // Second dropdown: the selected control's menu items as "value — label" strings. For non-menu
            // controls it offers nothing and the typed value is used as-is.
            var control = FindSelectedControl();
            if (control == null || (control.Kind != CameraControlKind.Menu && control.Kind != CameraControlKind.IntegerMenu))
                yield break;

            foreach (var item in control.Items)
                yield return item.Label.Length > 0 ? $"{item.Value} — {item.Label}" : item.Value.ToString();
        }
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (inputId == InputDeviceName.Id && selected != null)
            InputDeviceName.SetTypedInputValue(selected);
        else if (inputId == CustomResolution.Id)
        {
            if (selected != null)
            {
                var parts = selected.Split('x');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out var w) &&
                    int.TryParse(parts[1], out var h))
                {
                    CustomResolution.SetTypedInputValue(new Int2(w, h));

                    var deviceName = InputDeviceName.Value;
                    var caps = WebcamScan.GetCapabilities(deviceName);
                    if (caps.Any() && !caps.Any(c => c.Width == w && c.Height == h))
                    {
                        SetStatus("Warning: Unsupported resolution—may fallback.");
                    }
                }
            }
        }
        else if (inputId == CustomFps.Id)
        {
            if (double.TryParse(selected, out var fps))
            {
                CustomFps.SetTypedInputValue((int)fps);
            }
        }
        else if (inputId == ControlName.Id)
        {
            if (!string.IsNullOrEmpty(selected) && selected != "No controls found")
                ControlName.SetTypedInputValue(selected);
        }
        else if (inputId == ControlValue.Id)
        {
            // Options carry the item's Value, never a position ("12 — 50 Hz" → 12).
            var separatorIndex = selected?.IndexOf(" — ", StringComparison.Ordinal) ?? -1;
            var valueText = separatorIndex >= 0 ? selected![..separatorIndex] : selected;
            if (int.TryParse(valueText, out var itemValue))
                ControlValue.SetTypedInputValue(itemValue);
        }
    }

    private CameraControlDescriptor? FindSelectedControl()
        => _controlWorker?.CurrentCatalog?.Find(ControlName.Value);
    #endregion

}
