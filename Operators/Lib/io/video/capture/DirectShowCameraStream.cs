#nullable enable
using DirectShowLib;
using T3.SystemUi;

namespace Lib.io.video;

/// <summary>
/// Captures a camera through a DirectShow graph (source → sample grabber → null renderer). Used only for
/// devices SDL cannot see because they exist only as DirectShow filters: OBS Virtual Camera, Blackmagic
/// DeckLink, NDI Webcam Input and many older capture cards. The grabber asks for RGB32, so Windows inserts
/// whatever decoder the device needs (MJPEG, YUY2, NV12...).
/// <para>Create and dispose on one thread; frames arrive on DirectShow's streaming thread.</para>
/// </summary>
internal sealed class DirectShowCameraStream : ICameraStream, ISampleGrabberCB
{
    public static DirectShowCameraStream? TryOpen(DsDevice device, CameraMode requestedMode, out string? error)
    {
        var stream = new DirectShowCameraStream();
        try
        {
            if (stream.Build(device, requestedMode, out error))
                return stream;
        }
        catch (Exception e)
        {
            error = $"DirectShow could not open '{device.Name}': {e.Message}";
        }

        stream.Dispose();
        return null;
    }

    public CameraStreamState State
    {
        get
        {
            PollGraphEvents();
            return _state;
        }
    }

    public bool TryReadFrame(ref byte[] rgbaPixels, out int width, out int height)
    {
        lock (_frameLock)
        {
            width = _width;
            height = _height;
            if (!_hasNewFrame)
                return false;

            var byteCount = _width * _height * 4;
            if (rgbaPixels.Length < byteCount)
                rgbaPixels = new byte[byteCount];

            System.Buffer.BlockCopy(_latestFrame, 0, rgbaPixels, 0, byteCount);
            _hasNewFrame = false;
            return true;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        try
        {
            _mediaControl?.Stop();
            _grabber?.SetCallback(null, 1);
        }
        catch (Exception e)
        {
            Log.Debug($"DirectShow capture: stopping the graph failed: {e.Message}");
        }

        // Draining the frame lock waits for a BufferCB that is already running. The grabber's buffer is
        // owned by the filter, so releasing the filters while that copy is in flight frees memory the
        // streaming thread is still reading — one of the ways an unplugged camera used to crash the editor.
        lock (_frameLock)
        {
            _hasNewFrame = false;
        }

        Release(ref _mediaControl);
        Release(ref _mediaEvent);
        Release(ref _nullRenderer);
        Release(ref _grabber);
        Release(ref _source);
        Release(ref _builder);
        Release(ref _graph);
        _state = CameraStreamState.Disconnected;
    }

    /// <summary>Native capture modes of a DirectShow device (binds it briefly; call off the main thread).</summary>
    public static List<CameraMode> ReadModes(DsDevice device)
    {
        var modes = new List<CameraMode>();
        object? sourceObject = null;
        try
        {
            var filterId = typeof(IBaseFilter).GUID;
            device.Mon.BindToObject(null!, null, ref filterId, out sourceObject);
            if (sourceObject is not IBaseFilter source)
                return modes;

            var pin = DsFindPin.ByCategory(source, PinCategory.Capture, 0);
            if (pin is IAMStreamConfig config)
                ForEachCapability(config, (_, width, height, fps) => modes.Add(new CameraMode(width, height, fps)));

            if (pin != null)
                Marshal.ReleaseComObject(pin);
        }
        catch (Exception e)
        {
            Log.Debug($"DirectShow capture: reading the modes of '{device.Name}' failed: {e.Message}");
        }
        finally
        {
            if (sourceObject != null)
                Marshal.ReleaseComObject(sourceObject);
        }

        return modes;
    }

    private bool Build(DsDevice device, CameraMode requestedMode, out string? error)
    {
        error = null;
        _graph = (IFilterGraph2)new FilterGraph();
        _builder = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();
        Check(_builder.SetFiltergraph(_graph), "attach the graph builder");

        Check(_graph.AddSourceFilterForMoniker(device.Mon, null, device.Name, out var source), "add the camera");
        _source = source;

        if (requestedMode.Width > 0 || requestedMode.Fps > 0)
            TrySelectFormat(requestedMode);

        _grabber = (ISampleGrabber)new SampleGrabber();
        var wantedType = new AMMediaType
                             {
                                 majorType = MediaType.Video,
                                 subType = MediaSubType.RGB32,
                                 formatType = FormatType.VideoInfo,
                             };
        Check(_grabber.SetMediaType(wantedType), "request RGB32 frames");
        DsUtils.FreeAMMediaType(wantedType);
        Check(_graph.AddFilter((IBaseFilter)_grabber, "Sample Grabber"), "add the sample grabber");

        _nullRenderer = (IBaseFilter)new NullRenderer();
        Check(_graph.AddFilter(_nullRenderer, "Null Renderer"), "add the null renderer");

        // Some sources only have a preview pin or an uncategorized output.
        var hr = _builder.RenderStream(PinCategory.Capture, MediaType.Video, _source, (IBaseFilter)_grabber, _nullRenderer);
        if (hr < 0)
            hr = _builder.RenderStream(PinCategory.Preview, MediaType.Video, _source, (IBaseFilter)_grabber, _nullRenderer);
        if (hr < 0)
            hr = _builder.RenderStream(null, MediaType.Video, _source, (IBaseFilter)_grabber, _nullRenderer);
        Check(hr, "connect the camera to an RGB32 output");

        var connectedType = new AMMediaType();
        Check(_grabber.GetConnectedMediaType(connectedType), "read the negotiated format");
        try
        {
            var header = Marshal.PtrToStructure<VideoInfoHeader>(connectedType.formatPtr)!;
            _width = header.BmiHeader.Width;
            _height = Math.Abs(header.BmiHeader.Height);
            _isBottomUp = header.BmiHeader.Height > 0;
        }
        finally
        {
            DsUtils.FreeAMMediaType(connectedType);
        }

        if (_width <= 0 || _height <= 0)
        {
            error = $"'{device.Name}' negotiated an empty frame size.";
            return false;
        }

        _latestFrame = new byte[_width * _height * 4];
        Check(_grabber.SetBufferSamples(false), "configure the sample grabber");
        Check(_grabber.SetOneShot(false), "configure the sample grabber");
        Check(_grabber.SetCallback(this, 1), "register the frame callback");

        _mediaEvent = (IMediaEventEx)_graph;
        _mediaControl = (IMediaControl)_graph;
        Check(_mediaControl.Run(), "start the capture");
        _state = CameraStreamState.Open;
        return true;
    }

    /** Picks the native mode closest to the request; the device default stays if nothing fits. */
    private void TrySelectFormat(CameraMode requested)
    {
        var hr = _builder!.FindInterface(PinCategory.Capture, MediaType.Video, _source, typeof(IAMStreamConfig).GUID, out var configObject);
        if (hr < 0 || configObject is not IAMStreamConfig config)
            return;

        var bestIndex = -1;
        var bestScore = long.MaxValue;
        ForEachCapability(config, (index, width, height, fps) =>
                                  {
                                      long score = 0;
                                      if (requested.Width > 0)
                                          score += Math.Abs(width - requested.Width) + Math.Abs(height - requested.Height);
                                      if (requested.Fps > 0)
                                          score = score * 1000 + Math.Abs(fps - requested.Fps);
                                      if (score < bestScore)
                                      {
                                          bestScore = score;
                                          bestIndex = index;
                                      }
                                  });

        if (bestIndex < 0)
            return;

        config.GetNumberOfCapabilities(out _, out var capsSize);
        var caps = Marshal.AllocCoTaskMem(capsSize);
        try
        {
            if (config.GetStreamCaps(bestIndex, out var mediaType, caps) < 0)
                return;

            try
            {
                if (requested.Fps > 0 && mediaType.formatType == FormatType.VideoInfo && mediaType.formatPtr != IntPtr.Zero)
                {
                    var header = Marshal.PtrToStructure<VideoInfoHeader>(mediaType.formatPtr)!;
                    header.AvgTimePerFrame = 10_000_000L / requested.Fps;
                    Marshal.StructureToPtr(header, mediaType.formatPtr, false);
                }

                hr = config.SetFormat(mediaType);
                if (hr < 0)
                    Log.Debug($"DirectShow capture: the device refused the requested format (0x{hr:X8}).");
            }
            finally
            {
                DsUtils.FreeAMMediaType(mediaType);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(caps);
        }
    }

    private static void ForEachCapability(IAMStreamConfig config, Action<int, int, int, int> visit)
    {
        if (config.GetNumberOfCapabilities(out var count, out var capsSize) < 0)
            return;

        var caps = Marshal.AllocCoTaskMem(capsSize);
        try
        {
            for (var index = 0; index < count; index++)
            {
                if (config.GetStreamCaps(index, out var mediaType, caps) < 0)
                    continue;

                try
                {
                    if (mediaType.formatPtr == IntPtr.Zero)
                        continue;

                    int width, height;
                    long frameTime;
                    if (mediaType.formatType == FormatType.VideoInfo)
                    {
                        var header = Marshal.PtrToStructure<VideoInfoHeader>(mediaType.formatPtr)!;
                        (width, height, frameTime) = (header.BmiHeader.Width, Math.Abs(header.BmiHeader.Height), header.AvgTimePerFrame);
                    }
                    else if (mediaType.formatType == FormatType.VideoInfo2)
                    {
                        var header = Marshal.PtrToStructure<VideoInfoHeader2>(mediaType.formatPtr)!;
                        (width, height, frameTime) = (header.BmiHeader.Width, Math.Abs(header.BmiHeader.Height), header.AvgTimePerFrame);
                    }
                    else
                    {
                        continue;
                    }

                    var fps = frameTime > 0 ? (int)Math.Round(10_000_000.0 / frameTime) : 0;
                    if (width > 0 && height > 0)
                        visit(index, width, height, fps);
                }
                finally
                {
                    DsUtils.FreeAMMediaType(mediaType);
                }
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(caps);
        }
    }

    /** DirectShow's streaming thread. Converts bottom-up BGRA into top-down RGBA with opaque alpha. */
    int ISampleGrabberCB.BufferCB(double sampleTime, IntPtr buffer, int bufferLength)
    {
        var rowBytes = _width * 4;
        if (buffer == IntPtr.Zero || bufferLength < rowBytes * _height)
            return 0;

        lock (_frameLock)
        {
            // Dispose() clears the callback, but one invocation may already have been dispatched.
            if (_isDisposed)
                return 0;

            unsafe
            {
                var source = (byte*)buffer;
                fixed (byte* target = _latestFrame)
                {
                    for (var row = 0; row < _height; row++)
                    {
                        var sourceRow = source + (long)(_isBottomUp ? _height - 1 - row : row) * rowBytes;
                        var targetRow = target + (long)row * rowBytes;
                        for (var x = 0; x < rowBytes; x += 4)
                        {
                            targetRow[x] = sourceRow[x + 2];
                            targetRow[x + 1] = sourceRow[x + 1];
                            targetRow[x + 2] = sourceRow[x];
                            targetRow[x + 3] = 255;
                        }
                    }
                }
            }

            _hasNewFrame = true;
        }

        return 0;
    }

    int ISampleGrabberCB.SampleCB(double sampleTime, IMediaSample sample) => 0;

    /** Turns a lost device or an aborted graph into <see cref="CameraStreamState.Disconnected"/>. */
    private void PollGraphEvents()
    {
        var mediaEvent = _mediaEvent;
        if (mediaEvent == null || _state != CameraStreamState.Open)
            return;

        try
        {
            while (mediaEvent.GetEvent(out var code, out var param1, out var param2, 0) == 0)
            {
                mediaEvent.FreeEventParams(code, param1, param2);
                if (code is EventCode.DeviceLost or EventCode.ErrorAbort or EventCode.ErrorAbortEx)
                    _state = CameraStreamState.Disconnected;
            }
        }
        catch (Exception)
        {
            _state = CameraStreamState.Disconnected;
        }
    }

    private static void Check(int hr, string step)
    {
        if (hr < 0)
            throw new InvalidOperationException($"could not {step} (0x{hr:X8})");
    }

    private static void Release<T>(ref T? comObject) where T : class
    {
        if (comObject != null && Marshal.IsComObject(comObject))
        {
            try
            {
                Marshal.ReleaseComObject(comObject);
            }
            catch (Exception)
            {
                // Already released by the graph.
            }
        }

        comObject = null;
    }

    private DirectShowCameraStream()
    {
    }

    private readonly object _frameLock = new();
    private byte[] _latestFrame = [];
    private bool _hasNewFrame;
    private int _width;
    private int _height;
    private bool _isBottomUp;
    private volatile CameraStreamState _state = CameraStreamState.Disconnected;
    private volatile bool _isDisposed;

    private IFilterGraph2? _graph;
    private ICaptureGraphBuilder2? _builder;
    private IBaseFilter? _source;
    private ISampleGrabber? _grabber;
    private IBaseFilter? _nullRenderer;
    private IMediaControl? _mediaControl;
    private IMediaEventEx? _mediaEvent;
}
