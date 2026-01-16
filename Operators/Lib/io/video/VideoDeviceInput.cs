using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using DirectShowLib;
using OpenCvSharp;
using SharpDX;
using Device = SharpDX.Direct3D11.Device;
using FormatType = DirectShowLib.FormatType;

namespace Lib.io.video;

[Guid("cd5a182e-254b-4e65-820b-ff754122614c")]
public class VideoDeviceInput : Instance<VideoDeviceInput>, ICustomDropdownHolder
{
    // Output slots
    [Output(Guid = "1d0159cc-33d2-46b1-9c0c-7054aa560df5", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> Texture = new();
    
    [Output(Guid = "9C2E4C11-09B6-4F4D-8F99-4A7372D5F2B5", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Int2> Resolution = new();
    
    [Output(Guid = "A1B2C3D4-5678-90EF-1234-567890ABCDEF", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public new readonly Slot<string> Status = new();
    
    [Output(Guid = "868D5FFE-032C-4522-B56B-D96B30841DB7", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> UpdateCount = new();    
    
    private const float Epsilon = 1e-4f;

    // Static device information
    private static List<WebcamWithIndex> _webcamWithIndices;
    private static Dictionary<string, List<(int Width, int Height, double Fps)>> _webcamCapabilities;
    private static readonly object _capabilitiesLock = new();
    private static readonly Lock _staticInitLock = new();
    private static bool _devicesScanned;
    private readonly Lock _captureDeviceLock = new();
    private readonly Lock _lockObject = new();

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



    private CancellationTokenSource _cancellationTokenSource;

    // Instance variables
    private VideoCapture _capture;
    private Thread _captureThread;
    private Texture2D _gpuTexture;
    private int _height;
    private volatile string _lastStatusMessage = "";
    private Mat _sharedMat;
    private int _storeDeviceIndex = -1;

    // Transformation cache
    private Mat _transformationMatrix;
    private int _width;

    public VideoDeviceInput()
    {
        Texture.UpdateAction = Update;
        UpdateCount.UpdateAction = Update;
        Resolution.UpdateAction = Update;
        Status.UpdateAction = Update;
    }

    private void SetStatus(string message)
    {
        _lastStatusMessage = message;
    }

    private void Update(EvaluationContext context)
    {
        ScanWebCamDevices();

        var deviceName = InputDeviceName.GetValue(context);
        if (string.IsNullOrEmpty(deviceName) && _webcamWithIndices?.Count > 0)
        {
            deviceName = _webcamWithIndices[0].Name;
        }

        if (!TryGetIndexForDeviceName(deviceName, out var selectedDeviceIndex))
        {
            SetStatus($"Error: Camera '{deviceName}' not found.");
            StopCaptureThread();
        }
        else
        {
            var deviceIsActive = Active.GetValue(context);
            var deactivateWhenNotShowing = DeactivateWhenNotShowing.GetValue(context);

            bool shouldBeCapturing = deviceIsActive && (!deactivateWhenNotShowing || IsActiveInGraph());

            bool reconnectTriggered = Reconnect.GetValue(context);
            if (reconnectTriggered)
            {
                Reconnect.SetTypedInputValue(false);
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
                    StartCaptureThread(context);
                }
            }
            else
            {
                StopCaptureThread();
            }
        }

        if (OpenSettings.GetValue(context))
        {
            OpenVideoSettings();
            OpenSettings.SetTypedInputValue(false);
        }

        lock (_lockObject)
        {
            if (_sharedMat != null && !_sharedMat.Empty())
            {
                UploadMat(ResourceManager.Device, _sharedMat);
                Texture.Value = _gpuTexture;
                UpdateCount.Value++;
                Resolution.Value = new Int2(_sharedMat.Width, _sharedMat.Height);
            }
        }

        Status.Value = _lastStatusMessage;
    }

    private bool IsCaptureThreadRunning()
    {
        return _captureThread != null && _captureThread.IsAlive;
    }

    private bool IsActiveInGraph() => true;

    private void StartCaptureThread(EvaluationContext context)
    {
        if (IsCaptureThreadRunning()) return;

        var settings = new CaptureThreadSettings(this, context);
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
        if (!IsCaptureThreadRunning()) return;

        _cancellationTokenSource?.Cancel();

        if (!_captureThread.Join(TimeSpan.FromSeconds(2)))
        {
            Log.Debug("Video capture thread did not exit in time.");
        }

        _captureThread = null;
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        lock (_captureDeviceLock)
        {
            _capture?.Dispose();
            _capture = null;
        }
    }

    private void CaptureLoop(CaptureThreadSettings settings, CancellationToken token)
    {
        try
        {
            lock (_captureDeviceLock)
            {
                if (token.IsCancellationRequested) return;

                _capture = new VideoCapture();

                if (settings.ResolutionFpsType == ResolutionFpsTypeEnum.Custom)
                {
                    _capture.Set(VideoCaptureProperties.FrameWidth, settings.CustomResolution.X);
                    _capture.Set(VideoCaptureProperties.FrameHeight, settings.CustomResolution.Y);
                    _capture.Set(VideoCaptureProperties.Fps, settings.CustomFps);
                }

                if (!_capture.Open(_storeDeviceIndex, VideoCaptureAPIs.DSHOW))
                {
                    SetStatus("Error: Failed to open video capture device.");
                    return;
                }
            }

            Thread.Sleep(100);

            lock (_captureDeviceLock)
            {
                if (token.IsCancellationRequested || _capture == null) return;

                if (settings.ResolutionFpsType == ResolutionFpsTypeEnum.Custom)
                {
                    bool widthSet = _capture.Set(VideoCaptureProperties.FrameWidth, settings.CustomResolution.X);
                    bool heightSet = _capture.Set(VideoCaptureProperties.FrameHeight, settings.CustomResolution.Y);
                    bool fpsSet = _capture.Set(VideoCaptureProperties.Fps, settings.CustomFps);

                    if (!widthSet || !heightSet || !fpsSet)
                    {
                        Log.Debug("Set returned false—camera doesn't support requested settings.");
                    }

                    var actualWidth = (int)_capture.Get(VideoCaptureProperties.FrameWidth);
                    var actualHeight = (int)_capture.Get(VideoCaptureProperties.FrameHeight);
                    var actualFps = _capture.Get(VideoCaptureProperties.Fps);

                    if (actualWidth != settings.CustomResolution.X || actualHeight != settings.CustomResolution.Y ||
                        Math.Abs(actualFps - settings.CustomFps) > 0.01)
                    {
                        SetStatus($"Warning: Using {actualWidth}x{actualHeight}@{actualFps:F2}fps (requested {settings.CustomResolution.X}x{settings.CustomResolution.Y}@{settings.CustomFps}fps).");
                    }
                    else
                    {
                        SetStatus($"Success: Running at {settings.CustomResolution.X}x{settings.CustomResolution.Y}@{settings.CustomFps}fps.");
                    }
                }
                else
                {
                    SetStatus("Running at device default settings.");
                }
            }

            using var frame = new Mat();
            using var bgraMat = new Mat();

            while (!token.IsCancellationRequested)
            {
                Mat currentFrame = null;
                bool frameReadSuccess = false;

                lock (_captureDeviceLock)
                {
                    if (_capture != null && _capture.IsOpened())
                    {
                        frameReadSuccess = _capture.Read(frame);
                    }
                }

                if (frameReadSuccess && !frame.Empty())
                {
                    SetStatus("Running");
                    Cv2.CvtColor(frame, bgraMat, ColorConversionCodes.BGR2BGRA);

                    if (settings.FlipVertically)
                        Cv2.Flip(bgraMat, bgraMat, FlipMode.Y);

                    if (settings.FlipHorizontally)
                        Cv2.Flip(bgraMat, bgraMat, FlipMode.X);

                    if (settings.ResolutionFpsType == ResolutionFpsTypeEnum.Custom)
                    {
                        var targetSize = new Size(settings.CustomResolution.X, settings.CustomResolution.Y);
                        if (bgraMat.Size() != targetSize)
                        {
                            using var resized = new Mat();
                            Cv2.Resize(bgraMat, resized, targetSize, 0, 0, InterpolationFlags.Cubic);
                            currentFrame = resized.Clone();
                            SetStatus(_lastStatusMessage + " (Software resize)");
                        }
                    }

                    currentFrame ??= bgraMat;

                    if (settings.HasTransformation())
                    {
                        UpdateTransformationMatrix(settings, currentFrame.Size());
                        var newSize = new Size(currentFrame.Width, currentFrame.Height);

                        lock (_lockObject)
                        {
                            if (_sharedMat == null || _sharedMat.Width != newSize.Width || _sharedMat.Height != newSize.Height)
                            {
                                _sharedMat?.Dispose();
                                _sharedMat = new Mat(newSize, MatType.CV_8UC4);
                            }

                            Cv2.WarpAffine(currentFrame, _sharedMat, _transformationMatrix, newSize);
                        }
                    }
                    else
                    {
                        lock (_lockObject)
                        {
                            if (_sharedMat == null || _sharedMat.Width != currentFrame.Width || _sharedMat.Height != currentFrame.Height)
                            {
                                _sharedMat?.Dispose();
                                _sharedMat = new Mat(currentFrame.Height, currentFrame.Width, MatType.CV_8UC4);
                            }

                            currentFrame.CopyTo(_sharedMat);
                        }
                    }

                    if (currentFrame != bgraMat)
                    {
                        currentFrame.Dispose();
                    }
                }
                else
                {
                    SetStatus("Error: Failed to retrieve frame.");
                    Thread.Sleep(100);
                }
            }
        }
        catch (Exception e)
        {
            Log.Debug($"Video capture thread failed: {e.Message}\n{e.StackTrace}");
            SetStatus($"Error: Capture failed - {e.Message}");
        }
        finally
        {
            lock (_captureDeviceLock)
            {
                _capture?.Dispose();
                _capture = null;
            }

            _transformationMatrix?.Dispose();
        }
    }

    private void UpdateTransformationMatrix(CaptureThreadSettings settings, Size frameSize)
    {
        _transformationMatrix?.Dispose();
        _transformationMatrix = new Mat(2, 3, MatType.CV_32FC1);

        var center = new Point2f(frameSize.Width / 2.0f, frameSize.Height / 2.0f);
        var angleRad = settings.Rotation * Math.PI / 180.0;
        var cos = (float)Math.Cos(angleRad);
        var sin = (float)Math.Sin(angleRad);

        _transformationMatrix.Set(0, 0, cos * settings.Scale.X);
        _transformationMatrix.Set(0, 1, sin * settings.Scale.Y);
        _transformationMatrix.Set(1, 0, -sin * settings.Scale.X);
        _transformationMatrix.Set(1, 1, cos * settings.Scale.Y);

        var tx = (1.0f - cos * settings.Scale.X) * center.X - sin * settings.Scale.Y * center.Y;
        var ty = sin * settings.Scale.X * center.X + (1.0f - cos * settings.Scale.Y) * center.Y;

        _transformationMatrix.Set(0, 2, settings.Reposition.X + tx);
        _transformationMatrix.Set(1, 2, settings.Reposition.Y + ty);
    }

    private void UploadMat(Device device, Mat mat)
    {
        if (mat == null || mat.Empty())
        {
            Log.Debug("UploadMat: Mat is null or empty. Skipping upload.");
            return;
        }

        var width = mat.Width;
        var height = mat.Height;

        if (_gpuTexture == null || _width != width || _height != height)
        {
            _gpuTexture?.Dispose();
            _width = width;
            _height = height;

            var texDesc = new Texture2DDescription
                              {
                                  Width = width,
                                  Height = height,
                                  MipLevels = 1,
                                  ArraySize = 1,
                                  Format = Format.B8G8R8A8_UNorm,
                                  SampleDescription = new SampleDescription(1, 0),
                                  Usage = ResourceUsage.Default,
                                  BindFlags = BindFlags.ShaderResource,
                                  CpuAccessFlags = CpuAccessFlags.None,
                                  OptionFlags = ResourceOptionFlags.None
                              };
            var sharpDxTexture = new SharpDX.Direct3D11.Texture2D(device, texDesc);
            _gpuTexture = new Texture2D(sharpDxTexture);
        }

        var dataBox = new DataBox(mat.Data, (int)mat.Step(), 0);
        device.ImmediateContext.UpdateSubresource(dataBox, _gpuTexture);
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        StopCaptureThread();

        _gpuTexture?.Dispose();
        _gpuTexture = null;

        _transformationMatrix?.Dispose();
        _transformationMatrix = null;

        lock (_lockObject)
        {
            _sharedMat?.Dispose();
            _sharedMat = null;
        }
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
    private void OpenVideoSettings()
    {
        try
        {
            lock (_captureDeviceLock)
            {
                if (_capture != null && _capture.IsOpened())
                {
                    _capture.Set(VideoCaptureProperties.Settings, 1);
                }
                else
                {
                    Log.Debug("Cannot open settings, capture device is not available.");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Failed to open video settings: {ex.Message}");
        }
    }

    public void OpenCrossbarSettings()
    {
        Log.Debug("Crossbar settings not implemented for this version.");
    }
    #endregion

    #region Dropdown UI
    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        if (inputId == InputDeviceName.Id) return InputDeviceName.Value;
        if (inputId == CustomResolution.Id) return $"{CustomResolution.Value.X}x{CustomResolution.Value.Y}";
        if (inputId == CustomFps.Id) return CustomFps.Value.ToString();
        return string.Empty;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        if (inputId == InputDeviceName.Id)
        {
            if (_webcamWithIndices == null || _webcamWithIndices.Count == 0)
            {
                yield return "No devices found";
                yield break;
            }

            foreach (var (webcam, _) in _webcamWithIndices)
                yield return webcam;
        }
        else if (inputId == CustomResolution.Id)
        {
            var deviceName = InputDeviceName.Value;
            lock (_capabilitiesLock)
            {
                if (_webcamCapabilities != null && _webcamCapabilities.ContainsKey(deviceName))
                {
                    foreach (var (w, h, _) in _webcamCapabilities[deviceName].DistinctBy(c => (c.Width, c.Height)))
                        yield return $"{w}x{h}";
                }
                else
                    yield return "640x480";
            }
        }
        else if (inputId == CustomFps.Id)
        {
            var deviceName = InputDeviceName.Value;
            lock (_capabilitiesLock)
            {
                if (_webcamCapabilities != null && _webcamCapabilities.ContainsKey(deviceName))
                {
                    foreach (var (_, _, fps) in _webcamCapabilities[deviceName].DistinctBy(c => c.Fps))
                        yield return $"{fps:F0}";
                }
                else
                    yield return "30";
            }
        }
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string selected, bool isAListItem)
    {
        if (inputId == InputDeviceName.Id)
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
                    lock (_capabilitiesLock)
                    {
                        if (_webcamCapabilities.TryGetValue(deviceName, out var caps) &&
                            !caps.Any(c => c.Width == w && c.Height == h))
                        {
                            SetStatus("Warning: Unsupported resolution—may fallback.");
                        }
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
    }
    #endregion

    #region Scanning for webcams and capabilities
    public static void ScanWebCamDevices()
    {
        lock (_staticInitLock)
        {
            if (_devicesScanned)
                return;

            _webcamWithIndices = new List<WebcamWithIndex>();
            lock (_capabilitiesLock)
            {
                _webcamCapabilities = new Dictionary<string, List<(int Width, int Height, double Fps)>>();
            }

            try
            {
                var videoInputDevices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
                for (var i = 0; i < videoInputDevices.Length; i++)
                {
                    var device = videoInputDevices[i];
                    _webcamWithIndices.Add(new WebcamWithIndex(device.Name, i));

                    var capabilities = new List<(int Width, int Height, double Fps)>();
                    try
                    {
                        if (device.Mon is not IMoniker moniker)
                        {
                            Log.Debug($"Moniker is null for device {device.Name}");
                            continue;
                        }

                        var filterGuid = typeof(IBaseFilter).GUID;
                        moniker.BindToObject(null, null, ref filterGuid, out var filter);
                        if (filter is not IBaseFilter captureFilter)
                        {
                            Log.Debug($"Failed to create capture filter for {device.Name}");
                            continue;
                        }

                        captureFilter.EnumPins(out var enumPins);
                        var pins = new IPin[1];
                        while (enumPins.Next(1, pins, IntPtr.Zero) == 0)
                        {
                            pins[0].QueryPinInfo(out var pinInfo);
                            if (pinInfo.dir == PinDirection.Output)
                            {
                                if (pins[0] is IAMStreamConfig streamConfig)
                                {
                                    streamConfig.GetNumberOfCapabilities(out int count, out int size);
                                    if (size > 0)
                                    {
                                        var capsPtr = Marshal.AllocCoTaskMem(size);
                                        try
                                        {
                                            for (int j = 0; j < count; j++)
                                            {
                                                streamConfig.GetStreamCaps(j, out var mediaType, capsPtr);
                                                if (mediaType != null &&
                                                    mediaType.formatType == FormatType.VideoInfo &&
                                                    mediaType.formatPtr != IntPtr.Zero)
                                                {
                                                    var vih = Marshal.PtrToStructure<VideoInfoHeader>(mediaType.formatPtr);
                                                    int w = vih.BmiHeader.Width;
                                                    int h = vih.BmiHeader.Height;
                                                    double fps = vih.AvgTimePerFrame > 0
                                                                     ? 10_000_000.0 / vih.AvgTimePerFrame
                                                                     : 30.0;
                                                    capabilities.Add((w, h, fps));
                                                }

                                                if (mediaType != null)
                                                    DsUtils.FreeAMMediaType(mediaType);
                                            }
                                        }
                                        finally
                                        {
                                            Marshal.FreeCoTaskMem(capsPtr);
                                        }
                                    }
                                }
                            }

                            Marshal.ReleaseComObject(pins[0]);
                        }

                        Marshal.ReleaseComObject(enumPins);
                        Marshal.ReleaseComObject(captureFilter);
                    }
                    catch (Exception e)
                    {
                        Log.Debug($"Failed to get capabilities for {device.Name}: {e.Message}");
                    }

                    lock (_capabilitiesLock)
                    {
                        _webcamCapabilities.Add(device.Name, capabilities);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Debug("Failed to scan webcams: " + e.Message);
            }

            _devicesScanned = true;
        }
    }

    private static bool TryGetIndexForDeviceName(string deviceName, out int index)
    {
        index = -1;
        if (_webcamWithIndices == null) return false;

        var device = _webcamWithIndices.FirstOrDefault(d => d.Name == deviceName);
        if (device != null)
        {
            index = device.Index;
            return true;
        }

        return false;
    }

    public sealed record WebcamWithIndex(string Name, int Index);
    #endregion
}