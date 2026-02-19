#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using ManagedBass;
using T3.Core.Logging;

namespace T3.Core.Audio;

/// <summary>
/// Represents a 3D spatial audio stream for operator-based playback with native BASS 3D positioning.
/// Unlike StereoOperatorAudioStream, this plays directly to BASS output (not through the mixer)
/// to enable hardware-accelerated 3D audio processing.
/// </summary>
public sealed class SpatialOperatorAudioStream
{
    /// <summary>
    /// Gets the BASS stream handle for this audio stream.
    /// </summary>
    public int StreamHandle { get; private init; }

    /// <summary>
    /// Gets the duration of the audio stream in seconds.
    /// </summary>
    internal double Duration { get; private init; }

    /// <summary>
    /// Gets the file path of the loaded audio file.
    /// </summary>
    private string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the stream is currently playing.
    /// </summary>
    internal bool IsPlaying { get; private set; }

    /// <summary>
    /// Gets or sets whether the stream is currently paused.
    /// </summary>
    internal bool IsPaused { get; private set; }

    /// <summary>
    /// Indicates whether the stream has been stopped and reset due to being stale (not actively updated).
    /// </summary>
    private bool IsStoppedDueToStale { get; set; }

    /// <summary>
    /// The 3D position of the audio source.
    /// </summary>
    private Vector3 _position = Vector3.Zero;

    /// <summary>
    /// The velocity of the audio source for Doppler effect calculations.
    /// </summary>
    private Vector3 _velocity = Vector3.Zero;

    /// <summary>
    /// The orientation direction of the audio source.
    /// </summary>
    private Vector3 _orientation = new(0, 0, -1);

    /// <summary>
    /// The minimum distance at which the audio starts to attenuate.
    /// </summary>
    private float _minDistance = 1.0f;

    /// <summary>
    /// The maximum distance at which the audio is no longer audible.
    /// </summary>
    private float _maxDistance = 100.0f;

    /// <summary>
    /// Whether the audio source is currently beyond max distance (should be silent).
    /// </summary>
    private bool _isBeyondMaxDistance;

    /// <summary>
    /// Linear distance attenuation factor (0.0 at maxDistance, 1.0 at minDistance or closer).
    /// Applied on top of BASS's 3D processing to ensure proper linear falloff.
    /// </summary>
    private float _distanceAttenuation = 1.0f;

    /// <summary>
    /// The 3D processing mode for the audio source.
    /// </summary>
    private Mode3D _3dMode = Mode3D.Normal;

    /// <summary>
    /// The inner cone angle in degrees within which audio is at full volume.
    /// </summary>
    private float _innerAngleDegrees = 360.0f;

    /// <summary>
    /// The outer cone angle in degrees beyond which audio is at the outer volume.
    /// </summary>
    private float _outerAngleDegrees = 360.0f;

    /// <summary>
    /// The volume level outside the outer cone (0.0 to 1.0).
    /// </summary>
    private float _outerVolume = 1.0f;

    /// <summary>
    /// The current volume level (0.0 to 1.0).
    /// </summary>
    private float _currentVolume = 1.0f;

    /// <summary>
    /// The current playback speed multiplier.
    /// </summary>
    private float _currentSpeed = 1.0f;

    /// <summary>
    /// The default playback frequency of the stream.
    /// </summary>
    private float _defaultPlaybackFrequency;


    /// <summary>
    /// Cached sample frequency of the audio stream.
    /// </summary>
    private int _cachedFrequency;

    /// <summary>
    /// Indicates whether the stream is muted by user request.
    /// </summary>
    private bool _isUserMuted;

    /// <summary>
    /// The audio level during export, if available.
    /// </summary>
    private float? _exportLevel;

    /// <summary>
    /// Handle for the decode stream used during export (separate from playback stream).
    /// </summary>
    private int _exportDecodeStreamHandle;

    /// <summary>
    /// Indicates whether the stream is in export mode (should not play to speakers).
    /// </summary>
    private bool _isExportMode;

    /// <summary>
    /// Tracks the current playback position in seconds during export.
    /// This advances based on frame duration and playback speed.
    /// </summary>
    private double _exportPlaybackPosition;

    /// <summary>
    /// Private constructor to enforce factory method usage.
    /// </summary>
    private SpatialOperatorAudioStream() { }

    /// <summary>
    /// Converts a <see cref="Vector3"/> to a BASS <see cref="Vector3D"/>.
    /// </summary>
    private static Vector3D ToBassVector(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>
    /// Attempts to load a spatial audio stream from a file.
    /// </summary>
    /// <param name="filePath">The path to the audio file to load.</param>
    /// <param name="mixerHandle">Unused for spatial streams (kept for API compatibility).</param>
    /// <param name="stream">When successful, contains the created spatial audio stream.</param>
    /// <returns><c>true</c> if the stream was successfully loaded; otherwise, <c>false</c>.</returns>
    internal static bool TryLoadStream(string filePath, int mixerHandle, [NotNullWhen(true)] out SpatialOperatorAudioStream? stream)
    {
        stream = null;

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return false;

        // Create stream with 3D flag - this plays directly to BASS output, NOT through mixer
        // Must use Mono for proper 3D positioning, and Bass3D for hardware 3D processing
        var streamHandle = Bass.CreateStream(filePath, 0, 0,
            BassFlags.Float | BassFlags.Mono | BassFlags.Bass3D | BassFlags.AsyncFile);

        if (streamHandle == 0)
        {
            Log.Error($"[SpatialAudio] Error loading '{Path.GetFileName(filePath)}': {Bass.LastError}");
            return false;
        }

        Bass.ChannelGetAttribute(streamHandle, ChannelAttribute.Frequency, out var defaultFreq);
        var info = Bass.ChannelGetInfo(streamHandle);
        var bytes = Bass.ChannelGetLength(streamHandle);

        if (bytes <= 0)
        {
            Bass.StreamFree(streamHandle);
            return false;
        }

        var duration = Bass.ChannelBytes2Seconds(streamHandle, bytes);
        if (duration <= 0 || duration > 36000)
        {
            Bass.StreamFree(streamHandle);
            return false;
        }

        stream = new SpatialOperatorAudioStream
        {
            StreamHandle = streamHandle,
            _defaultPlaybackFrequency = defaultFreq,
            Duration = duration,
            FilePath = filePath,
            IsPlaying = false,
            IsPaused = false,
            _cachedFrequency = info.Frequency,
            IsStoppedDueToStale = true
        };

        // Initialize with volume at 0 - will be unmuted when Play() is triggered
        Bass.ChannelSetAttribute(streamHandle, ChannelAttribute.Volume, 0.0f);

        // Initialize 3D attributes
        stream.Initialize3DAudio();

        Log.Gated.Audio($"[SpatialAudio] Loaded: '{Path.GetFileName(filePath)}' ({info.Channels}ch, {info.Frequency}Hz, {duration:F2}s) [Native 3D]");
        return true;
    }

    /// <summary>
    /// Initializes the 3D audio attributes and position for this stream.
    /// </summary>
    private void Initialize3DAudio()
    {
        Bass.ChannelSet3DAttributes(StreamHandle, _3dMode, _minDistance, _maxDistance,
            (int)_innerAngleDegrees, (int)_outerAngleDegrees, _outerVolume);
        Bass.ChannelSet3DPosition(StreamHandle, ToBassVector(_position), ToBassVector(_orientation), ToBassVector(_velocity));
        AudioEngine.Mark3DApplyNeeded();
    }

    /// <summary>
    /// Updates the 3D position of the audio source and recalculates velocity.
    /// </summary>
    /// <param name="position">The new position of the audio source.</param>
    /// <param name="minDistance">The minimum distance for audio attenuation.</param>
    /// <param name="maxDistance">The maximum distance for audio attenuation.</param>
    internal void Update3DPosition(Vector3 position, float minDistance, float maxDistance)
    {
        var deltaPos = position - _position;
        _velocity = deltaPos * 60.0f; // Assume ~60fps
        _position = position;
        _minDistance = Math.Max(0.1f, minDistance);
        _maxDistance = Math.Max(_minDistance + 0.1f, maxDistance);

        // Calculate distance to listener for linear attenuation
        // (BASS's built-in rolloff is inverse distance, not linear - we apply our own linear falloff)
        var listenerPos = AudioEngine.Get3DListenerPosition();
        var distanceToListener = Vector3.Distance(_position, listenerPos);
        
        // Calculate linear attenuation: 1.0 at minDistance, 0.0 at maxDistance
        float newAttenuation;
        if (distanceToListener <= _minDistance)
        {
            newAttenuation = 1.0f;
        }
        else if (distanceToListener >= _maxDistance)
        {
            newAttenuation = 0.0f;
        }
        else
        {
            // Linear interpolation between min and max distance
            float range = _maxDistance - _minDistance;
            newAttenuation = 1.0f - ((distanceToListener - _minDistance) / range);
        }
        
        var wasBeyondMaxDistance = _isBeyondMaxDistance;
        _isBeyondMaxDistance = distanceToListener > _maxDistance;
        
        // Update volume if attenuation changed significantly or beyond-max state changed
        if (Math.Abs(newAttenuation - _distanceAttenuation) > 0.001f || _isBeyondMaxDistance != wasBeyondMaxDistance)
        {
            _distanceAttenuation = newAttenuation;
            ApplyEffectiveVolume();
        }

        Bass.ChannelSet3DAttributes(StreamHandle, _3dMode, _minDistance, _maxDistance,
            (int)_innerAngleDegrees, (int)_outerAngleDegrees, _outerVolume);
        Bass.ChannelSet3DPosition(StreamHandle, ToBassVector(_position), ToBassVector(_orientation), ToBassVector(_velocity));
        AudioEngine.Mark3DApplyNeeded();
    }

    /// <summary>
    /// Sets the 3D orientation direction of the audio source.
    /// </summary>
    /// <param name="orientation">The orientation vector (will be normalized).</param>
    internal void Set3DOrientation(Vector3 orientation)
    {
        _orientation = Vector3.Normalize(orientation);
        Bass.ChannelSet3DPosition(StreamHandle, ToBassVector(_position), ToBassVector(_orientation), ToBassVector(_velocity));
        AudioEngine.Mark3DApplyNeeded();
    }

    /// <summary>
    /// Sets the 3D sound cone parameters for directional audio.
    /// </summary>
    /// <param name="innerAngleDegrees">The inner cone angle in degrees (0-360).</param>
    /// <param name="outerAngleDegrees">The outer cone angle in degrees (0-360).</param>
    /// <param name="outerVolume">The volume level outside the outer cone (0.0 to 1.0).</param>
    internal void Set3DCone(float innerAngleDegrees, float outerAngleDegrees, float outerVolume)
    {
        _innerAngleDegrees = Math.Clamp(innerAngleDegrees, 0f, 360f);
        _outerAngleDegrees = Math.Clamp(outerAngleDegrees, 0f, 360f);
        _outerVolume = Math.Clamp(outerVolume, 0f, 1f);

        Bass.ChannelSet3DAttributes(StreamHandle, _3dMode, _minDistance, _maxDistance,
            (int)_innerAngleDegrees, (int)_outerAngleDegrees, _outerVolume);
        AudioEngine.Mark3DApplyNeeded();
    }

    /// <summary>
    /// Sets the 3D processing mode for the audio source.
    /// </summary>
    /// <param name="mode">The 3D mode to use.</param>
    internal void Set3DMode(Mode3D mode)
    {
        _3dMode = mode;
        Bass.ChannelSet3DAttributes(StreamHandle, _3dMode, _minDistance, _maxDistance,
            (int)_innerAngleDegrees, (int)_outerAngleDegrees, _outerVolume);
        AudioEngine.Mark3DApplyNeeded();
    }

    /// <summary>
    /// Starts playback of the audio stream.
    /// </summary>
    internal void Play()
    {
        IsStoppedDueToStale = false;
        IsPlaying = true;
        IsPaused = false;

        // During export mode, don't actually play to speakers - just track state for rendering
        if (_isExportMode)
        {
            // Reset export playback position and seek decode stream to beginning
            _exportPlaybackPosition = 0.0;
            if (_exportDecodeStreamHandle != 0)
            {
                Bass.ChannelSetPosition(_exportDecodeStreamHandle, 0);
            }
            return;
        }

        // For 3D streams, play directly to BASS (not through mixer)
        Bass.ChannelPlay(StreamHandle, true);

        // Apply volume after starting playback (respects user mute and max distance cutoff)
        ApplyEffectiveVolume();
        AudioEngine.Mark3DApplyNeeded();
    }

    /// <summary>
    /// Pauses playback of the audio stream.
    /// </summary>
    internal void Pause()
    {
        if (!IsPlaying || IsPaused) return;
        Bass.ChannelPause(StreamHandle);
        IsPaused = true;
    }

    /// <summary>
    /// Resumes playback of a paused audio stream.
    /// </summary>
    internal void Resume()
    {
        if (!IsPaused) return;
        IsPaused = false;

        // During export mode, don't actually play to speakers or apply 3D
        if (_isExportMode)
        {
            return;
        }

        Bass.ChannelPlay(StreamHandle);
        AudioEngine.Mark3DApplyNeeded();
    }

    /// <summary>
    /// Stops playback and resets the stream position to the beginning.
    /// </summary>
    internal void Stop()
    {
        IsPlaying = false;
        IsPaused = false;
        IsStoppedDueToStale = false;
        Bass.ChannelPause(StreamHandle);
        Bass.ChannelSetPosition(StreamHandle, 0);
    }

    /// <summary>
    /// Sets the stale state of the stream. Stale streams are stopped and reset to the beginning.
    /// </summary>
    /// <param name="stale">Whether the stream should be stopped due to being stale.</param>
    /// <param name="reason">Optional reason for the stale state change (for debugging).</param>
    internal void SetStale(bool stale, string reason = "")
    {
        if (IsStoppedDueToStale == stale) return;
        IsStoppedDueToStale = stale;

        if (stale)
        {
            // Stop playback and seek to beginning when becoming stale
            Bass.ChannelPause(StreamHandle);
            Bass.ChannelSetPosition(StreamHandle, 0);
            IsPlaying = false;
            IsPaused = false;
        }
        else
        {
            ApplyEffectiveVolume();
        }
    }

    /// <summary>
    /// Sets the volume and mute state of the stream.
    /// </summary>
    /// <param name="volume">The volume level (0.0 to 1.0).</param>
    /// <param name="mute">Whether the stream should be muted.</param>
    internal void SetVolume(float volume, bool mute)
    {
        _currentVolume = volume;
        _isUserMuted = mute;

        if (!IsPlaying) return;

        ApplyEffectiveVolume();
    }

    /// <summary>
    /// Applies the effective volume considering all mute states and distance attenuation.
    /// </summary>
    private void ApplyEffectiveVolume()
    {
        if (!IsPlaying) return;
        
        // During export mode, always keep volume at 0 to prevent audio going to speakers
        if (_isExportMode)
        {
            Bass.ChannelSetAttribute(StreamHandle, ChannelAttribute.Volume, 0.0f);
            return;
        }
        
        // Mute if: user muted, stale stopped, or beyond max distance
        if (_isUserMuted || IsStoppedDueToStale || _isBeyondMaxDistance)
        {
            Bass.ChannelSetAttribute(StreamHandle, ChannelAttribute.Volume, 0.0f);
            return;
        }
        
        // Apply linear distance attenuation on top of user volume
        // This provides proper linear falloff between minDistance and maxDistance
        float finalVolume = _currentVolume * _distanceAttenuation;
        Bass.ChannelSetAttribute(StreamHandle, ChannelAttribute.Volume, finalVolume);
    }

    /// <summary>
    /// Sets the playback speed of the stream.
    /// </summary>
    /// <param name="speed">The playback speed multiplier (clamped between 0.1 and 4.0).</param>
    internal void SetSpeed(float speed)
    {
        var clampedSpeed = Math.Clamp(speed, 0.1f, 4f);
        if (Math.Abs(clampedSpeed - _currentSpeed) < 0.001f) return;

        _currentSpeed = clampedSpeed;
        
        // During export mode, we don't modify BASS - speed is handled in RenderAudio
        if (_isExportMode)
        {
            return;
        }

        // For live playback, adjust BASS frequency for speed change
        var newFreq = _defaultPlaybackFrequency * clampedSpeed;
        Bass.ChannelSetAttribute(StreamHandle, ChannelAttribute.Frequency, newFreq);
    }

    /// <summary>
    /// Seeks to a specific position in the audio stream.
    /// </summary>
    /// <param name="timeInSeconds">The position to seek to, in seconds.</param>
    internal void Seek(float timeInSeconds)
    {
        // Update export playback position if in export mode
        if (_isExportMode)
        {
            _exportPlaybackPosition = timeInSeconds;
            if (_exportDecodeStreamHandle != 0)
            {
                var position = Bass.ChannelSeconds2Bytes(_exportDecodeStreamHandle, timeInSeconds);
                Bass.ChannelSetPosition(_exportDecodeStreamHandle, position);
            }
            return;
        }
        
        var position2 = Bass.ChannelSeconds2Bytes(StreamHandle, timeInSeconds);
        Bass.ChannelSetPosition(StreamHandle, position2);
    }

    /// <summary>
    /// Gets the current audio level of the stream for metering purposes.
    /// </summary>
    /// <returns>The peak audio level (0.0 to 1.0).</returns>
    internal float GetLevel()
    {
        if (_exportLevel.HasValue) return _exportLevel.Value;
        if (!IsPlaying || (IsPaused && !IsStoppedDueToStale)) return 0f;

        var level = Bass.ChannelGetLevel(StreamHandle);
        if (level == -1) return 0f;

        var left = (level & 0xFFFF) / 32768f;
        var right = ((level >> 16) & 0xFFFF) / 32768f;
        return Math.Min(Math.Max(left, right), 1f);
    }

    /// <summary>
    /// Gets the current playback position of the stream.
    /// </summary>
    /// <returns>The current position in seconds.</returns>
    public double GetCurrentPosition()
    {
        long positionBytes = Bass.ChannelGetPosition(StreamHandle);
        if (positionBytes < 0) positionBytes = 0;
        return Bass.ChannelBytes2Seconds(StreamHandle, positionBytes);
    }

    #region Export Support

    /// <summary>
    /// Prepares the stream for export by pausing playback and creating a decode stream for rendering.
    /// </summary>
    internal void PrepareForExport()
    {
        _isExportMode = true;
        _exportPlaybackPosition = 0.0;
        IsPlaying = false;
        IsPaused = false;
        IsStoppedDueToStale = true;

        // Mute and pause the playback stream - prevent any audio going to speakers
        Bass.ChannelSetAttribute(StreamHandle, ChannelAttribute.Volume, 0.0f);
        Bass.ChannelPause(StreamHandle);
        Bass.ChannelSetPosition(StreamHandle, 0);

        // Create a separate decode stream for export rendering
        // This allows us to read audio data without affecting the 3D playback stream
        _exportDecodeStreamHandle = Bass.CreateStream(FilePath, 0, 0,
            BassFlags.Decode | BassFlags.Float | BassFlags.Mono);

        if (_exportDecodeStreamHandle == 0)
        {
            Log.Warning($"[SpatialAudio] Failed to create export decode stream: {Bass.LastError}");
        }

        ClearExportMetering();
    }

    /// <summary>
    /// Restarts the stream after an export operation, resetting position and restoring playback state.
    /// </summary>
    internal void RestartAfterExport()
    {
        _isExportMode = false;
        IsStoppedDueToStale = false;

        // Free the export decode stream
        if (_exportDecodeStreamHandle != 0)
        {
            Bass.StreamFree(_exportDecodeStreamHandle);
            _exportDecodeStreamHandle = 0;
        }

        // Reset playback stream position
        Bass.ChannelSetPosition(StreamHandle, 0);

        // Restart playback
        Bass.ChannelPlay(StreamHandle);

        if (!_isUserMuted)
            Bass.ChannelSetAttribute(StreamHandle, ChannelAttribute.Volume, _currentVolume);

        IsPlaying = true;
        IsPaused = false;
        AudioEngine.Mark3DApplyNeeded();
    }

    /// <summary>
    /// Updates the export level metering from an audio buffer.
    /// </summary>
    /// <param name="buffer">The audio buffer containing sample data.</param>
    internal void UpdateFromBuffer(float[] buffer)
    {
        float peak = buffer.Select(Math.Abs).Prepend(0f).Max();
        _exportLevel = Math.Min(peak, 1f);
    }

    /// <summary>
    /// Clears the export metering level.
    /// </summary>
    internal void ClearExportMetering()
    {
        _exportLevel = null;
    }

    /// <summary>
    /// Renders audio data from this stream into an output buffer with resampling support.
    /// During export, this computes 3D attenuation and panning manually since hardware 3D
    /// is not available for decode streams.
    /// </summary>
    /// <param name="startTime">The start time in seconds (unused - we track position internally).</param>
    /// <param name="duration">The duration to render in seconds.</param>
    /// <param name="outputBuffer">The buffer to write the rendered audio data to.</param>
    /// <param name="targetSampleRate">The target sample rate for the output.</param>
    /// <param name="targetChannels">The target number of channels for the output.</param>
    /// <returns>The number of samples written to the output buffer.</returns>
    public int RenderAudio(double startTime, double duration, float[] outputBuffer, int targetSampleRate, int targetChannels)
    {
        // If muted, output silence
        if (_isUserMuted)
        {
            Array.Clear(outputBuffer, 0, outputBuffer.Length);
            return outputBuffer.Length;
        }
        
        int nativeSampleRate = _cachedFrequency > 0 ? _cachedFrequency : 44100;
        int nativeChannels = 1; // Always mono for 3D audio

        // Use the export decode stream for rendering
        int streamToUse = _exportDecodeStreamHandle != 0 ? _exportDecodeStreamHandle : StreamHandle;

        // Calculate 3D attenuation and panning for export
        var listenerPos = AudioEngine.Get3DListenerPosition();
        var listenerForward = AudioEngine.Get3DListenerForward();
        
        float distanceAttenuation = Compute3DAttenuation(listenerPos);
        float coneAttenuation = Compute3DConeAttenuation(listenerPos);
        float pan = Compute3DPanning(listenerPos, listenerForward);

        // Apply playback speed to the duration of audio we need to read from the source
        // e.g., at 2x speed, we need 2x the source audio for the same output duration
        double sourceDuration = duration * _currentSpeed;
        
        // Check if we've reached the end of the audio
        if (_exportPlaybackPosition >= Duration)
        {
            // Past end of stream, output silence
            Array.Clear(outputBuffer, 0, outputBuffer.Length);
            return outputBuffer.Length;
        }

        // Seek the decode stream to the current export playback position
        if (streamToUse != 0)
        {
            long positionBytes = Bass.ChannelSeconds2Bytes(streamToUse, _exportPlaybackPosition);
            Bass.ChannelSetPosition(streamToUse, positionBytes, PositionFlags.Bytes);
        }

        // Use modified sample rate to achieve speed change during resampling
        // Reading at (nativeSampleRate / speed) and outputting at targetSampleRate achieves speed change
        int effectiveSampleRate = (int)(nativeSampleRate / _currentSpeed);
        
        // Fill and resample the output buffer from the decode stream
        OperatorAudioUtils.FillAndResample(
            (start, dur, buffer) => RenderNativeAudio(streamToUse, buffer),
            startTime, sourceDuration, outputBuffer,
            nativeSampleRate, nativeChannels, targetSampleRate, targetChannels);

        // Advance the export playback position by the source duration consumed
        _exportPlaybackPosition += sourceDuration;

        // Apply user volume, distance attenuation, cone attenuation, and panning to the output buffer
        // Combined volume = user volume * distance attenuation * cone attenuation
        float combinedAttenuation = _currentVolume * distanceAttenuation * coneAttenuation;
        Apply3DToBuffer(outputBuffer, targetChannels, combinedAttenuation, pan);

        return outputBuffer.Length;
    }

    /// <summary>
    /// Computes the 3D distance attenuation factor.
    /// </summary>
    private float Compute3DAttenuation(Vector3 listenerPos)
    {
        float distance = Vector3.Distance(_position, listenerPos);
        
        if (distance <= _minDistance)
            return 1.0f;
        
        if (distance >= _maxDistance)
            return 0.0f;
        
        // Linear rolloff between min and max distance
        float range = _maxDistance - _minDistance;
        float normalizedDistance = (distance - _minDistance) / range;
        
        // Apply rolloff factor from config
        float attenuation = 1.0f - (normalizedDistance * AudioConfig.RolloffFactor);
        return Math.Clamp(attenuation, 0.0f, 1.0f);
    }

    /// <summary>
    /// Computes the stereo panning value based on 3D position relative to listener.
    /// </summary>
    private float Compute3DPanning(Vector3 listenerPos, Vector3 listenerForward)
    {
        var toSource = _position - listenerPos;
        if (toSource.Length() < 0.001f)
            return 0.0f; // Source at listener position, center pan

        toSource = Vector3.Normalize(toSource);
        
        // Calculate right vector from forward (assuming Y is up)
        var listenerRight = Vector3.Cross(listenerForward, new Vector3(0, 1, 0));
        if (listenerRight.Length() < 0.001f)
            listenerRight = new Vector3(1, 0, 0); // Fallback
        else
            listenerRight = Vector3.Normalize(listenerRight);

        // Dot product with right vector gives pan (-1 = left, 1 = right)
        float pan = Vector3.Dot(toSource, listenerRight);
        return Math.Clamp(pan, -1.0f, 1.0f);
    }

    /// <summary>
    /// Computes the cone attenuation factor based on the angle between the source orientation
    /// and the direction to the listener.
    /// </summary>
    private float Compute3DConeAttenuation(Vector3 listenerPos)
    {
        // If cone angles are 360 degrees, no cone attenuation (omnidirectional)
        if (_innerAngleDegrees >= 360.0f && _outerAngleDegrees >= 360.0f)
            return 1.0f;

        // Calculate direction from source to listener
        var toListener = listenerPos - _position;
        if (toListener.Length() < 0.001f)
            return 1.0f; // Listener at source position, full volume

        toListener = Vector3.Normalize(toListener);

        // Calculate angle between source orientation and direction to listener
        // _orientation points in the direction the source is "facing"
        float dotProduct = Vector3.Dot(_orientation, toListener);
        float angleRadians = MathF.Acos(Math.Clamp(dotProduct, -1.0f, 1.0f));
        float angleDegrees = angleRadians * (180.0f / MathF.PI);

        // Convert half-angles (BASS uses full cone angles, but we compare against half)
        float innerHalfAngle = _innerAngleDegrees / 2.0f;
        float outerHalfAngle = _outerAngleDegrees / 2.0f;

        if (angleDegrees <= innerHalfAngle)
        {
            // Inside inner cone - full volume
            return 1.0f;
        }
        else if (angleDegrees >= outerHalfAngle)
        {
            // Outside outer cone - outer volume
            return _outerVolume;
        }
        else
        {
            // Between inner and outer cone - interpolate
            float range = outerHalfAngle - innerHalfAngle;
            float t = (angleDegrees - innerHalfAngle) / range;
            return 1.0f + t * (_outerVolume - 1.0f);
        }
    }

    /// <summary>
    /// Applies 3D attenuation and panning to an output buffer.
    /// Uses equal-power panning for perceptually accurate stereo positioning.
    /// </summary>
    private static void Apply3DToBuffer(float[] buffer, int channels, float attenuation, float pan)
    {
        if (channels == 2)
        {
            // Equal-power (constant-power) panning for perceptually accurate stereo
            // pan: -1 = full left, 0 = center, 1 = full right
            // Convert pan range [-1, 1] to angle [0, PI/2]
            float angle = (pan + 1.0f) * 0.25f * MathF.PI; // 0 to PI/2
            
            // Equal power: left = cos(angle), right = sin(angle)
            // At center (pan=0, angle=PI/4): left = right = 0.707 (equal)
            // At full left (pan=-1, angle=0): left = 1, right = 0
            // At full right (pan=1, angle=PI/2): left = 0, right = 1
            float leftGain = attenuation * MathF.Cos(angle);
            float rightGain = attenuation * MathF.Sin(angle);
            
            for (int i = 0; i < buffer.Length; i += 2)
            {
                buffer[i] *= leftGain;
                if (i + 1 < buffer.Length)
                    buffer[i + 1] *= rightGain;
            }
        }
        else
        {
            // Mono or multi-channel: just apply attenuation
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] *= attenuation;
            }
        }
    }

    /// <summary>
    /// Renders audio data in the native format of the stream.
    /// The stream should already be seeked to the correct position before calling.
    /// </summary>
    private static int RenderNativeAudio(int streamHandle, float[] buffer)
    {
        int bytesToRead = buffer.Length * sizeof(float);
        int bytesRead = Bass.ChannelGetData(streamHandle, buffer, bytesToRead);
        return bytesRead > 0 ? bytesRead / sizeof(float) : 0;
    }

    #endregion

    /// <summary>
    /// Disposes of the audio stream, releasing all BASS resources.
    /// </summary>
    internal void Dispose()
    {
        Bass.ChannelStop(StreamHandle);
        Bass.StreamFree(StreamHandle);
        
        if (_exportDecodeStreamHandle != 0)
        {
            Bass.StreamFree(_exportDecodeStreamHandle);
            _exportDecodeStreamHandle = 0;
        }
    }
}
