using System.Threading;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Utils;
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
        var gotGpuFrame = false;
        double duration;
        bool isOpen;
        long readyTarget;
        lock (_lock)
        {
            if (_zeroCopy)
            {
                if (_hasPendingGpuFrame)
                {
                    unsafe { ffmpeg.av_frame_move_ref(_renderGpuFrame, _pendingGpuFrame); }
                    _lastUploadedTarget = _pendingTarget;
                    _hasPendingGpuFrame = false;
                    gotGpuFrame = true;
                }
            }
            else if (_hasPendingFrame)
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
                              ? TimeToFrameMapper.SecondsToFramePts(
                                  TimeToFrameMapper.ResolvePlaybackSeconds(requestedSeconds, duration, loop),
                                  _streamStartPts, _timeBaseNum, _timeBaseDen, _frameRate)
                              : 0;
        }

        if (gotGpuFrame)
        {
            // Convert on the render thread's immediate context (outside the lock). The converter owns the output
            // texture; Texture just points at it.
            _hardwareConverter ??= new HardwareFrameConverter();
            Texture = _hardwareConverter.Convert(_renderGpuFrame, _renderGpuFrame.Width, _renderGpuFrame.Height);
            _renderGpuFrame.Unref();
            produced = true;
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
                    var target = TimeToFrameMapper.SecondsToFramePts(
                        TimeToFrameMapper.ResolvePlaybackSeconds(requestedSeconds, _duration, loop),
                        _streamStartPts, _timeBaseNum, _timeBaseDen, _frameRate);

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
        _hardwareConverter?.Dispose();
        _pendingGpuFrame.Dispose();
        _renderGpuFrame.Dispose();
        _converter?.Dispose();
        _session?.Dispose();
        _cache?.Dispose();
        if (!_zeroCopy)
            Texture?.Dispose(); // in zero-copy the converter owns the output texture, disposed above
        Texture = null;
        _wake.Dispose();
        _framePublished.Dispose();
        _cancellation.Dispose();
    }

    /// <summary>
    /// Assigns this stream's frame-cache budget — the engine's share of the shared global budget. Safe to
    /// call from the engine's eval thread; the worker applies it to its cache on the next request.
    /// </summary>
    public void SetCacheBudget(long bytes) => Volatile.Write(ref _cacheBudget, bytes);

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

        _cache?.SetBudget(Volatile.Read(ref _cacheBudget));

        var playSeconds = TimeToFrameMapper.ResolvePlaybackSeconds(seconds, _session.DurationSeconds, loop);
        var target = TimeToFrameMapper.SecondsToFramePts(playSeconds, _session.StreamStartPts,
                                                         _session.TimeBaseNum, _session.TimeBaseDen, _session.FrameRate);

        if (target == _workerLastTarget)
            return;

        Frame frameToPublish;
        if (_cache != null && _cache.TryGet(target, out var cachedFrame))
        {
            frameToPublish = cachedFrame;
        }
        else
        {
            if (!DecodeTo(target))
                return;

            frameToPublish = _session!.CurrentFrame;
        }

        PublishFrame(frameToPublish, target);
        _workerLastTarget = target;
        PrefetchAhead(target);
    }

    private void OpenSource(string url)
    {
        _workerUrl = url;
        _converter?.Dispose();
        _converter = null;
        _session?.Dispose();
        _session = null;
        _cache?.Dispose();
        _cache = null;
        _workerLastTarget = NotSet;
        _workerLastDecodedPts = NotSet;

        var session = VideoDecoderSession.TryOpen(url, out var error);
        lock (_lock)
        {
            _errorMessage = error;
            _isOpen = session != null;
            _zeroCopy = session?.UsesZeroCopy ?? false;
            _duration = session?.DurationSeconds ?? 0;
            _timeBaseNum = session?.TimeBaseNum ?? 0;
            _timeBaseDen = session?.TimeBaseDen ?? 0;
            _streamStartPts = session?.StreamStartPts ?? 0;
            _frameRate = session?.FrameRate ?? 0;
        }

        if (session == null)
            return;

        _session = session;
        var path = session.UsesZeroCopy ? "D3D11VA hardware (zero-copy)"
                       : session.UsesHardwareDecode ? "D3D11VA hardware (CPU read-back)" : "software";
        Log.Debug($"Video decode path: {path} — {session.Width}x{session.Height} {session.PixelFormat}");

        // Treat up to ~0.5 s ahead as sequential playback; larger jumps seek.
        _workerSequentialThreshold = session.TimeBaseDen / (2L * Math.Max(1, session.TimeBaseNum));

        // Zero-copy converts on the render thread straight from the GPU surface, so it uses neither the swscale
        // converter nor the RAM frame cache (the decoder's fixed texture pool can't be retained).
        if (!session.UsesZeroCopy)
        {
            _converter = new SoftwareFrameConverter(session.IsHdr);
            _cache = new VideoFrameCache(Volatile.Read(ref _cacheBudget));
            _cachedFrameBytes = ffmpeg.av_image_get_buffer_size(session.PixelFormat, session.Width, session.Height, 1);
        }
    }

    // Decodes forward to the target frame, caching every frame read so the surrounding GOP is available for
    // cheap scrub-back. Seeks to the preceding keyframe first unless the target is a short hop ahead of the
    // decoder's current position. Returns false if the stream ends before reaching the target.
    private bool DecodeTo(long target)
    {
        var delta = target - _workerLastDecodedPts;
        var advancingSequentially = _workerLastDecodedPts != NotSet && delta > 0 && delta <= _workerSequentialThreshold;

        if (!advancingSequentially)
            _session!.SeekToKeyframeBefore(target);

        var decodedPts = _workerLastDecodedPts;
        var reached = false;
        while (_session!.TryReadNextFrame(out var pts))
        {
            _cache?.Add(pts, _session.CurrentFrame, _cachedFrameBytes);
            decodedPts = pts;
            if (pts >= target)
            {
                reached = true;
                break;
            }
        }

        if (!reached)
            return false;

        _workerLastDecodedPts = decodedPts;
        return true;
    }

    // After serving the requested frame, decodes a short way past it into the cache so forward playback rides
    // on cache hits and absorbs decode jitter. Only runs when the decoder is already at or ahead of the shown
    // frame (the normal forward case) — it never seeks — and bails the instant the requested frame changes,
    // so scrubbing stays responsive. Only caches; never publishes.
    private void PrefetchAhead(long displayTarget)
    {
        if (_session == null || _cache == null || _workerLastDecodedPts == NotSet || _workerLastDecodedPts < displayTarget)
            return;

        var leadTarget = displayTarget + _workerSequentialThreshold;
        for (var i = 0; i < PrefetchMaxFramesPerCycle && _workerLastDecodedPts < leadTarget; i++)
        {
            if (CurrentRequestTarget() != displayTarget)
                return;

            if (!_session.TryReadNextFrame(out var pts))
                return;

            _cache.Add(pts, _session.CurrentFrame, _cachedFrameBytes);
            _workerLastDecodedPts = pts;
        }
    }

    // The frame the render thread currently wants, recomputed from the latest posted request, so the prefetch
    // loop can bail the moment the user scrubs. Returns NotSet if the source changed or isn't open.
    private long CurrentRequestTarget()
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

        if (url != _workerUrl || _session == null)
            return NotSet;

        var playSeconds = TimeToFrameMapper.ResolvePlaybackSeconds(seconds, _session.DurationSeconds, loop);
        return TimeToFrameMapper.SecondsToFramePts(playSeconds, _session.StreamStartPts, _session.TimeBaseNum, _session.TimeBaseDen, _session.FrameRate);
    }

    private unsafe void PublishFrame(Frame frame, long target)
    {
        if (_zeroCopy)
        {
            lock (_lock)
            {
                _pendingGpuFrame.Unref();                     // drop the previous un-shown frame (latest-wins)
                ffmpeg.av_frame_ref(_pendingGpuFrame, frame); // pin the decoder's GPU surface for the render thread
                _pendingTarget = target;
                _hasPendingGpuFrame = true;
            }

            _framePublished.Set();
            return;
        }

        var rgba = _converter!.Convert(frame);
        var byteCount = rgba.Width * rgba.Height * _converter.BytesPerPixel;

        lock (_lock)
        {
            if (_pendingBuffer == null || _pendingBuffer.Length < byteCount)
                _pendingBuffer = new byte[byteCount];

            rgba.FillImageBuffer(_pendingBuffer, 1);
            _pendingWidth = rgba.Width;
            _pendingHeight = rgba.Height;
            _pendingIsHdr = _session!.IsHdr;
            _pendingTarget = target;
            _hasPendingFrame = true;
        }

        _framePublished.Set();
    }

    private const long NotSet = long.MinValue;
    private const double FrameEpsilonSeconds = 1.0 / 1000.0;
    private const int ExportFrameTimeoutMs = 5000;

    // Default cache budget until the engine assigns this stream a share of the shared global budget.
    private const long CacheBudgetBytes = 512L * 1024 * 1024;

    // Cap on frames read ahead per cycle, so the first prefetch after a seek can't monopolize the worker; the
    // lead distance itself is bounded by the sequential threshold (~0.5 s of video).
    private const int PrefetchMaxFramesPerCycle = 90;

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
    private double _frameRate;

    // Converted-frame handoff (worker → render thread), guarded by _lock.
    private byte[]? _pendingBuffer;
    private int _pendingWidth;
    private int _pendingHeight;
    private bool _pendingIsHdr;
    private long _pendingTarget;
    private bool _hasPendingFrame;

    // Zero-copy GPU handoff (worker → render thread). _zeroCopy is set at open under _lock; the pending GPU
    // frame is ref'd by the worker and moved out by the render thread, both under _lock.
    private bool _zeroCopy;
    private readonly Frame _pendingGpuFrame = new();
    private bool _hasPendingGpuFrame;

    // Render-thread only.
    private long _lastUploadedTarget = NotSet;
    private HardwareFrameConverter? _hardwareConverter;
    private readonly Frame _renderGpuFrame = new();

    // Cache budget assigned by the engine (its share of the shared global budget); the eval thread writes it,
    // the worker reads it when creating or refreshing the cache.
    private long _cacheBudget = CacheBudgetBytes;

    // Worker-thread only.
    private VideoDecoderSession? _session;
    private SoftwareFrameConverter? _converter;
    private VideoFrameCache? _cache;
    private int _cachedFrameBytes;
    private string? _workerUrl;
    private long _workerLastTarget = NotSet;
    private long _workerLastDecodedPts = NotSet;
    private long _workerSequentialThreshold = 1;
}
