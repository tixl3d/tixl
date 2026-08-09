using System.Threading;
using T3.Core.Audio;
using T3.Core.Logging;
using T3.Core.Utils;

namespace T3.VideoServices;

/// <summary>
/// Keeps one video's audio playing in sync with the timeline. Owns an <see cref="AudioDecoderSession"/> and a
/// <see cref="VideoAudioStream"/> push stream, and runs a worker that tops the push buffer up toward the
/// requested source time. The timeline is master: sync means holding a small buffer whose *front* sits at the
/// requested time, and re-seeking when it drifts away.
///
/// Audio only plays while the playhead advances forward at roughly 1×. Stopped, reversed, scrubbed or
/// fast-forwarded playback would need pitch-shifted or granular audio, so it stays silent instead.
/// </summary>
internal sealed class VideoAudioTrack : IDisposable
{
    /// <summary>The BASS channel for the audio graph to route; 0 until the worker has opened the track (or when
    /// the file has no audio at all). Read from the evaluation thread.</summary>
    public int Channel => Volatile.Read(ref _channel);

    /// <summary>
    /// Posts the source time this track should be playing. <paramref name="absolutePath"/> must be the original
    /// file — a preview proxy carries no audio track. Not calling this silences the track within a few frames.
    /// </summary>
    public void Request(string absolutePath, double sourceSeconds, bool loop)
    {
        EnsureWorkerStarted();

        lock (_lock)
        {
            _requestedUrl = absolutePath;
            _requestedSeconds = sourceSeconds;
            _requestedLoop = loop;
            _lastRequestMs = Environment.TickCount64;
        }

        _wake.Set();
    }

    /// <summary>
    /// Export counterpart of <see cref="Request"/>: decodes and queues this frame's audio <b>synchronously, on
    /// the calling thread</b>, before the render loop pulls the mixdown. Nothing here advances with the wall
    /// clock — the queue moves one exported frame per call — so two renders of the same range produce
    /// identical audio. Returns the channel for the graph to route, as <see cref="Request"/> does.
    /// </summary>
    public int RequestForExport(string absolutePath, double sourceSeconds, bool loop)
    {
        lock (_workLock)
        {
            // Also tells the worker to keep its hands off: it self-clears once export stops calling.
            var now = Environment.TickCount64;
            var renderStarting = now - _lastExportRequestMs > ExportModeHoldMs;
            _lastExportRequestMs = now;

            if (!EnsureSource(absolutePath))
                return 0;

            var target = TimeToFrameMapper.ResolvePlaybackSeconds(sourceSeconds, _session!.DurationSeconds, loop);

            // The exported frame's duration is exactly the step between two requests, so nothing has to thread
            // the render fps through the operator API. The first frame has no previous step to measure.
            var step = target - _lastTarget;
            var frameSeconds = step > 0 && step <= MaxForwardStepSeconds ? step : DefaultExportFrameSeconds;
            _lastTarget = target;

            // Re-seek only on a real discontinuity — the first exported frame, or a cut — never on drift:
            // there is no clock to drift against, and a spurious re-seek would make the render non-repeatable.
            // The queue still holds live playback audio when a render begins; without dropping it, the first
            // exported frames would carry a remnant of whatever was last heard in the editor.
            var queued = _stream!.BufferedSeconds;
            if (renderStarting || double.IsNaN(_fedSeconds) || Math.Abs(_fedSeconds - queued - target) > frameSeconds * 2)
                Resync(target);

            // One frame's worth is enough: the mixdown pulls exactly that much per frame, so the queue neither
            // starves nor grows.
            TopUp(loop, frameSeconds);
            return _stream.Channel;
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _wake.Set();
        if (_worker != null && !_worker.Join(TimeSpan.FromSeconds(2)))
        {
            // Stuck in a blocking decode/IO call. Leaking its resources is safer than disposing them under
            // its feet — the thread is background, so it can't keep the process alive.
            Log.Warning("[VideoAudio] The worker did not exit in time; leaving its resources behind.");
            return;
        }

        // The worker has exited, but the evaluation thread can still be inside RequestForExport, so take the
        // same lock it holds. After a device change the stream's handle is already dead — freeing it again
        // would act on a handle BASS has since re-issued.
        lock (_workLock)
        {
            if (_stream is { IsInvalidated: false })
                _stream.Dispose();

            _session?.Dispose();
            _stream = null;
            _session = null;
        }

        _wake.Dispose();
        _cancellation.Dispose();
    }

    private void EnsureWorkerStarted()
    {
        if (_worker != null)
            return;

        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "Video audio" };
        _worker.Start();
    }

    private void WorkerLoop()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            // Woken by each request, but also ticking on its own so a track whose operator stopped asking
            // still gets muted.
            _wake.WaitOne(TickIntervalMs);
            if (_cancellation.IsCancellationRequested)
                break;

            try
            {
                Tick();
                lock (_workLock)
                {
                    LogDiagnostics();
                }
            }
            catch (Exception e)
            {
                // An unhandled exception on a background thread would terminate the editor process.
                Log.Warning("Video audio worker error: " + e);
            }
        }
    }

    private void Tick()
    {
        string? url;
        double requestedSeconds;
        bool loop;
        long lastRequestMs;
        lock (_lock)
        {
            url = _requestedUrl;
            requestedSeconds = _requestedSeconds;
            loop = _requestedLoop;
            lastRequestMs = _lastRequestMs;
        }

        if (url == null)
            return;

        lock (_workLock)
        {
            TickCore(url, requestedSeconds, loop, lastRequestMs);
        }
    }

    private void TickCore(string url, double requestedSeconds, bool loop, long lastRequestMs)
    {
        // While export is driving this track it owns the session and the queue outright; a wall-clock tick
        // interleaved with it would flush or refill mid-render and make the output non-repeatable.
        if (Environment.TickCount64 - _lastExportRequestMs < ExportModeHoldMs)
            return;

        if (!EnsureSource(url))
            return;

        // The operator stopped asking — not evaluated, or outside its clip range.
        if (Environment.TickCount64 - lastRequestMs > SilenceAfterIdleMs)
        {
            StopFeeding();
            return;
        }

        var target = TimeToFrameMapper.ResolvePlaybackSeconds(requestedSeconds, _session!.DurationSeconds, loop);
        var now = Environment.TickCount64;

        // The worker ticks faster than the operator posts requests, so an unchanged time means "no new frame
        // yet", not "the playhead stopped" — only elapsed wall time can tell those apart. Judging every tick
        // would flush the queue between every pair of rendered frames.
        if (target != _lastTarget)
        {
            var step = target - _lastTarget;
            _lastTarget = target;
            _lastTargetChangeMs = now;

            // A looped target wraps duration→0 while still moving forward; unwrap it so the seam doesn't read
            // as a jump — the audio TopUp pre-fed across the seam then plays through instead of being flushed.
            if (loop && step < 0 && _session.DurationSeconds > 0)
                step += _session.DurationSeconds;

            // Consecutive well-behaved steps, so a scrub (a jump per frame) never accumulates enough to feed a
            // grain before the next jump flushes it. Normal playback qualifies within a frame or two.
            var normalPlayback = !double.IsNaN(step) && step > 0 && step <= MaxForwardStepSeconds;
            _forwardSteps = normalPlayback ? _forwardSteps + 1 : 0;
        }
        else if (now - _lastTargetChangeMs > StationaryTimeoutMs)
        {
            _forwardSteps = 0;
        }

        if (_forwardSteps < RequiredForwardSteps)
        {
            StopFeeding();
            return;
        }

        // What is audible now is the front of the queue: everything fed, minus everything still waiting. The
        // instantaneous queue length swings by tens of milliseconds as the mixer pulls in chunks, so threshold
        // a smoothed estimate — comparing the raw value against a tight threshold resyncs on the swing itself,
        // which flushes the queue several times a second and shreds the audio.
        var drift = _fedSeconds - _stream!.BufferedSeconds - target;

        // Around the loop seam the fed position wraps to 0 before the target does; compare on the circle so
        // that moment doesn't read as a full-duration offset and force a resync right at the seam.
        if (loop && _session.DurationSeconds > 0)
        {
            var duration = _session.DurationSeconds;
            drift = MathUtils.Fmod(drift + duration * 0.5, duration) - duration * 0.5;
        }

        _smoothedDrift = double.IsNaN(_smoothedDrift) ? drift : _smoothedDrift + (drift - _smoothedDrift) * DriftSmoothing;

        if (double.IsNaN(_fedSeconds) || Math.Abs(_smoothedDrift) > ResyncThresholdSeconds)
            Resync(target);

        TopUp(loop, TargetFillSeconds);
    }

    // Opens the source and push stream for this url, rebuilding both when a device change invalidated them:
    // the push stream declares the rate of the data fed into it, so a session resampling to a different rate
    // would be played back at the wrong pitch rather than merely re-resampled.
    /// <summary>True once both <see cref="_session"/> and <see cref="_stream"/> are usable for this url.</summary>
    private bool EnsureSource(string url)
    {
        if (_stream is { IsInvalidated: true })
            DropAfterDeviceChange();

        if (url != _workerUrl || _sessionSampleRate != AudioConfig.MixerFrequency)
            OpenSource(url);

        return _session != null && EnsureStream();
    }

    private void TopUp(bool loop, double targetFillSeconds)
    {
        var stream = _stream!;
        var floatsPerSecond = (double)(stream.SampleRate * VideoAudioStream.Channels);

        for (var i = 0; i < MaxChunksPerTick && stream.BufferedSeconds < targetFillSeconds; i++)
        {
            if (!_session!.TryDecodeChunk(out var chunk, out var chunkStart))
            {
                if (!loop)
                    return; // end of the track — the queue drains to silence

                Resync(0);
                continue;
            }

            var chunkEnd = chunkStart + chunk.Length / floatsPerSecond;
            if (!double.IsNaN(_pendingStart))
            {
                // A seek lands at or before the request, so drop what precedes it instead of playing it early.
                if (chunkEnd <= _pendingStart)
                {
                    _fedSeconds = chunkEnd;
                    continue;
                }

                var skippedFloats = (int)Math.Round((_pendingStart - chunkStart) * stream.SampleRate) * VideoAudioStream.Channels;
                if (skippedFloats > 0 && skippedFloats < chunk.Length)
                {
                    chunk = chunk[skippedFloats..];
                    chunkStart = _pendingStart;
                }

                _pendingStart = double.NaN;
            }

            stream.Feed(chunk);
            _fedSeconds = chunkStart + chunk.Length / floatsPerSecond;
            _chunksFed++;
        }
    }

    // One summary line per second while the Audio log category is on. The counters are what distinguish the
    // failure modes: repeated resyncs mean the sync logic is thrashing, a buffer stuck near zero means decode
    // or the pull rate can't keep up, and steps stuck at zero means the playhead never looked like playback.
    private void LogDiagnostics()
    {
        if (!Log.Gated.AudioEnabled || _stream == null)
            return;

        var now = Environment.TickCount64;
        if (now < _diagnosticsDueMs)
            return;

        _diagnosticsDueMs = now + 1000;
        Log.Gated.Audio($"[VideoAudio] target={_lastTarget:0.000}s fed={_fedSeconds:0.000}s "
                        + $"buffered={_stream.BufferedSeconds * 1000:0}ms drift={_smoothedDrift * 1000:0}ms steps={_forwardSteps} "
                        + $"resyncs/s={_resyncs} chunks/s={_chunksFed} mutes/s={_mutes}");
        _resyncs = 0;
        _chunksFed = 0;
        _mutes = 0;
    }

    // Drops the queue and marks the track un-anchored, so the next audible stretch starts with a fresh seek.
    private void StopFeeding()
    {
        if (double.IsNaN(_fedSeconds))
            return;

        _stream?.Flush();
        _fedSeconds = double.NaN;
        _pendingStart = double.NaN;
        _smoothedDrift = double.NaN;
        _mutes++;
    }

    private void Resync(double target)
    {
        _session!.SeekTo(target);
        _stream!.Flush();
        _fedSeconds = target;
        _pendingStart = target;
        _smoothedDrift = double.NaN;
        _resyncs++;
    }

    private bool EnsureStream()
    {
        if (_stream != null)
            return true;

        _stream = VideoAudioStream.TryCreate();
        if (_stream == null)
            return false;

        Volatile.Write(ref _channel, _stream.Channel);
        return true;
    }

    private void OpenSource(string url)
    {
        _session?.Dispose();
        _workerUrl = url;
        _fedSeconds = double.NaN;
        _lastTarget = double.NaN;
        _pendingStart = double.NaN;
        _forwardSteps = 0;

        // BASS init is what settles AudioConfig.MixerFrequency from the output device — until then it reads a
        // 48 kHz placeholder. Initializing here (idempotent) rather than relying on the push stream to do it
        // keeps the resampler's target and the stream's declared rate from disagreeing on the very first tick.
        AudioMixerManager.Initialize();
        _sessionSampleRate = AudioConfig.MixerFrequency;

        // Resampling straight to the mixer's rate spares BASS a second conversion on every pull.
        _session = AudioDecoderSession.TryOpen(url, _sessionSampleRate, out var error);
        if (error != null)
            Log.Warning($"[VideoAudio] {error}");
        else if (_session == null)
            Log.Gated.Audio($"[VideoAudio] '{Path.GetFileName(url)}' has no audio track");
        else
            Log.Gated.Audio($"[VideoAudio] '{Path.GetFileName(url)}' {_session.SourceSampleRate} Hz "
                            + $"{_session.SourceChannels} ch -> {_sessionSampleRate} Hz stereo, "
                            + $"{_session.DurationSeconds:0.00}s");
    }

    // A device change frees every BASS handle and may change the mixer's rate, so the push stream and the
    // resampler (built for the old rate) both have to be rebuilt. The handles are already dead — freeing them
    // again would act on whatever BASS has since handed out.
    private void DropAfterDeviceChange()
    {
        Volatile.Write(ref _channel, 0);
        _stream = null;
        _session?.Dispose();
        _session = null;
        _workerUrl = null;
        _sessionSampleRate = 0;
        _fedSeconds = double.NaN;
        _lastTarget = double.NaN;
        _forwardSteps = 0;
    }

    private const int TickIntervalMs = 10;
    private const int SilenceAfterIdleMs = 100;

    // Enough queued audio to ride out decode jitter, but short enough that a resync isn't audible as a gap.
    private const double TargetFillSeconds = 0.2;

    // Largest per-frame playhead advance still treated as normal playback; beyond it the playhead jumped.
    private const double MaxForwardStepSeconds = 0.25;

    // Only a sustained offset should trigger a re-seek; steady playback needs none at all, since the step gate
    // already catches every jump. Generous enough to clear the queue-length swing plus the constant lateness
    // from mixer-side buffering that the queue length can't see.
    private const double ResyncThresholdSeconds = 0.3;

    // Per-tick weight of the drift estimate; ~200 ms of smoothing at the 10 ms tick.
    private const double DriftSmoothing = 0.05;

    // How long the requested time may sit unchanged before the playhead counts as genuinely stopped. Longer
    // than a slow frame, short enough that a pause is heard as immediate.
    private const int StationaryTimeoutMs = 120;

    private const int RequiredForwardSteps = 2;

    // Bounds the work one tick can do, so a pathological source can't monopolize the worker.
    private const int MaxChunksPerTick = 64;

    // How long after an export request the worker keeps out of the way. Long enough to bridge a slow exported
    // frame, short enough that live playback resumes promptly once the render ends.
    private const int ExportModeHoldMs = 500;

    // Only used for the very first exported frame, before a step between two requests can be measured.
    private const double DefaultExportFrameSeconds = 1 / 30.0;

    private readonly object _lock = new();

    // Guards the decode session, the push stream and the feed position — shared between the worker thread and
    // the evaluation thread, which drives them directly during export.
    private readonly object _workLock = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly CancellationTokenSource _cancellation = new();
    private Thread? _worker;

    // Request (evaluation thread → worker), guarded by _lock.
    private string? _requestedUrl;
    private double _requestedSeconds;
    private bool _requestedLoop;
    private long _lastRequestMs;

    private int _channel;

    private long _lastExportRequestMs = long.MinValue / 2;

    // Guarded by _workLock: the worker thread during playback, the evaluation thread during export.
    private AudioDecoderSession? _session;
    private VideoAudioStream? _stream;
    private string? _workerUrl;
    private int _sessionSampleRate;
    private double _fedSeconds = double.NaN;
    private double _lastTarget = double.NaN;
    private double _pendingStart = double.NaN;
    private double _smoothedDrift = double.NaN;
    private long _lastTargetChangeMs;
    private int _forwardSteps;

    // Diagnostics, emitted once per second when the Audio log category is on.
    private long _diagnosticsDueMs;
    private int _resyncs;
    private int _chunksFed;
    private int _mutes;
}
