#nullable enable
using System;
using System.IO;
using T3.Core.Audio.Native;
using T3.Core.Logging;

namespace T3.Core.Audio;

/// <summary>
/// A file-based operator source in the native engine: playback triggers, stale handling and metering.
/// </summary>
public abstract class OperatorAudioStreamBase
{
    /// <summary>Gets the duration of the audio stream in seconds.</summary>
    internal double Duration { get; }

    /// <summary>The source the audio graph routes.</summary>
    internal AudioHandle Source { get; }

    internal bool IsPaused { get; set; }
    internal bool IsPlaying { get; set; }

    public string FilePath { get; }

    /// <summary>Indicates whether the stream has been stopped and reset due to being stale (not actively updated).</summary>
    internal bool IsStaleStopped;

    /// <summary>
    /// Set while an [AudioBus] owns this stream's level — i.e. it is routed into the audio processing graph,
    /// which applies the per-source gain itself. Writing the volume here too would have the two fighting every frame.
    /// </summary>
    internal bool GraphOwnsVolume;

    /// <summary>
    /// Plays through the operator bus again once no bus routes this source any more: unwired,
    /// the bus deleted, or an editor preview that stopped and left the source in its stopped bus.
    /// </summary>
    internal void ReclaimRouting()
    {
        var operatorBus = AudioMixerManager.OperatorBus;
        if (_engine.GetRouteTarget(Source) != operatorBus)
            _engine.Route(Source, operatorBus);
    }

    /// <summary>Starts playback of the audio stream.</summary>
    internal void Play()
    {
        IsStaleStopped = false;
        _engine.Play(Source);

        // Starting a source that ran to its end rewinds it, which would replace a seek made just before.
        if (_pendingSeekSecs >= 0)
        {
            _engine.Seek(Source, _pendingSeekSecs);
            _pendingSeekSecs = -1;
        }

        IsPlaying = true;
        IsPaused = false;

        if (!_isUserMuted && !GraphOwnsVolume)
            _engine.SetVolume(Source, _currentVolume);
    }

    /// <summary>Pauses playback of the audio stream.</summary>
    internal void Pause()
    {
        if (!IsPlaying || IsPaused)
            return;

        _engine.Pause(Source);
        IsPaused = true;
    }

    /// <summary>Resumes playback of a paused audio stream.</summary>
    internal void Resume()
    {
        if (!IsPaused)
            return;

        _engine.Play(Source);
        IsPaused = false;
    }

    /// <summary>Stops playback and resets the stream position to the beginning.</summary>
    internal void Stop()
    {
        IsPlaying = false;
        IsPaused = false;
        IsStaleStopped = false;
        _engine.Pause(Source);
        _engine.Seek(Source, 0);
    }

    /// <summary>
    /// Sets the stale state of the stream. Stale streams are paused to prevent them from contributing to the mix.
    /// During export mode, we pause but don't reset position (to preserve playback continuity when stream becomes active again).
    /// </summary>
    internal void SetStale(bool stale)
    {
        if (IsStaleStopped == stale)
            return;

        IsStaleStopped = stale;

        if (stale)
        {
            _engine.Pause(Source);

            if (!AudioEngine.IsExporting)
            {
                _engine.Seek(Source, 0);
                IsPlaying = false;
                IsPaused = false;
            }

            return;
        }

        if (!IsPlaying || IsPaused)
            return;

        _engine.Play(Source);
        if (!_isUserMuted && !GraphOwnsVolume)
            _engine.SetVolume(Source, _currentVolume);
    }

    /// <summary>Sets the volume and mute state of the stream.</summary>
    internal void SetVolume(float volume, bool mute)
    {
        _currentVolume = volume;
        _isUserMuted = mute;

        if (!IsPlaying || GraphOwnsVolume)
            return;

        _engine.SetVolume(Source, !mute && !IsStaleStopped ? volume : 0f);
    }

    /// <summary>Sets the playback speed of the stream, clamped between 0.1 and 4.</summary>
    internal void SetSpeed(float speed)
    {
        var clampedSpeed = Math.Clamp(speed, 0.1f, 4f);
        if (Math.Abs(clampedSpeed - _currentSpeed) < 0.001f)
            return;

        _engine.SetPitch(Source, clampedSpeed);
        _currentSpeed = clampedSpeed;
    }

    /// <summary>Seeks to a position in seconds.</summary>
    internal void Seek(float timeInSeconds)
    {
        _engine.Seek(Source, timeInSeconds);
        _pendingSeekSecs = timeInSeconds;
    }

    /// <summary>Restarts the stream after an export operation, resetting position and restoring playback state.</summary>
    internal void RestartAfterExport()
    {
        IsStaleStopped = false;
        _engine.Seek(Source, 0);
        _engine.Play(Source);

        if (!_isUserMuted && !GraphOwnsVolume)
            _engine.SetVolume(Source, _currentVolume);

        IsPlaying = true;
        IsPaused = false;
    }

    /// <summary>Gets the peak audio level (0.0 to 1.0) of the stream for metering purposes.</summary>
    internal float GetLevel()
    {
        if (!IsPlaying || (IsPaused && !IsStaleStopped))
            return 0f;

        return Math.Min(_engine.GetLevel(Source), 1f);
    }

    /// <summary>Disposes of the audio stream.</summary>
    internal void Dispose() => _engine.Unload(Source);

    /// <summary>
    /// Prepares the stream for export by resetting position and playback state.
    /// The stream will require an explicit play trigger to start during export.
    /// </summary>
    internal void PrepareForExport()
    {
        _engine.Pause(Source);
        _engine.Seek(Source, 0);

        // Reset playback state so streams don't auto-play when un-staled during export.
        IsPlaying = false;
        IsPaused = false;
    }

    /// <summary>Loads a file as a stopped, silent source playing through the operator bus.</summary>
    private protected static bool TryLoadSource(string filePath, NativeAudioEngine engine, out AudioHandle source, out double duration, out int channels, out int sampleRate)
    {
        source = default;
        duration = 0;
        channels = 0;
        sampleRate = 0;

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return false;

        source = engine.LoadFile(filePath);
        if (!source.IsValid)
            return false;

        duration = engine.GetLengthSecs(source);
        if (duration <= 0 || duration > MaxDurationSecs || !engine.TryGetFormat(source, out channels, out sampleRate))
        {
            Log.Error($"[Audio] '{Path.GetFileName(filePath)}' has no usable audio.");
            engine.Unload(source);
            source = default;
            return false;
        }

        engine.Route(source, AudioMixerManager.OperatorBus);
        engine.SetVolume(source, 0f);
        return true;
    }

    private protected OperatorAudioStreamBase(NativeAudioEngine engine, AudioHandle source, double duration, string filePath)
    {
        _engine = engine;
        Source = source;
        Duration = duration;
        FilePath = filePath;
    }

    private protected readonly NativeAudioEngine _engine;

    private const double MaxDurationSecs = 36000;

    private float _currentVolume = 1f;
    private float _currentSpeed = 1f;
    private bool _isUserMuted;
    private float _pendingSeekSecs = -1;
}
