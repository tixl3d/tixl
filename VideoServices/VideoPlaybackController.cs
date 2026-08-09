using System.Threading;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Utils;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using T3.Core.Logging;
using T3.Core.Resource;
using T3.Core.Video;
using CoreTexture2D = T3.Core.DataTypes.Texture2D;

namespace T3.VideoServices;

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
    public bool Update(string absolutePath, double requestedSeconds, bool loop, bool renderingToFile,
                       VideoPlaybackOptimization optimization)
    {
        EnsureWorkerStarted();

        lock (_lock)
        {
            _requestedUrl = absolutePath;
            _requestedSeconds = requestedSeconds;
            _requestedLoop = loop;
            _requestedMode = optimization;
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
                    // This frame's surface belongs to the current session. Claim it so a concurrent mode/source
                    // switch on the worker can't dispose that session while Convert is reading the surface below.
                    _renderConverting = true;
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
            try
            {
                var converted = _hardwareConverter.Convert(_renderGpuFrame, _renderGpuFrame.Width, _renderGpuFrame.Height);
                if (converted != null)
                {
                    Texture = converted;
                    produced = true;
                }
            }
            catch (Exception e)
            {
                // A convert can transiently fail right after a source/mode switch — a frame whose decoder session is
                // being torn down, or the shared device mid-reconfigure. Keep the last valid texture rather than
                // nulling the output or letting the exception surface as an operator error; the next frame recovers.
                Log.Debug($"Zero-copy convert skipped (transient): {e.Message}");
            }
            finally
            {
                _renderGpuFrame.Unref();
                lock (_lock)
                {
                    _renderConverting = false;
                    Monitor.PulseAll(_lock); // release a worker waiting in OpenSource to tear the session down
                }
            }
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

                    // Both publish paths satisfy the wait: the software path flags _hasPendingFrame, the
                    // zero-copy path _hasPendingGpuFrame. Ignoring the latter made every new frame ride
                    // out the full timeout in zero-copy exports.
                    if (_lastUploadedTarget == target
                        || (_hasPendingFrame && _pendingTarget == target)
                        || (_hasPendingGpuFrame && _pendingTarget == target))
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
        _softwareTexture?.Dispose(); // the zero-copy output texture is owned (and disposed) by the converter above
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

        // Own a dedicated software-upload texture, separate from the zero-copy converter's output. The two used to
        // alias through Texture, so switching zero-copy → software disposed the converter's output behind its back;
        // switching back then threw "COM object null" when EnsureOutput read the freed texture's description.
        if (_softwareTexture == null || _softwareTexture.Description.Width != _pendingWidth
            || _softwareTexture.Description.Height != _pendingHeight || _softwareTexture.Description.Format != format)
        {
            _softwareTexture?.Dispose();
            _softwareTexture = CoreTexture2D.CreateTexture2D(new Texture2DDescription
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
            ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, _softwareTexture, 0);
        }

        Texture = _softwareTexture;
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
        VideoPlaybackOptimization mode;
        lock (_lock)
        {
            url = _requestedUrl;
            seconds = _requestedSeconds;
            loop = _requestedLoop;
            mode = _requestedMode;
        }

        if (url == null)
            return;

        if (url != _workerUrl || mode != _workerMode)
            OpenSource(url, mode);

        if (_session == null)
            return;

        _cache?.SetBudget(Volatile.Read(ref _cacheBudget));

        var playSeconds = TimeToFrameMapper.ResolvePlaybackSeconds(seconds, _session.DurationSeconds, loop);
        var target = TimeToFrameMapper.SecondsToFramePts(playSeconds, _session.StreamStartPts,
                                                         _session.TimeBaseNum, _session.TimeBaseDen, _session.FrameRate);

        if (target == _workerLastTarget)
            return;

        Frame frameToPublish;
        if (_cache != null && _cache.TryGet(FrameIndexForTime(playSeconds), out var cachedFrame))
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

    private void OpenSource(string url, VideoPlaybackOptimization mode)
    {
        _workerUrl = url;
        _workerMode = mode;

        // Both GPU frames referencing the previous session's surfaces must be released before we dispose it, or a
        // convert on the render thread reads freed D3D resources. The still-queued frame we can drop here; a frame
        // already handed to the render thread (mid-Convert) we must WAIT for — its convert AddRefs the surface, and
        // on a freed resource that's an AccessViolationException, which a normal try/catch can't contain.
        lock (_lock)
        {
            if (_hasPendingGpuFrame)
            {
                _pendingGpuFrame.Unref();
                _hasPendingGpuFrame = false;
            }

            while (_renderConverting)
                Monitor.Wait(_lock);
        }

        _converter?.Dispose();
        _converter = null;
        _session?.Dispose();
        _session = null;
        _cache?.Dispose();
        _cache = null;
        _workerLastTarget = NotSet;
        _workerLastDecodedPts = NotSet;

        var session = VideoDecoderSession.TryOpen(url, mode, out var error);
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
        Log.Gated.VideoRender($"Video decode path: {path} — {session.Width}x{session.Height} {session.PixelFormat}");

        // ~0.5 s prefetch lead. Forward catch-up seeks only past this; it grows to the observed GOP depth so
        // decoding forward inside one long GOP never re-seeks the keyframe (starts at ~0.5 s before learning).
        _workerSequentialThreshold = session.TimeBaseDen / (2L * Math.Max(1, session.TimeBaseNum));
        _workerForwardSeekThreshold = _workerSequentialThreshold;

        // Zero-copy converts on the render thread straight from the GPU surface, so it uses neither the swscale
        // converter nor the RAM frame cache (the decoder's fixed texture pool can't be retained).
        if (!session.UsesZeroCopy)
        {
            _converter = new SoftwareFrameConverter(session.IsHdr);
            _cache = new VideoFrameCache(Volatile.Read(ref _cacheBudget));
            _cachedFrameBytes = ffmpeg.av_image_get_buffer_size(session.PixelFormat, session.Width, session.Height, 1);
        }
    }

    // Cache key for a decoded frame: the frame-grid-snapped PTS of the frame containing it — the same value the
    // render thread asks for (SecondsToFramePts). Keying by the raw decode PTS instead misses on every lookup
    // (the snapped target rarely equals the frame's exact PTS, e.g. 41 vs 42 at 23.976 fps), which forces a
    // re-decode — and, since prefetch has run the decoder ahead, a backward seek + GOP re-decode — every frame.
    // Cache key = integer frame index. The render thread asks for the frame at a time via FLOOR (the frame whose
    // display interval contains it); a decoded frame reports which frame it IS via ROUND (its PTS sits on a frame
    // boundary, so rounding avoids the float-edge off-by-one a floor would hit). Both yield the same index for one
    // frame, so forward playback hits the prefetched cache instead of re-decoding (and backward-seeking) at stray
    // indices. Falls back to raw PTS only when the frame rate is unknown.
    private long FrameIndexForTime(double seconds)
    {
        var fps = _session!.FrameRate;
        return fps > 0 ? (long)Math.Floor(seconds * fps) : 0;
    }

    private long FrameIndexForPts(long pts)
    {
        var fps = _session!.FrameRate;
        if (fps <= 0)
            return pts;

        var seconds = TimeToFrameMapper.PtsToSeconds(pts, _session.StreamStartPts, _session.TimeBaseNum, _session.TimeBaseDen);
        return (long)Math.Round(seconds * fps);
    }

    // Decodes forward to the target frame, caching every frame read so the surrounding GOP is available for
    // cheap scrub-back. Seeks to the preceding keyframe first unless the target is a short hop ahead of the
    // decoder's current position. A target past the end resolves to the final frame; false means nothing
    // decoded at all.
    private bool DecodeTo(long target)
    {
        var known = _workerLastDecodedPts != NotSet;
        var delta = known ? target - _workerLastDecodedPts : 0;

        // Seek only when the target is behind the decoder (no backward decode) or far enough ahead to likely cross
        // into a later GOP, where a keyframe seek skips real decode work. A forward target inside the current GOP
        // decodes forward instead — seeking back to the keyframe and re-running the whole GOP on every catch-up
        // frame is what stops long-GOP playback from ever converging.
        var seeking = !known || delta < 0 || delta > _workerForwardSeekThreshold;

        if (seeking)
            _session!.SeekToKeyframeBefore(target);

        var firstPts = NotSet;
        var decodedPts = _workerLastDecodedPts;
        var reached = false;
        while (_session!.TryReadNextFrame(out var pts))
        {
            if (firstPts == NotSet)
                firstPts = pts;
            _cache?.Add(FrameIndexForPts(pts), _session.CurrentFrame, _cachedFrameBytes);
            decodedPts = pts;
            if (pts >= target)
            {
                reached = true;
                break;
            }
        }

        // After a seek the first decoded frame is the GOP keyframe, so keyframe→target is a live GOP-depth sample.
        // Grow the forward-seek threshold to the deepest seen, so forward catch-up within one GOP stays sequential.
        if (seeking && reached && firstPts != NotSet && target - firstPts > _workerForwardSeekThreshold)
            _workerForwardSeekThreshold = target - firstPts;

        // A target past the last frame — a clip trimmed beyond its footage, or a container over-reporting its
        // duration — resolves to the final frame. Reporting failure instead left the target unrecorded, so every
        // later request re-seeked and re-decoded the whole trailing GOP for as long as the playhead sat there.
        if (!reached && firstPts == NotSet)
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

            _cache.Add(FrameIndexForPts(pts), _session.CurrentFrame, _cachedFrameBytes);
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
    private VideoPlaybackOptimization _requestedMode;

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

    // True while the render thread is converting _renderGpuFrame; OpenSource waits on it (under _lock) before
    // disposing the session whose surface that frame references.
    private bool _renderConverting;

    // Render-thread only.
    private long _lastUploadedTarget = NotSet;
    private HardwareFrameConverter? _hardwareConverter;
    private CoreTexture2D? _softwareTexture; // software/read-back upload target; the zero-copy output is the converter's.
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
    private VideoPlaybackOptimization _workerMode;
    private long _workerLastTarget = NotSet;
    private long _workerLastDecodedPts = NotSet;
    private long _workerSequentialThreshold = 1;
    private long _workerForwardSeekThreshold = 1;
}
