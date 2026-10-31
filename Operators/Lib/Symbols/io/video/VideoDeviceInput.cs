#nullable enable
using System.Threading;
using T3.Core.Utils;
using T3.Graphics.Compat;
using T3.SystemUi;

namespace Lib.io.video;

[Guid("cd5a182e-254b-4e65-820b-ff754122614c")]
public class VideoDeviceInput : Instance<VideoDeviceInput>, ICustomDropdownHolder, IStatusProvider
{
    [Output(Guid = "1d0159cc-33d2-46b1-9c0c-7054aa560df5", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> Texture = new();

    [Output(Guid = "9C2E4C11-09B6-4F4D-8F99-4A7372D5F2B5", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Int2> Resolution = new();

    [Output(Guid = "A1B2C3D4-5678-90EF-1234-567890ABCDEF", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public new readonly Slot<string> Status = new();

    [Output(Guid = "868D5FFE-032C-4522-B56B-D96B30841DB7", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> UpdateCount = new();

    [Input(Guid = "236D4C5C-0022-4416-A22C-D6DF73C306E2")]
    public readonly InputSlot<bool> Active = new();

    [Input(Guid = "3022DE8A-5D88-4A37-9799-780F2A838A6E")]
    public readonly InputSlot<float> ApplyRotationData = new();

    [Input(Guid = "11F7432D-31F1-44B9-8F75-B1569B314B13")]
    public readonly InputSlot<int> CustomFps = new();

    [Input(Guid = "C9E1C1F6-3A18-4A1C-8A5E-4B4119965B6E")]
    public readonly InputSlot<Int2> CustomResolution = new();

    [Input(Guid = "22513B82-E77A-417A-8A46-24E677F072D4")]
    public readonly InputSlot<bool> DeactivateWhenNotShowing = new();

    [Input(Guid = "A57E815D-70C9-4D3B-998C-D13506B8F56E")]
    public readonly InputSlot<bool> FlipHorizontally = new();

    [Input(Guid = "3022DE8A-5D88-4A37-9799-780F2A838A5F")]
    public readonly InputSlot<bool> FlipVertically = new();

    [Input(Guid = "f5b900ec-ee17-123e-9972-cdd0580c104e")]
    public readonly InputSlot<string> InputDeviceName = new();

    [Input(Guid = "8D2C28C7-1234-40E2-9388-75574519543D")]
    public readonly InputSlot<bool> OpenSettings = new();

    [Input(Guid = "67E149A5-F7B6-47BC-B147-3B9B11C19C29")]
    public readonly InputSlot<bool> Reconnect = new();

    [Input(Guid = "F187A997-7E4A-48C6-81F9-2A27F150A68A")]
    public readonly InputSlot<Vector2> Reposition = new();

    [Input(Guid = "49019D29-873E-4B7C-A897-C575A384A650", MappedType = typeof(ResolutionFpsTypeEnum))]
    public readonly InputSlot<int> ResolutionFpsType = new();

    [Input(Guid = "805602D5-52B2-4A73-A337-12E00C3C91F2")]
    public readonly InputSlot<Vector2> Scale = new();

    public VideoDeviceInput()
    {
        Texture.UpdateAction = Update;
        UpdateCount.UpdateAction = Update;
        Resolution.UpdateAction = Update;
        Status.UpdateAction = Update;
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return _lastStatusMessage.StartsWith("Error") ? IStatusProvider.StatusLevel.Error : IStatusProvider.StatusLevel.Success;
    }

    public string GetStatusMessage()
    {
        return _lastStatusMessage;
    }

    private void Update(EvaluationContext context)
    {
        Cameras.Provider?.EnsureStarted();

        var reconnect = Reconnect.GetValue(context);
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

        if (OpenSettings.GetValue(context))
        {
            OpenSettings.SetTypedInputValue(false);
            Log.Warning("Native camera settings dialogs are no longer available.", this);
        }

        lock (_frameLock)
        {
            _imageTransform = imageTransform;
            if (_hasNewFrame)
            {
                _hasNewFrame = false;
                UploadFrameToGpu(_publishedPixels, _publishedWidth, _publishedHeight);
                Texture.Value = _gpuTexture;
                Resolution.Value = new Int2(_publishedWidth, _publishedHeight);
                UpdateCount.Value++;
            }
        }

        Status.Value = _lastStatusMessage;
    }

    private bool IsCaptureThreadRunning() => _captureThread is { IsAlive: true };

    private void StartCaptureThread(StreamSettings settings)
    {
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
        if (_captureThread == null)
            return;

        _cancellationTokenSource?.Cancel();
        if (!_captureThread.Join(TimeSpan.FromSeconds(2)))
            Log.Debug("Video capture thread did not exit in time.", this);

        _captureThread = null;
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        SetStatus("Stopped");
    }

    /** Keeps trying until cancelled, so a camera that is busy or unplugged resumes on its own. */
    private void CaptureLoop(StreamSettings settings, CancellationToken token)
    {
        try
        {
            var provider = Cameras.Provider;
            if (provider == null)
            {
                SetStatus("Error: Cameras are not available in this application.");
                return;
            }

            while (!token.IsCancellationRequested)
            {
                using (var stream = provider.TryOpen(settings.DeviceName, settings.Mode, out var error))
                {
                    if (stream == null)
                    {
                        SetStatus($"Error: {error}");
                    }
                    else
                    {
                        SetStatus("Waiting for first frame...");
                        ReadFramesUntilStreamEnds(stream, token);
                        if (stream.State == CameraStreamState.Denied)
                        {
                            SetStatus("Error: Access to the camera was denied.");
                            return;
                        }

                        if (stream.State == CameraStreamState.Disconnected)
                            SetStatus("Error: Camera disconnected.");
                    }
                }

                token.WaitHandle.WaitOne(ReopenIntervalMs);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Video capture thread failed: {e.Message}", this);
            SetStatus($"Error: Capture failed - {e.Message}");
        }
    }

    private void ReadFramesUntilStreamEnds(ICameraStream stream, CancellationToken token)
    {
        var capturedPixels = Array.Empty<byte>();
        var scratchPixels = Array.Empty<byte>();
        var lastReportedSize = new Int2(0, 0);

        while (!token.IsCancellationRequested && stream.State == CameraStreamState.Open)
        {
            if (!stream.TryReadFrame(ref capturedPixels, out var width, out var height))
            {
                Thread.Sleep(FramePollIntervalMs);
                continue;
            }

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

    private void SetStatus(string message) => _lastStatusMessage = message;

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        StopCaptureThread();
        GraphicsUtilities.Dispose(ref _gpuTexture);
    }

    #region Dropdown UI
    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        if (inputId == InputDeviceName.Id)
            return InputDeviceName.Value;

        if (inputId == CustomResolution.Id)
            return $"{CustomResolution.Value.X}x{CustomResolution.Value.Y}";

        if (inputId == CustomFps.Id)
            return CustomFps.Value.ToString();

        return string.Empty;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        var provider = Cameras.Provider;
        provider?.EnsureStarted();
        var cameras = provider?.GetCameras() ?? [];

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

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (selected == null)
            return;

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
    private const string NoDevicesOption = "No devices found";

    private readonly Lock _frameLock = new();

    private CancellationTokenSource? _cancellationTokenSource;
    private Thread? _captureThread;
    private StreamSettings _activeStreamSettings;
    private volatile string _lastStatusMessage = string.Empty;
    private Texture2D? _gpuTexture;

    private ImageTransform _imageTransform;
    private byte[] _publishedPixels = [];
    private int _publishedWidth;
    private int _publishedHeight;
    private bool _hasNewFrame;
}
