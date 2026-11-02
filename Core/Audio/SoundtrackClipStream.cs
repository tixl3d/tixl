#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using T3.Core.Animation;
using T3.Core.Audio.Native;
using T3.Core.Settings;
using T3.Core.Logging;

namespace T3.Core.Audio;

/// <summary>
/// Controls the playback of a <see cref="TimelineAudioClip"/> by the <see cref="AudioEngine"/>, as a sound in
/// the native engine. The timeline is the master: playback runs freely and is reseeked once it drifts too far.
/// </summary>
internal sealed class SoundtrackClipStream
{
    private SoundtrackClipStream(NativeAudioEngine engine, AudioHandle source)
    {
        _engine = engine;
        _source = source;
    }

    public double Duration;
    internal bool IsInUse;
    public bool IsNew = true;
    internal double TargetTime { get; set; }

    internal AudioClipResourceHandle ResourceHandle = null!;

    internal AudioHandle Source => _source;
    internal int ChannelCount { get; private set; }
    internal int SampleRate { get; private set; }

    internal void UpdateSoundtrackPlaybackSpeed(double newSpeed)
    {
        if (newSpeed == 0.0)
        {
            _engine.Pause(_source);
            return;
        }

        // Reverse playback isn't supported: backwards scrubbing plays forward at the same rate and is
        // kept near the playhead by the resync in UpdateSoundtrackTime.
        _engine.SetPitch(_source, (float)Math.Abs(newSpeed));
        _engine.Play(_source);
    }

    /// <summary>
    /// Creates a <see cref="SoundtrackClipStream"/> by loading an <see cref="AudioClipResourceHandle"/>.
    /// </summary>
    internal static bool TryLoadSoundtrackClip(AudioClipResourceHandle handle, [NotNullWhen(true)] out SoundtrackClipStream? stream)
    {
        stream = null;

        if (handle.LoadingAttemptFailed)
            return false;
        
        if (string.IsNullOrEmpty(handle.Clip.AssetPath))
            return false;

        handle.LoadingAttemptFailed = true;
        
        if (!handle.TryGetFileResource(out var file))
        {
            Log.Error($"AudioClip file '{handle.Clip.AssetPath}' does not exist.");
            return false;
        }

        var fileInfo = file.FileInfo;
        if (fileInfo is not { Exists: true })
        {
            Log.Error($"AudioClip file '{handle.Clip.AssetPath}' does not exist.");
            return false;
        }

        var engine = AudioMixerManager.NativeEngine;
        if (engine == null)
            return false;

        var source = engine.LoadFile(fileInfo.FullName);
        if (!source.IsValid)
            return false;

        var duration = engine.GetLengthSecs(source);
        engine.TryGetFormat(source, out var channels, out var sampleRate);
        handle.Clip.LengthInSeconds = (float)duration;

        // A graph-routed clip is placed by its [AudioBus]; a detour through the soundtrack bus
        // would blip it at full volume for the frame before the bus takes it.
        if (!handle.Clip.IsRoutedToGraph)
            engine.Route(source, AudioMixerManager.SoundtrackBus);

        stream = new SoundtrackClipStream(engine, source)
                     {
                         ResourceHandle = handle,
                         Duration = duration,
                         ChannelCount = channels,
                         SampleRate = sampleRate,
                     };

        stream.UpdateSoundtrackPlaybackSpeed(1.0);
        handle.LoadingAttemptFailed = false;
        return true;
    }

    /// <summary>
    /// We try to find a compromise between letting the engine play the audio clip in the correct playback speed
    /// which eventually will drift away from Tooll's Playback time. If the delta between playback and audio-clip
    /// time exceeds a threshold, we resync.
    /// 
    /// Frequent resync causes audio glitches.
    /// Too large of a threshold can disrupt syncing and increase latency.
    /// </summary>
    internal void UpdateSoundtrackTime(Playback playback)
    {
        if (playback.PlaybackSpeed == 0)
        {
            _engine.Pause(_source);
            return;
        }

        var clip = ResourceHandle.Clip;

        // Graph hand-off: while the clip's AudioReference is wired into a bus, the graph owns its routing and
        // level. Checking the real route every frame instead of a one-shot transition avoids racing the bus,
        // whose release can run later in the same frame.
        if (!clip.IsRoutedToGraph && !_engine.GetRouteTarget(_source).IsValid)
            _engine.Route(_source, AudioMixerManager.SoundtrackBus);

        // Elapsed seconds since the clip's TimeRange.Start on the timeline.
        // (TargetTime is set externally to the current playback time-in-seconds.)
        var elapsedInClipSecs = TargetTime - playback.SecondsFromBars(clip.TimeRange.Start);

        // Native-rate target position in the source file.
        var targetSourcePosSecs = clip.SourceOffsetSecs + elapsedInClipSecs;

        // Effective source-end (where the audible content stops).
        var sourceEndSecs = clip.SourceDurationSecs > 0
                                ? clip.SourceOffsetSecs + clip.SourceDurationSecs
                                : clip.LengthInSeconds;

        // Looping: wrap the target position back into the valid source window (clamped to the file's
        // content — a start-extension into negative source time doesn't widen the loop) for as long as
        // the clip lasts. The wrap makes the drift exceed the resync threshold, so the seek happens on
        // the next check.
        var loopStartSecs = Math.Max(clip.SourceOffsetSecs, 0);
        var loopLengthSecs = Math.Min(sourceEndSecs, clip.LengthInSeconds) - loopStartSecs;
        if (clip.IsLooping && loopLengthSecs > 0.01 && elapsedInClipSecs >= 0)
        {
            targetSourcePosSecs = loopStartSecs + elapsedInClipSecs % loopLengthSecs;
        }

        // Optional TimeRange.End bound: skip when End <= Start (the "no explicit end" sentinel).
        var hasClipEnd = clip.TimeRange.End > clip.TimeRange.Start;
        var clipEndSecs = hasClipEnd ? playback.SecondsFromBars(clip.TimeRange.End) : double.PositiveInfinity;

        // targetSourcePos < 0 = the clip was start-extended before the file's content begins: stay
        // muted until the playhead reaches valid source time instead of starting the audio early.
        var isOutOfBounds = elapsedInClipSecs < 0
                            || targetSourcePosSecs < 0
                            || targetSourcePosSecs >= sourceEndSecs
                            || (hasClipEnd && TargetTime >= clipEndSecs);

        var isPlaying = _engine.IsPlaying(_source);

        if (isOutOfBounds)
        {
            if (isPlaying)
                _engine.Pause(_source);

            return;
        }

        if (!isPlaying)
            _engine.Play(_source);

        var speed = playback.PlaybackSpeed;

        // The cursor is what's being mixed right now, and that reaches the speakers one output latency later.
        var latencyInSourceSecs = _engine.OutputLatencySecs * Math.Abs(speed);
        var audiblePosSecs = _engine.GetCursorSecs(_source) - latencyInSourceSecs;
        var soundDelta = (audiblePosSecs - targetSourcePosSecs) * speed;

        var audio = CompositionSettings.Current.Audio;
        if (!clip.IsRoutedToGraph)
        {
            _engine.SetVolume(_source,
                              clip.Volume
                              * audio.SoundtrackVolume
                              * (clip.IsMuted ? 0f : 1f)
                              * (audio.SoundtrackMute ? 0f : 1f));
        }

        // We may not fall behind or skip ahead in playback
        var maxSoundDelta = audio.AudioResyncThreshold * Math.Abs(speed);
        if (Math.Abs(soundDelta) <= maxSoundDelta)
            return;

        _engine.Seek(_source, Math.Max(targetSourcePosSecs + latencyInSourceSecs, 0));
    }

    /// <summary>Stops the sound and resets its speed, so that <see cref="UpdateForExport"/> alone drives it.</summary>
    internal void PrepareForExport()
    {
        _engine.Pause(_source);
        _engine.SetPitch(_source, 1);
    }

    /// <summary>
    /// Positions the sound for the next rendered block. Contiguous frames read sequentially, so it only reseeks
    /// on a real discontinuity: activation, render-range start or a loop wrap.
    /// </summary>
    internal void UpdateForExport(double currentTime)
    {
        var clip = ResourceHandle.Clip;

        var elapsedInClip = currentTime - Playback.Current.SecondsFromBars(clip.TimeRange.Start);
        var targetSourcePos = clip.SourceOffsetSecs + elapsedInClip;
        var sourceEnd = clip.SourceDurationSecs > 0
                            ? clip.SourceOffsetSecs + clip.SourceDurationSecs
                            : clip.LengthInSeconds;

        // Looping clips wrap back into their valid source window for as long as the clip lasts.
        var loopStart = Math.Max(clip.SourceOffsetSecs, 0);
        var loopLength = Math.Min(sourceEnd, clip.LengthInSeconds) - loopStart;
        if (clip.IsLooping && loopLength > 0.01 && elapsedInClip >= 0)
            targetSourcePos = loopStart + elapsedInClip % loopLength;

        // A negative source position means the clip was start-extended before the file's content:
        // it stays silent until the playhead reaches valid source time.
        var isActive = elapsedInClip >= 0 && targetSourcePos >= 0 && targetSourcePos < sourceEnd;
        if (clip.IsLooping && clip.TimeRange.End > clip.TimeRange.Start)
            isActive &= currentTime < Playback.Current.SecondsFromBars(clip.TimeRange.End);

        if (!isActive)
        {
            _engine.Pause(_source);
            return;
        }

        if (Math.Abs(_engine.GetCursorSecs(_source) - targetSourcePos) > ExportResyncThresholdSecs)
            _engine.Seek(_source, targetSourcePos);

        if (!clip.IsRoutedToGraph)
            _engine.SetVolume(_source, clip.IsMuted ? 0f : clip.Volume * CompositionSettings.Current.Audio.SoundtrackVolume);

        _engine.Play(_source);
    }

    internal void DisableSoundtrackStream() => _engine.Unload(_source);

    /** Roughly half a frame at 30 fps: lets contiguous frames read on, while staying inaudible. */
    private const double ExportResyncThresholdSecs = 0.015;

    private readonly NativeAudioEngine _engine;
    private readonly AudioHandle _source;
}
