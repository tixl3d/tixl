using System.Threading;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using T3.Core.Logging;
using T3.Core.Resource;
using CoreTexture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Video;

/// <summary>
/// Drives playback of one video for one operator instance. Decoding and YUV→RGBA conversion run on a
/// dedicated worker thread so they never block the render thread; the render thread only uploads the most
/// recent converted frame into the output <see cref="CoreTexture2D"/> (the D3D immediate context must stay
/// on the render thread). The last-valid texture is retained, so a not-yet-ready frame never blanks output.
///
/// The worker decodes toward the latest requested time (stale requests are discarded), advancing
/// sequentially when the target is just ahead and exact-seeking on a discontinuity. Forward playback and
/// export therefore stay on the fast sequential path.
/// </summary>
public sealed class VideoPlaybackController : IDisposable
{
    public CoreTexture2D? Texture { get; private set; }
    public float Duration { get; private set; }
    public bool HasCompleted { get; private set; }

    /// <summary>False until the requested frame is on screen (drives export-gated <c>Playback.OpNotReady</c>).</summary>
    public bool IsReady { get; private set; }

    /// <summary>Non-null when the file can't be opened or FFmpeg is unavailable.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Posts the requested time to the decode worker and uploads the latest ready frame. Returns true when a
    /// new frame was uploaded this call. Runs on the render thread; never blocks on decoding.
    /// </summary>
    public bool Update(string absolutePath, double requestedSeconds, bool loop, bool renderingToFile)
    {
        EnsureWorkerStarted();

        lock (_lock)
        {
            _requestedUrl = absolutePath;
            _requestedSeconds = requestedSeconds;
            _requestedLoop = loop;
        }

        _wake.Set();

        // Export is not real-time and must be frame-exact, so block until the worker has produced the
        // requested frame. (Realtime playback stays asynchronous and shows the last-valid texture meanwhile.)
        if (renderingToFile)
            WaitForRequestedFrame(requestedSeconds, loop);

        var produced = false;
        double duration;
        bool isOpen;
        long readyTarget;
        lock (_lock)
        {
            if (_hasPendingFrame)
            {
                UploadPendingFrame();
                _lastUploadedTarget = _pendingTarget;
                _hasPendingFrame = false;
                produced = true;
            }

            duration = _duration;
            isOpen = _isOpen;
            ErrorMessage = _errorMessage;

            readyTarget = isOpen && _timeBaseDen > 0
                              ? TimeToFrameMapper.SecondsToPts(
                                  TimeToFrameMapper.ResolvePlaybackSeconds(requestedSeconds, duration, loop),
                                  _streamStartPts, _timeBaseNum, _timeBaseDen)
                              : 0;
        }

        Duration = (float)duration;
        HasCompleted = isOpen && !loop && requestedSeconds >= duration - FrameEpsilonSeconds;
        IsReady = Texture != null && isOpen && _lastUploadedTarget == readyTarget;
        return produced;
    }

    // Blocks the render thread until the worker has decoded the requested frame (export only). Bounded by a
    // timeout so a decode failure can't freeze the export — it just yields and the exporter retries.
    private void WaitForRequestedFrame(double requestedSeconds, bool loop)
    {
        var deadline = Environment.TickCount64 + ExportFrameTimeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            lock (_lock)
            {
                if (_errorMessage != null)
                    return;

                if (_isOpen && _timeBaseDen > 0)
                {
                    var target = TimeToFrameMapper.SecondsToPts(
                        TimeToFrameMapper.ResolvePlaybackSeconds(requestedSeconds, _duration, loop),
                        _streamStartPts, _timeBaseNum, _timeBaseDen);

                    if (_lastUploadedTarget == target || (_hasPendingFrame && _pendingTarget == target))
                        return;
                }
            }

            _framePublished.Wait(20);
            _framePublished.Reset();
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _wake.Set();
        _worker?.Join(TimeSpan.FromSeconds(2));

        // The worker has exited, so its resources can be released without racing it.
        _converter?.Dispose();
        _session?.Dispose();
        Texture?.Dispose();
        Texture = null;
        _wake.Dispose();
        _framePublished.Dispose();
        _cancellation.Dispose();
    }

    // ---- render thread ----

    private void EnsureWorkerStarted()
    {
        if (_worker != null)
            return;

        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "FFmpeg decode" };
        _worker.Start();
    }

    private unsafe void UploadPendingFrame()
    {
        var format = _pendingIsHdr ? Format.R16G16B16A16_UNorm : Format.R8G8B8A8_UNorm;
        var bytesPerPixel = _pendingIsHdr ? 8 : 4;

        if (Texture == null || Texture.Description.Width != _pendingWidth || Texture.Description.Height != _pendingHeight
            || Texture.Description.Format != format)
        {
            Texture?.Dispose();
            Texture = CoreTexture2D.CreateTexture2D(new Texture2DDescription
                                                        {
                                                            Width = _pendingWidth,
                                                            Height = _pendingHeight,
                                                            ArraySize = 1,
                                                            MipLevels = 1,
                                                            Format = format,
                                                            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget | BindFlags.UnorderedAccess,
                                                            CpuAccessFlags = CpuAccessFlags.None,
                                                            OptionFlags = ResourceOptionFlags.None,
                                                            Usage = ResourceUsage.Default,
                                                            SampleDescription = new SampleDescription(1, 0),
                                                        });
        }

        fixed (byte* pixels = _pendingBuffer)
        {
            var dataBox = new SharpDX.DataBox((IntPtr)pixels, _pendingWidth * bytesPerPixel, 0);
            ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, Texture, 0);
        }
    }

    // ---- worker thread ----

    private void WorkerLoop()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            _wake.WaitOne();
            if (_cancellation.IsCancellationRequested)
                break;

            try
            {
                ProcessLatestRequest();
            }
            catch (Exception e)
            {
                // An unhandled exception on a background thread would terminate the editor process, so a
                // transient decode/convert failure is contained and surfaced as an operator error instead.
                lock (_lock)
                {
                    _errorMessage = "Video decoding failed: " + e.Message;
                }

                Log.Warning("FFmpeg decode worker error: " + e);
            }
        }
    }

    private void ProcessLatestRequest()
    {
        string? url;
        double seconds;
        bool loop;
        lock (_lock)
        {
            url = _requestedUrl;
            seconds = _requestedSeconds;
            loop = _requestedLoop;
        }

        if (url == null)
            return;

        if (url != _workerUrl)
            OpenSource(url);

        if (_session == null)
            return;

        var playSeconds = TimeToFrameMapper.ResolvePlaybackSeconds(seconds, _session.DurationSeconds, loop);
        var target = TimeToFrameMapper.SecondsToPts(playSeconds, _session.StreamStartPts,
                                                    _session.TimeBaseNum, _session.TimeBaseDen);

        if (target == _workerLastTarget)
            return;

        if (!DecodeTo(target))
            return;

        PublishFrame(target);
        _workerLastTarget = target;
    }

    private void OpenSource(string url)
    {
        _workerUrl = url;
        _converter?.Dispose();
        _converter = null;
        _session?.Dispose();
        _session = null;
        _workerLastTarget = NotSet;
        _workerLastDecodedPts = NotSet;

        var session = VideoDecoderSession.TryOpen(url, out var error);
        lock (_lock)
        {
            _errorMessage = error;
            _isOpen = session != null;
            _duration = session?.DurationSeconds ?? 0;
            _timeBaseNum = session?.TimeBaseNum ?? 0;
            _timeBaseDen = session?.TimeBaseDen ?? 0;
            _streamStartPts = session?.StreamStartPts ?? 0;
        }

        if (session == null)
            return;

        _session = session;
        _converter = new SoftwareFrameConverter(session.IsHdr);
        // Treat up to ~0.5 s ahead as sequential playback; larger jumps seek.
        _workerSequentialThreshold = session.TimeBaseDen / (2L * Math.Max(1, session.TimeBaseNum));
    }

    private bool DecodeTo(long target)
    {
        var delta = target - _workerLastDecodedPts;
        var advancingSequentially = _workerLastDecodedPts != NotSet && delta > 0 && delta <= _workerSequentialThreshold;

        long decodedPts;
        if (advancingSequentially)
        {
            decodedPts = _workerLastDecodedPts;
            var reached = false;
            while (_session!.TryReadNextFrame(out var pts))
            {
                decodedPts = pts;
                if (pts >= target)
                {
                    reached = true;
                    break;
                }
            }

            if (!reached)
                return false;
        }
        else if (!_session!.SeekAndDecodeTo(target, out decodedPts))
        {
            return false;
        }

        _workerLastDecodedPts = decodedPts;
        return true;
    }

    private void PublishFrame(long target)
    {
        var rgba = _converter!.Convert(_session!.CurrentFrame);
        var byteCount = rgba.Width * rgba.Height * _converter.BytesPerPixel;

        lock (_lock)
        {
            if (_pendingBuffer == null || _pendingBuffer.Length < byteCount)
                _pendingBuffer = new byte[byteCount];

            rgba.FillImageBuffer(_pendingBuffer, 1);
            _pendingWidth = rgba.Width;
            _pendingHeight = rgba.Height;
            _pendingIsHdr = _session.IsHdr;
            _pendingTarget = target;
            _hasPendingFrame = true;
        }

        _framePublished.Set();
    }

    private const long NotSet = long.MinValue;
    private const double FrameEpsilonSeconds = 1.0 / 1000.0;
    private const int ExportFrameTimeoutMs = 5000;

    private readonly object _lock = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly ManualResetEventSlim _framePublished = new(false);
    private readonly CancellationTokenSource _cancellation = new();
    private Thread? _worker;

    // Request (render thread → worker), guarded by _lock.
    private string? _requestedUrl;
    private double _requestedSeconds;
    private bool _requestedLoop;

    // Source metadata (worker → render thread), guarded by _lock.
    private bool _isOpen;
    private string? _errorMessage;
    private double _duration;
    private int _timeBaseNum;
    private int _timeBaseDen;
    private long _streamStartPts;

    // Converted-frame handoff (worker → render thread), guarded by _lock.
    private byte[]? _pendingBuffer;
    private int _pendingWidth;
    private int _pendingHeight;
    private bool _pendingIsHdr;
    private long _pendingTarget;
    private bool _hasPendingFrame;

    // Render-thread only.
    private long _lastUploadedTarget = NotSet;

    // Worker-thread only.
    private VideoDecoderSession? _session;
    private SoftwareFrameConverter? _converter;
    private string? _workerUrl;
    private long _workerLastTarget = NotSet;
    private long _workerLastDecodedPts = NotSet;
    private long _workerSequentialThreshold = 1;
}
