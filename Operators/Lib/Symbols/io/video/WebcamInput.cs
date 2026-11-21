#nullable enable
using System.Threading;
using T3.Core.Utils;
using T3.Graphics.Compat;
using T3.SystemUi;

namespace Lib.io.video;

[Guid("cd5a182e-254b-4e65-820b-ff754122614c")]
public class WebcamInput : Instance<WebcamInput>, ICustomDropdownHolder, IStatusProvider, ICameraSettingsHolder
{
    [Output(Guid = "1d0159cc-33d2-46b1-9c0c-7054aa560df5", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> Texture = new();

    [Output(Guid = "9C2E4C11-09B6-4F4D-8F99-4A7372D5F2B5", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Int2> Resolution = new();

    [Output(Guid = "A1B2C3D4-5678-90EF-1234-567890ABCDEF", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public new readonly Slot<string> Status = new();

    [Output(Guid = "868D5FFE-032C-4522-B56B-D96B30841DB7", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> UpdateCount = new();

    /// <summary>Every control the camera reports, with range, default and live value.</summary>
    [Output(Guid = "f23112d1-2f77-4afe-813d-c6a51ab1f6a4", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> ControlsCatalog = new();

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
    [Input(Guid = "4e15a154-74ee-4fa7-9a31-62f23e8f5d61")]
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

    /// <summary>When on, Control1..4 values are 0..1 over each control's range; otherwise device units.</summary>
    [Input(Guid = "13553eca-d106-4eb6-afe9-095425b19b98")]
    public readonly InputSlot<bool> NormalizedControlValues = new();

    [Input(Guid = "2f1e2465-34c3-40b1-94c5-375e7a8d733f")]
    public readonly InputSlot<string> Control1 = new();

    [Input(Guid = "71948b15-97d5-4a01-858f-d07bb3f60a26")]
    public readonly InputSlot<float> Control1Value = new();

    [Input(Guid = "ffb8bf70-1ef8-4e11-921c-fb76c8f8a4e6")]
    public readonly InputSlot<string> Control2 = new();

    [Input(Guid = "5605ce18-38fd-45dc-bdba-19dc19f8b909")]
    public readonly InputSlot<float> Control2Value = new();

    [Input(Guid = "e557173c-1835-4ef2-98da-0828b24a4ca1")]
    public readonly InputSlot<string> Control3 = new();

    [Input(Guid = "340481bb-c924-4bb1-a565-54d5bea3f2ea")]
    public readonly InputSlot<float> Control3Value = new();

    [Input(Guid = "a3dd2efe-f31e-4f3c-bcd1-aab1972b9af5")]
    public readonly InputSlot<string> Control4 = new();

    [Input(Guid = "1ad30e74-ec75-43da-8e1c-4f9b55f14977")]
    public readonly InputSlot<float> Control4Value = new();

    public WebcamInput()
    {
        Texture.UpdateAction = Update;
        UpdateCount.UpdateAction = Update;
        Resolution.UpdateAction = Update;
        Status.UpdateAction = Update;
        ControlsCatalog.UpdateAction = Update;

        _controlNameInputs = [Control1, Control2, Control3, Control4];
        _controlValueInputs = [Control1Value, Control2Value, Control3Value, Control4Value];
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        if (IsError(_lastStatusMessage))
            return IStatusProvider.StatusLevel.Error;

        return _cameraControls.LastWarning.Length > 0 ? IStatusProvider.StatusLevel.Warning : IStatusProvider.StatusLevel.Success;
    }

    public string GetStatusMessage()
    {
        if (IsError(_lastStatusMessage) || _cameraControls.LastWarning.Length == 0)
            return _lastStatusMessage;

        return _cameraControls.LastWarning;
    }

    /** Ordinal: the culture-sensitive overload would let a locale decide whether a message is an error. */
    private static bool IsError(string message) => message.StartsWith("Error", StringComparison.Ordinal);

    private void Update(EvaluationContext context)
    {
        // The capture thread watches this to honour DeactivateWhenNotShowing: an operator that stops being
        // evaluated cannot be told about it, so the thread times the gap itself.
        _lastEvaluationMs = Environment.TickCount64;
        CameraSources.Provider?.EnsureStarted();

        // Edge-triggered: a connected input held at true must not restart the camera on every frame.
        var reconnect = MathUtils.WasTriggered(Reconnect.GetValue(context), ref _reconnectWasTrue);
        if (reconnect)
            Reconnect.SetTypedInputValue(false);

        var isCustomMode = (ResolutionFpsTypeEnum)ResolutionFpsType.GetValue(context) == ResolutionFpsTypeEnum.Custom;
        var customResolution = CustomResolution.GetValue(context);
        var streamSettings = new StreamSettings(InputDeviceName.GetValue(context) ?? string.Empty,
                                                isCustomMode
                                                    ? new CameraMode(customResolution.X, customResolution.Y, CustomFps.GetValue(context))
                                                    : default);

        var imageTransform = new ImageTransform(FlipHorizontally.GetValue(context),
                                                FlipVertically.GetValue(context),
                                                ApplyRotationData.GetValue(context),
                                                Scale.GetValue(context),
                                                Reposition.GetValue(context));

        _deactivateWhenIdle = DeactivateWhenNotShowing.GetValue(context);

        if (Active.GetValue(context))
        {
            if (IsCaptureThreadRunning() && (reconnect || streamSettings != _activeStreamSettings))
                StopCaptureThread();

            if (!IsCaptureThreadRunning())
                StartCaptureThread(streamSettings);
        }
        else
        {
            StopCaptureThread();
        }

        // A replugged camera can come back at another native index and has lost its settings: let the
        // controls find it again and re-apply what is stored. Swapping in one step matters: the capture
        // thread may set the flag between a read and a clear, which would drop the notification.
        var streamReopened = Interlocked.Exchange(ref _streamReopenSignal, 0) != 0;
        UpdateCameraControls(context, streamSettings.DeviceName, reconnect || streamReopened);

        if (MathUtils.WasTriggered(OpenSettings.GetValue(context), ref _openSettingsWasTrue))
        {
            OpenSettings.SetTypedInputValue(false);
            _cameraControls.RequestControls();
            if (!CameraSettingsWindow.TryOpen(this))
                Log.Warning("The camera settings window is only available in the editor.", this);
        }

        // Only a buffer swap happens under the lock; the upload runs outside it so the capture thread
        // never waits for the GPU.
        var hasNewFrame = false;
        int frameWidth, frameHeight;
        lock (_frameLock)
        {
            _imageTransform = imageTransform;
            frameWidth = _publishedWidth;
            frameHeight = _publishedHeight;
            if (_hasNewFrame)
            {
                _hasNewFrame = false;
                (_uploadPixels, _publishedPixels) = (_publishedPixels, _uploadPixels);
                hasNewFrame = true;
            }
        }

        if (hasNewFrame)
        {
            try
            {
                UploadFrameToGpu(_uploadPixels, frameWidth, frameHeight);
                _activeMode = new CameraMode(frameWidth, frameHeight, 0);
                Texture.Value = _gpuTexture;
                Resolution.Value = new Int2(frameWidth, frameHeight);
                UpdateCount.Value++;
            }
            catch (Exception e)
            {
                // A lost device must not escape the evaluation; keep showing the previous frame.
                SetStatus($"Error: Uploading the camera frame failed - {e.Message}");
            }
        }

        Status.Value = _lastStatusMessage;
    }

    private void UpdateCameraControls(EvaluationContext context, string deviceName, bool reconnect)
    {
        for (var slotIndex = 0; slotIndex < CameraControlSession.SlotCount; slotIndex++)
        {
            _controlNames[slotIndex] = _controlNameInputs[slotIndex].GetValue(context);
            _controlValues[slotIndex] = _controlValueInputs[slotIndex].GetValue(context);
        }

        _cameraControls.Update(deviceName, reconnect, CameraSettings.GetValue(context),
                               _controlNames, _controlValues, NormalizedControlValues.GetValue(context));

        var catalogText = _cameraControls.CatalogText;
        if (!ReferenceEquals(ControlsCatalog.Value, catalogText))
            ControlsCatalog.Value = catalogText;
    }

    private bool IsCaptureThreadRunning() => _captureThread is { IsAlive: true };

    private void StartCaptureThread(StreamSettings settings)
    {
        // A stopped thread that hasn't exited yet may still hold the camera; start once it has let go.
        _stoppingThreads.RemoveAll(static thread => !thread.IsAlive);
        if (_stoppingThreads.Count > 0)
        {
            SetStatus("Waiting for the previous capture to release the camera...");
            return;
        }

        _activeStreamSettings = settings;
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;

        _captureThread = new Thread(() => CaptureLoop(settings, token))
                             {
                                 IsBackground = true,
                                 Name = "Video Capture Thread",
                                 Priority = ThreadPriority.BelowNormal
                             };
        _captureThread.Start();
    }

    private void StopCaptureThread()
    {
        // Release the references of threads that already exited, whether or not one is running now.
        _stoppingThreads.RemoveAll(static thread => !thread.IsAlive);

        if (_captureThread != null)
        {
            // Never wait on the evaluation thread: opening a camera can block for seconds (permission
            // prompts, slow drivers). The thread sees the cancellation and exits on its own. Its token
            // source is not disposed, because the thread still waits on its handle.
            _cancellationTokenSource?.Cancel();
            if (_captureThread.IsAlive)
                _stoppingThreads.Add(_captureThread);

            _captureThread = null;
            _cancellationTokenSource = null;
        }

        _activeMode = default; // "zero before the first frame" — the settings window must not show a stale size.

        // Also when no thread is running: a start that had to wait for a previous capture leaves that
        // "Waiting for the previous capture..." message behind, and the operator is off now.
        if (!string.Equals(_lastStatusMessage, StoppedStatus, StringComparison.Ordinal))
            SetStatus(StoppedStatus);
    }

    /** Keeps trying until cancelled, so a camera that is busy or unplugged resumes on its own. */
    private void CaptureLoop(StreamSettings settings, CancellationToken token)
    {
        try
        {
            var provider = CameraSources.Provider;
            if (provider == null)
            {
                SetStatus("Error: Cameras are not available in this application.");
                return;
            }

            var reopenDelayMs = ReopenIntervalMs;

            while (!token.IsCancellationRequested)
            {
                // Nothing evaluates this operator any more, so stop holding the camera (and the bus
                // bandwidth) for a graph that is not being rendered. Parking here keeps the thread ready
                // to reopen the moment the operator is evaluated again.
                if (IsPausedForIdleFrame())
                {
                    SetStatus("Paused (not rendered)");
                    token.WaitHandle.WaitOne(IdlePollIntervalMs);
                    continue;
                }

                var attemptStartedMs = Environment.TickCount64;
                var deliveredFrames = false;
                using (var stream = provider.TryOpen(settings.DeviceName, settings.Mode, out var error))
                {
                    if (stream == null)
                    {
                        SetStatus($"Error: {error}");
                    }
                    else
                    {
                        SetStatus("Waiting for first frame...");
                        Interlocked.Exchange(ref _streamReopenSignal, 1);
                        deliveredFrames = ReadFramesUntilStreamEnds(stream, token);

                        // A stop closed the stream on purpose, and its state reports that as a disconnect.
                        // Reporting it here would overwrite the "Stopped" status with an error the user
                        // cannot act on — the reason a normal stop showed "Camera disconnected".
                        if (token.IsCancellationRequested)
                            return;

                        if (stream.State == CameraStreamState.Denied)
                        {
                            SetStatus("Error: Access to the camera was denied.");
                            return;
                        }

                        if (stream.State == CameraStreamState.Disconnected)
                            SetStatus("Error: Camera disconnected.");
                    }
                }

                // Back off while the camera keeps failing: a device that has just been closed often needs a
                // moment before it can be opened again, and reopening it every second is what turns that
                // into a camera that never comes back. A stream that ran for a while, or delivered frames
                // before dying, is a real disconnect — retry those at the normal pace.
                var lifetimeMs = Environment.TickCount64 - attemptStartedMs;
                if (deliveredFrames || lifetimeMs >= HealthyStreamMs)
                    reopenDelayMs = ReopenIntervalMs;
                else
                    reopenDelayMs = Math.Min(reopenDelayMs * 2, MaxReopenIntervalMs);

                token.WaitHandle.WaitOne(reopenDelayMs);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Video capture thread failed: {e.Message}", this);
            SetStatus($"Error: Capture failed - {e.Message}");
        }
    }

    /** Reads until the stream ends or the capture is cancelled; true if at least one frame arrived. */
    private bool ReadFramesUntilStreamEnds(ICameraStream stream, CancellationToken token)
    {
        var capturedPixels = Array.Empty<byte>();
        var scratchPixels = Array.Empty<byte>();
        var lastReportedSize = new Int2(0, 0);
        var deliveredFrames = false;

        while (!token.IsCancellationRequested && stream.State == CameraStreamState.Open)
        {
            // Leaving closes the stream; the caller's loop parks until the operator is evaluated again.
            if (IsPausedForIdleFrame())
                return deliveredFrames;

            if (!stream.TryReadFrame(ref capturedPixels, out var width, out var height))
            {
                Thread.Sleep(FramePollIntervalMs);
                continue;
            }

            deliveredFrames = true;

            if (width != lastReportedSize.X || height != lastReportedSize.Y)
            {
                lastReportedSize = new Int2(width, height);
                SetStatus($"Running at {width}x{height}");
            }

            ImageTransform imageTransform;
            lock (_frameLock)
            {
                imageTransform = _imageTransform;
            }

            ApplyImageTransform(imageTransform, ref capturedPixels, ref scratchPixels, width, height);

            // Swapping keeps the render thread's lock wait independent of the frame size.
            lock (_frameLock)
            {
                (_publishedPixels, capturedPixels) = (capturedPixels, _publishedPixels);
                _publishedWidth = width;
                _publishedHeight = height;
                _hasNewFrame = true;
            }
        }

        return deliveredFrames;
    }

    private static void ApplyImageTransform(in ImageTransform transform, ref byte[] pixels, ref byte[] scratch, int width, int height)
    {
        var byteCount = width * height * BytesPerPixel;
        if (scratch.Length < byteCount)
            scratch = new byte[byteCount];

        // These two inputs have always had each other's effect, and existing projects depend on the look they got.
        if (transform.FlipVertically)
            MirrorLeftToRight(pixels, width, height);

        if (transform.FlipHorizontally)
            MirrorTopToBottom(pixels, scratch, width, height);

        if (transform.HasAffinePart)
        {
            Warp(transform, pixels, scratch, width, height);
            (pixels, scratch) = (scratch, pixels);
        }
    }

    private static void MirrorLeftToRight(byte[] pixels, int width, int height)
    {
        var packedPixels = MemoryMarshal.Cast<byte, uint>(pixels.AsSpan());
        for (var row = 0; row < height; row++)
        {
            packedPixels.Slice(row * width, width).Reverse();
        }
    }

    private static void MirrorTopToBottom(byte[] pixels, byte[] scratch, int width, int height)
    {
        var rowByteCount = width * BytesPerPixel;
        var rowBuffer = scratch.AsSpan(0, rowByteCount);
        for (var row = 0; row < height / 2; row++)
        {
            var top = pixels.AsSpan(row * rowByteCount, rowByteCount);
            var bottom = pixels.AsSpan((height - 1 - row) * rowByteCount, rowByteCount);
            top.CopyTo(rowBuffer);
            bottom.CopyTo(top);
            rowBuffer.CopyTo(bottom);
        }
    }

    /** Bilinear resampling around the image center, leaving transparent black where no source pixel lands. */
    private static void Warp(in ImageTransform transform, byte[] source, byte[] destination, int width, int height)
    {
        var centerX = width * 0.5f;
        var centerY = height * 0.5f;
        var angle = transform.RotationDegrees * MathF.PI / 180f;
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);

        var m00 = cos * transform.Scale.X;
        var m01 = sin * transform.Scale.Y;
        var m10 = -sin * transform.Scale.X;
        var m11 = cos * transform.Scale.Y;
        var offsetX = transform.Reposition.X + (1f - m00) * centerX - m01 * centerY;
        var offsetY = transform.Reposition.Y - m10 * centerX + (1f - m11) * centerY;

        var byteCount = width * height * BytesPerPixel;
        var determinant = m00 * m11 - m01 * m10;
        if (MathF.Abs(determinant) < Epsilon)
        {
            destination.AsSpan(0, byteCount).Clear();
            return;
        }

        var inverse00 = m11 / determinant;
        var inverse01 = -m01 / determinant;
        var inverse10 = -m10 / determinant;
        var inverse11 = m00 / determinant;

        var stride = width * BytesPerPixel;
        var destinationIndex = 0;
        for (var y = 0; y < height; y++)
        {
            var relativeY = y - offsetY;
            for (var x = 0; x < width; x++, destinationIndex += BytesPerPixel)
            {
                var relativeX = x - offsetX;
                var sourceX = inverse00 * relativeX + inverse01 * relativeY;
                var sourceY = inverse10 * relativeX + inverse11 * relativeY;

                var left = (int)MathF.Floor(sourceX);
                var top = (int)MathF.Floor(sourceY);
                if (left < 0 || top < 0 || left >= width - 1 || top >= height - 1)
                {
                    destination.AsSpan(destinationIndex, BytesPerPixel).Clear();
                    continue;
                }

                var blendX = sourceX - left;
                var blendY = sourceY - top;
                var topLeftIndex = top * stride + left * BytesPerPixel;
                for (var channel = 0; channel < BytesPerPixel; channel++)
                {
                    var upper = MathUtils.Lerp(source[topLeftIndex + channel], source[topLeftIndex + BytesPerPixel + channel], blendX);
                    var lower = MathUtils.Lerp(source[topLeftIndex + stride + channel], source[topLeftIndex + stride + BytesPerPixel + channel], blendX);
                    destination[destinationIndex + channel] = (byte)MathUtils.Lerp(upper, lower, blendY);
                }
            }
        }
    }

    private unsafe void UploadFrameToGpu(byte[] pixels, int width, int height)
    {
        if (_gpuTexture == null || _gpuTexture.Description.Width != width || _gpuTexture.Description.Height != height)
        {
            GraphicsUtilities.Dispose(ref _gpuTexture);

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

        fixed (byte* pixelData = pixels)
        {
            var dataBox = new DataBox((IntPtr)pixelData, width * BytesPerPixel, 0);
            ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, _gpuTexture, 0);
        }
    }

    /// <summary>
    /// True once DeactivateWhenNotShowing is on and no evaluation has run for a while — the only way an
    /// operator can notice that nothing renders it, since an unrendered operator is never updated again.
    /// </summary>
    private bool IsPausedForIdleFrame()
        => _deactivateWhenIdle && Environment.TickCount64 - _lastEvaluationMs > DeactivateTimeoutMs;

    private void SetStatus(string message) => _lastStatusMessage = message;

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        StopCaptureThread();
        _cameraControls.Dispose();
        GraphicsUtilities.Dispose(ref _gpuTexture);
    }

    #region Camera settings window
    string ICameraSettingsHolder.CameraSettingsDeviceName => _cameraControls.DeviceName;

    string ICameraSettingsHolder.CameraSettingsStatus => _cameraControls.Status;

    IReadOnlyList<CameraSetting> ICameraSettingsHolder.GetCameraSettings() => _cameraControls.GetSettings();

    void ICameraSettingsHolder.ApplyCameraSetting(string name, int? value, bool? auto) => _cameraControls.Apply(name, value, auto);

    Guid ICameraSettingsHolder.StoredCameraSettingsInputId => CameraSettings.Id;

    string ICameraSettingsHolder.SerializeCameraSettings(IReadOnlyDictionary<string, (int Value, bool IsAuto)> settings)
        => CameraControlSession.Serialize(settings);

    IReadOnlyList<CameraMode> ICameraSettingsHolder.GetCameraModes()
    {
        var modesTask = _cameraModesTask;
        if (modesTask is { IsCompleted: true })
        {
            _cameraModesTask = null;
            if (modesTask.IsCompletedSuccessfully)
                _cameraModes = modesTask.Result;
        }

        // Listing cameras takes the provider's lock, which a camera being opened can hold for seconds:
        // never on the main thread.
        var now = Environment.TickCount64;
        var deviceName = InputDeviceName.Value ?? string.Empty;
        var isStale = now - _cameraModesReadMs > CameraModesRefreshIntervalMs
                      || !string.Equals(deviceName, _cameraModesDeviceName, StringComparison.Ordinal);
        var provider = CameraSources.Provider;
        if (_cameraModesTask == null && isStale && provider != null)
        {
            _cameraModesReadMs = now;
            _cameraModesDeviceName = deviceName;
            _cameraModesTask = Task.Run(() => ReadCameraModes(provider, deviceName));
        }

        return _cameraModes;
    }

    CameraMode ICameraSettingsHolder.RequestedCameraMode
        => (ResolutionFpsTypeEnum)ResolutionFpsType.Value == ResolutionFpsTypeEnum.Custom
               ? new CameraMode(CustomResolution.Value.X, CustomResolution.Value.Y, CustomFps.Value)
               : default;

    CameraMode ICameraSettingsHolder.ActiveCameraMode => _activeMode;

    CameraFormatInputs ICameraSettingsHolder.FormatInputs
        => new(ResolutionFpsType.Id, (int)ResolutionFpsTypeEnum.DeviceDefault, (int)ResolutionFpsTypeEnum.Custom,
               CustomResolution.Id, CustomFps.Id);

    private static IReadOnlyList<CameraMode> ReadCameraModes(ICameraProvider provider, string deviceName)
    {
        var cameras = provider.GetCameras();
        var camera = cameras.FirstOrDefault(c => c.Name == deviceName) ?? cameras.FirstOrDefault();
        if (camera == null)
            return [];

        return camera.Modes
                     .Distinct()
                     .OrderByDescending(mode => mode.Width * mode.Height)
                     .ThenByDescending(mode => mode.Fps)
                     .ToArray();
    }
    #endregion

    #region Dropdown UI
    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        if (inputId == InputDeviceName.Id)
            return InputDeviceName.Value;

        if (inputId == CustomResolution.Id)
            return $"{CustomResolution.Value.X}x{CustomResolution.Value.Y}";

        if (inputId == CustomFps.Id)
            return CustomFps.Value.ToString();

        var slotIndex = GetControlSlotIndex(inputId);
        if (slotIndex >= 0)
            return _controlNameInputs[slotIndex].Value ?? string.Empty;

        return string.Empty;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        if (GetControlSlotIndex(inputId) >= 0)
            return _cameraControls.GetControlNames();

        var cameras = GetCameraOptions();

        if (inputId == InputDeviceName.Id)
        {
            if (cameras.Count == 0)
                return [NoDevicesOption];

            return cameras.Select(camera => camera.Name);
        }

        var selectedName = InputDeviceName.Value;
        var selectedCamera = cameras.FirstOrDefault(camera => camera.Name == selectedName) ?? cameras.FirstOrDefault();
        var modes = selectedCamera?.Modes ?? [];

        if (inputId == CustomResolution.Id)
            return modes.Select(mode => $"{mode.Width}x{mode.Height}").Distinct();

        if (inputId == CustomFps.Id)
            return modes.Select(mode => mode.Fps.ToString()).Distinct();

        return [];
    }

    /// <summary>
    /// The camera list behind the dropdowns. Reading it takes the provider's lock — which a camera being
    /// opened can hold for seconds (permission prompts, slow drivers) — and lists the modes of every camera,
    /// so the dropdowns draw from a cached list that a background task refreshes instead of calling into the
    /// provider on the evaluation thread. The first read is inline, so a dropdown is never empty.
    /// </summary>
    private IReadOnlyList<CameraInfo> GetCameraOptions()
    {
        var task = _cameraOptionsTask;
        if (task is { IsCompleted: true })
        {
            _cameraOptionsTask = null;
            if (task.IsCompletedSuccessfully)
                _cameraOptions = task.Result;
        }

        var now = Environment.TickCount64;
        var provider = CameraSources.Provider;
        if (provider == null || _cameraOptionsTask != null || now - _cameraOptionsReadMs < CameraOptionsRefreshIntervalMs)
            return _cameraOptions;

        _cameraOptionsReadMs = now;

        // Nothing to show yet: reading inline keeps the first dropdown useful. A plugged-in camera is still
        // picked up by the refreshes that follow.
        if (_cameraOptions.Count == 0)
            _cameraOptions = provider.GetCameras();
        else
            _cameraOptionsTask = Task.Run(provider.GetCameras);

        return _cameraOptions;
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (selected == null)
            return;

        var slotIndex = GetControlSlotIndex(inputId);
        if (slotIndex >= 0)
        {
            SelectControlForSlot(slotIndex, selected);
            return;
        }

        if (inputId == InputDeviceName.Id)
        {
            if (selected != NoDevicesOption)
                InputDeviceName.SetTypedInputValue(selected);
        }
        else if (inputId == CustomResolution.Id)
        {
            var parts = selected.Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height))
                CustomResolution.SetTypedInputValue(new Int2(width, height));
        }
        else if (inputId == CustomFps.Id)
        {
            if (int.TryParse(selected, out var fps))
                CustomFps.SetTypedInputValue(fps);
        }
    }
    private int GetControlSlotIndex(Guid inputId)
    {
        for (var slotIndex = 0; slotIndex < CameraControlSession.SlotCount; slotIndex++)
        {
            if (_controlNameInputs[slotIndex].Id == inputId)
                return slotIndex;
        }

        return -1;
    }

    /** Starts the slot at the camera's current value, so picking a control doesn't make the image jump. */
    private void SelectControlForSlot(int slotIndex, string selected)
    {
        if (selected == CameraControlSession.NoControlOption)
        {
            _controlNameInputs[slotIndex].SetTypedInputValue(string.Empty);
            return;
        }

        _controlNameInputs[slotIndex].SetTypedInputValue(selected);

        var control = _cameraControls.FindControl(selected);
        if (control?.CurrentValue is not { } currentValue)
            return;

        _controlValueInputs[slotIndex].SetTypedInputValue(NormalizedControlValues.Value
                                                              ? CameraControlSession.Normalize(control, currentValue)
                                                              : currentValue);
    }
    #endregion

    private readonly record struct StreamSettings(string DeviceName, CameraMode Mode);

    private readonly record struct ImageTransform(bool FlipHorizontally, bool FlipVertically, float RotationDegrees, Vector2 Scale, Vector2 Reposition)
    {
        public bool HasAffinePart => MathF.Abs(RotationDegrees) > Epsilon
                                     || MathF.Abs(Scale.X - 1f) > Epsilon || MathF.Abs(Scale.Y - 1f) > Epsilon
                                     || MathF.Abs(Reposition.X) > Epsilon || MathF.Abs(Reposition.Y) > Epsilon;
    }

    private enum ResolutionFpsTypeEnum
    {
        DeviceDefault = 0,
        Custom = 1
    }

    private const float Epsilon = 1e-4f;
    private const int BytesPerPixel = 4;
    private const int FramePollIntervalMs = 2;
    private const int ReopenIntervalMs = 1000;

    /** A stream that lasted this long was working, so a failure after it is a real disconnect, not thrash. */
    private const int HealthyStreamMs = 1500;

    /** Ceiling of the reopen backoff: a camera that is gone should not be polled harder than this. */
    private const int MaxReopenIntervalMs = 5000;

    /** How long capture may stay unused before DeactivateWhenNotShowing releases the camera. */
    private const int DeactivateTimeoutMs = 3000;
    private const int IdlePollIntervalMs = 250;

    private const string StoppedStatus = "Stopped";
    private const string NoDevicesOption = "No devices found";

    private readonly Lock _frameLock = new();

    private readonly CameraControlSession _cameraControls = new();

    /** Set by the capture thread when a camera opened, consumed by the next evaluation. */
    private int _streamReopenSignal;

    /** Written by the evaluation thread, read by the capture thread; a stale read only delays a pause. */
    private volatile bool _deactivateWhenIdle;

    private long _lastEvaluationMs = Environment.TickCount64;
    private CameraMode _activeMode;

    /** Camera list for the dropdowns; see <see cref="GetCameraOptions"/>. */
    private IReadOnlyList<CameraInfo> _cameraOptions = [];
    private Task<IReadOnlyList<CameraInfo>>? _cameraOptionsTask;
    private long _cameraOptionsReadMs = long.MinValue / 2;
    private const long CameraOptionsRefreshIntervalMs = 3000;

    private IReadOnlyList<CameraMode> _cameraModes = [];
    private Task<IReadOnlyList<CameraMode>>? _cameraModesTask;
    private string? _cameraModesDeviceName;
    private long _cameraModesReadMs = long.MinValue / 2;
    private const long CameraModesRefreshIntervalMs = 3000;
    private readonly InputSlot<string>[] _controlNameInputs;
    private readonly InputSlot<float>[] _controlValueInputs;
    private readonly string?[] _controlNames = new string?[CameraControlSession.SlotCount];
    private readonly float[] _controlValues = new float[CameraControlSession.SlotCount];

    private CancellationTokenSource? _cancellationTokenSource;
    private Thread? _captureThread;
    private readonly List<Thread> _stoppingThreads = [];
    private bool _reconnectWasTrue;
    private bool _openSettingsWasTrue;
    private StreamSettings _activeStreamSettings;
    private volatile string _lastStatusMessage = string.Empty;
    private Texture2D? _gpuTexture;

    private ImageTransform _imageTransform;
    private byte[] _publishedPixels = [];
    private byte[] _uploadPixels = [];
    private int _publishedWidth;
    private int _publishedHeight;
    private bool _hasNewFrame;
}
