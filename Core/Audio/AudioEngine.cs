#nullable enable
using System;
using System.Collections.Generic;
using ManagedBass;
using T3.Core.Animation;
using T3.Core.IO;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Resource.Assets;
// ReSharper disable MergeIntoPattern

namespace T3.Core.Audio;

/// <summary>
/// Controls loading, playback, and discarding of audio clips.
/// </summary>
public static class AudioEngine
{
    // --- Soundtrack (Timeline) Audio ---
    internal static readonly Dictionary<AudioClipResourceHandle, SoundtrackClipStream> SoundtrackClipStreams = new();
    private static readonly Dictionary<AudioClipResourceHandle, double> _updatedSoundtrackClipTimes = new();
    private static readonly List<AudioClipResourceHandle> _obsoleteSoundtrackHandles = new();

    // --- Operator Audio ---
    private static readonly Dictionary<Guid, OperatorAudioState<StereoOperatorAudioStream>> _stereoOperatorStates = new();
    private static readonly Dictionary<Guid, SpatialOperatorState> _spatialOperatorStates = new();
    
    /// <summary>
    /// Internal monotonic frame token for stale detection.
    /// Incremented at the start of each new frame (detected via Playback.FrameCount change).
    /// </summary>
    private static long _audioFrameToken;
    
    /// <summary>
    /// Tracks the last seen Playback.FrameCount to detect when a new frame has started.
    /// When this differs from current Playback.FrameCount, we increment _audioFrameToken.
    /// </summary>
    private static int _lastSeenPlaybackFrame = -1;
    
    /// <summary>
    /// Tracks whether stale check has been performed for the current frame token.
    /// </summary>
    private static long _lastStaleCheckFrameToken = -1;
    
    // Export state

    /// <summary>
    /// Gets whether the audio engine is currently in export mode.
    /// During export, streams should not be paused when marked stale.
    /// </summary>
    internal static bool IsExporting { get; private set; }

    // 3D Listener
    private static Vector3 _listenerPosition = Vector3.Zero;
    private static Vector3 _listenerVelocity = Vector3.Zero;
    private static Vector3 _listenerForward = new(0, 0, 1);
    private static Vector3 _listenerUp = new(0, 1, 0);
    private static bool _3dInitialized;
    private static bool _3dListenerDirty;
    private static bool _3dApplyNeeded;

    private static double _lastPlaybackSpeed = 1;
    private static bool _bassInitialized;
    private static bool _bassInitFailed;

    /// <summary>
    /// Common state for operator audio streams using mixer (stereo).
    /// </summary>
    private sealed class OperatorAudioState<T> where T : OperatorAudioStreamBase
    {
        public T? Stream;
        public string CurrentFilePath = string.Empty;
        public bool IsPaused;
        public bool PreviousPlay;
        public bool PreviousStop;
        public bool IsStale;
        
        /// <summary>
        /// The pending normalized seek position (0.0 to 1.0) to apply on the next play trigger.
        /// Seek changes are stored here but not applied until playback is triggered.
        /// </summary>
        public float PendingSeek;
        
        /// <summary>
        /// The frame token when this operator was last updated.
        /// Used for stale detection - if this doesn't match the current _audioFrameToken,
        /// the operator is considered stale.
        /// </summary>
        public long LastUpdatedFrameId = -1;
        
        /// <summary>
        /// The file path that previously failed to load, if any.
        /// Used to avoid repeated load attempts and error logging for the same invalid file.
        /// </summary>
        public string? FailedFilePath;
        
        /// <summary>
        /// The error message from the last failed load attempt.
        /// Cleared when a new file path is set or when explicitly cleared.
        /// </summary>
        public string? LastLoadError;
    }

    /// <summary>
    /// State for spatial operator audio streams (native 3D, not using mixer).
    /// </summary>
    private sealed class SpatialOperatorState
    {
        public SpatialOperatorAudioStream? Stream;
        public string CurrentFilePath = string.Empty;
        public bool IsPaused;
        public bool PreviousPlay;
        public bool PreviousStop;
        public bool IsStale;
        
        /// <summary>
        /// The pending normalized seek position (0.0 to 1.0) to apply on the next play trigger.
        /// Seek changes are stored here but not applied until playback is triggered.
        /// </summary>
        public float PendingSeek;
        
        /// <summary>
        /// The frame token when this operator was last updated.
        /// Used for stale detection - if this doesn't match the current _audioFrameToken,
        /// the operator is considered stale.
        /// </summary>
        public long LastUpdatedFrameId = -1;
        
        /// <summary>
        /// The file path that previously failed to load, if any.
        /// Used to avoid repeated load attempts and error logging for the same invalid file.
        /// </summary>
        public string? FailedFilePath;
        
        /// <summary>
        /// The error message from the last failed load attempt.
        /// Cleared when a new file path is set or when explicitly cleared.
        /// </summary>
        public string? LastLoadError;
    }

    #region Soundtrack Management

    /// <summary>
    /// Registers a soundtrack clip to be used at the specified time during the current frame.
    /// </summary>
    /// <param name="handle">The audio clip resource handle.</param>
    /// <param name="time">The playback time in seconds.</param>
    public static void UseSoundtrackClip(AudioClipResourceHandle handle, double time)
    {
        _updatedSoundtrackClipTimes[handle] = time;
    }

    /// <summary>
    /// Reloads a soundtrack clip by freeing the existing stream and re-registering it.
    /// </summary>
    /// <param name="handle">The audio clip resource handle to reload.</param>
    public static void ReloadSoundtrackClip(AudioClipResourceHandle handle)
    {
        if (SoundtrackClipStreams.TryGetValue(handle, out var stream))
        {
            Bass.StreamFree(stream.StreamHandle);
            SoundtrackClipStreams.Remove(handle);
        }
        UseSoundtrackClip(handle, 0);
    }

    /// <summary>
    /// Completes the audio processing for the current frame, handling soundtrack clips,
    /// FFT analysis, and stale operator detection.
    /// </summary>
    /// <param name="playback">The current playback state.</param>
    /// <param name="frameDurationInSeconds">The duration of the current frame in seconds.</param>
    public static void CompleteFrame(Playback playback, double frameDurationInSeconds)
    {
        EnsureBassInitialized();

        ProcessSoundtrackClips(playback, frameDurationInSeconds);

        // Process FFT data after filling the buffer from soundtrack
        // Skip during export - GetFullMixDownBuffer handles FFT processing during export
        // to ensure consistent behavior between soundtrack and external audio modes
        if (!playback.IsRenderingToFile && 
            playback.Settings is { Enabled: true, AudioSource: PlaybackSettings.AudioSources.ProjectSoundTrack })
            AudioAnalysis.ProcessUpdate(playback.Settings.AudioGainFactor, playback.Settings.AudioDecayFactor);

        StopStaleOperators();
        
        // Ensure the frame token is incremented even when no audio operators update.
        // This must be called AFTER StopStaleOperators() so stale detection compares
        // against the previous frame's token, not the current one.
        EnsureFrameTokenCurrent();
        
        // Apply all 3D audio changes once per frame (batched for performance)
        Apply3DChanges();

        _obsoleteSoundtrackHandles.Clear();
        _updatedSoundtrackClipTimes.Clear();
    }

    private static void EnsureBassInitialized()
    {
        if (_bassInitialized || _bassInitFailed) return;

        AudioMixerManager.Initialize();
        if (AudioMixerManager.OperatorMixerHandle == 0)
        {
            Log.Error("[AudioEngine] Failed to initialize AudioMixerManager; audio disabled.");
            _bassInitFailed = true;
            return;
        }

        _bassInitialized = true;
        InitializeGlobalVolumeFromSettings();
    }

    private static void ProcessSoundtrackClips(Playback playback, double frameDurationInSeconds)
    {
        // Note: During export, both soundtrack and external audio modes follow the same code path here.
        // The only difference is whether soundtrack audio is mixed in GetFullMixDownBuffer.
        // This ensures consistent behavior for FFT/metering between modes.

        foreach (var (handle, time) in _updatedSoundtrackClipTimes)
        {
            if (SoundtrackClipStreams.TryGetValue(handle, out var clip))
            {
                clip.TargetTime = time;
            }
            else if (!string.IsNullOrEmpty(handle.Clip.FilePath) && 
                     SoundtrackClipStream.TryLoadSoundtrackClip(handle, out var soundtrackClipStream))
            {
                SoundtrackClipStreams[handle] = soundtrackClipStream;
            }
        }

        var playbackSpeedChanged = Math.Abs(_lastPlaybackSpeed - playback.PlaybackSpeed) > 0.001f;
        _lastPlaybackSpeed = playback.PlaybackSpeed;

        var handledMainSoundtrack = false;
        foreach (var (handle, clipStream) in SoundtrackClipStreams)
        {
            clipStream.IsInUse = _updatedSoundtrackClipTimes.ContainsKey(clipStream.ResourceHandle);
            if (!clipStream.IsInUse && clipStream.ResourceHandle.Clip.DiscardAfterUse)
            {
                _obsoleteSoundtrackHandles.Add(handle);
                continue;
            }

            if (!playback.IsRenderingToFile && playbackSpeedChanged)
                clipStream.UpdateSoundtrackPlaybackSpeed(playback.PlaybackSpeed);

            if (handledMainSoundtrack || !clipStream.ResourceHandle.Clip.IsSoundtrack)
                continue;

            handledMainSoundtrack = true;

            if (playback.IsRenderingToFile)
                AudioRendering.ExportAudioFrame(playback, frameDurationInSeconds, clipStream);
            else
            {
                UpdateFftBufferFromSoundtrack(playback);
                clipStream.UpdateSoundtrackTime(playback);
            }
        }

        foreach (var handle in _obsoleteSoundtrackHandles)
        {
            SoundtrackClipStreams[handle].DisableSoundtrackStream();
            SoundtrackClipStreams.Remove(handle);
        }
        
        // Always update FFT buffer from mixer when in soundtrack mode, even if no soundtrack is loaded.
        // This ensures audio metering operators (AudioWaveform, PlaybackFFT, AudioReaction, etc.)
        // continue to work with operator-generated audio when no soundtrack is loaded.
        if (!handledMainSoundtrack && !playback.IsRenderingToFile)
        {
            UpdateFftBufferFromSoundtrack(playback);
        }
    }

    /// <summary>
    /// Sets the mute state for the soundtrack audio.
    /// </summary>
    /// <param name="configSoundtrackMute">True to mute the soundtrack, false to unmute.</param>
    public static void SetSoundtrackMute(bool configSoundtrackMute) => IsSoundtrackMuted = configSoundtrackMute;
    
    /// <summary>
    /// Gets a value indicating whether the soundtrack is currently muted.
    /// </summary>
    public static bool IsSoundtrackMuted { get; private set; }
    
    /// <summary>
    /// Gets a value indicating whether global audio is currently muted.
    /// </summary>
    public static bool IsGlobalMuted => ProjectSettings.Config.GlobalMute;

    internal static void UpdateFftBufferFromSoundtrack(Playback playback)
    {
        UpdateFftBufferFromSoundtrack(playback, AudioAnalysisContext.Default);
    }

    /// <summary>
    /// Updates FFT and waveform buffers from the soundtrack mixer.
    /// </summary>
    /// <param name="playback">The current playback state</param>
    /// <param name="context">The analysis context to write data into</param>
    internal static void UpdateFftBufferFromSoundtrack(Playback playback, AudioAnalysisContext context)
    {
        if (playback.Settings is not { AudioSource: PlaybackSettings.AudioSources.ProjectSoundTrack })
            return;

        // During export, the GlobalMixer is paused and empty, so FFT/waveform data
        // is populated via WaveFormProcessing.PopulateFromExportBuffer() in AudioRendering.GetFullMixDownBuffer()
        if (playback.IsRenderingToFile)
            return;

        // Get FFT data from the GlobalMixer to include both soundtrack AND operator audio
        // This ensures all audio metering (AudioWaveform, AudioFrequencies, AudioReaction, etc.)
        // monitors the complete audio output including operator-generated sounds
        var mixerHandle = AudioMixerManager.GlobalMixerHandle;
        if (mixerHandle == 0)
            return;

        const int dataFlags = (int)DataFlags.FFT2048;
        _ = Bass.ChannelGetData(mixerHandle, context.FftGainBuffer, dataFlags);

        if (!context.WaveformRequested)
            return;

        int lengthInBytes = AudioConfig.WaveformSampleCount << 2 << 1;
        context.LastWaveformFetchResult = Bass.ChannelGetData(mixerHandle,
            context.InterleavedSampleBuffer, lengthInBytes);
    }

    /// <summary>
    /// Gets the number of audio channels for a soundtrack clip.
    /// </summary>
    /// <param name="handle">The audio clip resource handle.</param>
    /// <returns>The number of channels, or 2 (stereo) if the clip is not found.</returns>
    public static int GetClipChannelCount(AudioClipResourceHandle? handle)
    {
        if (handle == null || !SoundtrackClipStreams.TryGetValue(handle, out var clipStream))
            return 2;
        Bass.ChannelGetInfo(clipStream.StreamHandle, out var info);
        return info.Channels;
    }

    /// <summary>
    /// Gets the sample rate for a soundtrack clip.
    /// </summary>
    /// <param name="clip">The audio clip resource handle.</param>
    /// <returns>The sample rate in Hz, or 48000 if the clip is not found.</returns>
    public static int GetClipSampleRate(AudioClipResourceHandle? clip)
    {
        if (clip == null || !SoundtrackClipStreams.TryGetValue(clip, out var stream))
            return 48000;
        Bass.ChannelGetInfo(stream.StreamHandle, out var info);
        return info.Frequency;
    }

    #endregion

    #region 3D Listener

    /// <summary>
    /// Converts a System.Numerics.Vector3 to a ManagedBass.Vector3D.
    /// </summary>
    private static Vector3D ToBassVector(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>
    /// Sets the 3D listener position and orientation for spatial audio.
    /// </summary>
    /// <param name="position">The world position of the listener.</param>
    /// <param name="forward">The forward direction vector of the listener.</param>
    /// <param name="up">The up direction vector of the listener.</param>
    public static void Set3DListenerPosition(Vector3 position, Vector3 forward, Vector3 up)
    {
        // Compute velocity from position delta (assumes ~60fps, will be refined by actual frame time)
        var deltaPos = position - _listenerPosition;
        _listenerVelocity = deltaPos * 60.0f;
        
        _listenerPosition = position;
        _listenerForward = forward;
        _listenerUp = up;
        _3dListenerDirty = true;

        if (!_3dInitialized)
        {
            Initialize3DAudio();
            _3dInitialized = true;
            Log.Gated.Audio($"[AudioEngine] 3D audio initialized | Pos: {position}");
        }
    }

    /// <summary>
    /// Initializes BASS 3D audio with configured factors.
    /// </summary>
    private static void Initialize3DAudio()
    {
        // Set 3D factors for proper world scaling
        Bass.Set3DFactors(
            AudioConfig.DistanceFactor,
            AudioConfig.RolloffFactor,
            AudioConfig.DopplerFactor);
        
        // Apply initial listener position
        ApplyListenerPosition();
    }

    /// <summary>
    /// Applies the current listener position to BASS.
    /// </summary>
    private static void ApplyListenerPosition()
    {
        var pos = ToBassVector(_listenerPosition);
        var vel = ToBassVector(_listenerVelocity);
        var front = ToBassVector(_listenerForward);
        var top = ToBassVector(_listenerUp);
        
        Bass.Set3DPosition(pos, vel, front, top);
        _3dListenerDirty = false;
    }

    /// <summary>
    /// Marks that a 3D apply is needed at the end of the frame.
    /// Called by spatial streams when their positions are updated.
    /// </summary>
    internal static void Mark3DApplyNeeded() => _3dApplyNeeded = true;

    /// <summary>
    /// Applies all pending 3D changes (called once per frame in CompleteFrame).
    /// </summary>
    private static void Apply3DChanges()
    {
        if (_3dListenerDirty)
        {
            ApplyListenerPosition();
        }
        
        if (_3dApplyNeeded || _3dListenerDirty)
        {
            Bass.Apply3D();
            _3dApplyNeeded = false;
        }
    }

    /// <summary>
    /// Gets the current 3D listener position.
    /// </summary>
    /// <returns>The world position of the listener.</returns>
    public static Vector3 Get3DListenerPosition() => _listenerPosition;
    
    /// <summary>
    /// Gets the current 3D listener forward direction.
    /// </summary>
    /// <returns>The forward direction vector of the listener.</returns>
    public static Vector3 Get3DListenerForward() => _listenerForward;
    
    /// <summary>
    /// Gets the current 3D listener up direction.
    /// </summary>
    /// <returns>The up direction vector of the listener.</returns>
    public static Vector3 Get3DListenerUp() => _listenerUp;

    #endregion

    #region Stereo Operator Playback

    /// <summary>
    /// Updates the playback state of a stereo audio stream for an operator.
    /// </summary>
    /// <param name="operatorId">The unique identifier of the operator.</param>
    /// <param name="filePath">The file path of the audio file to play.</param>
    /// <param name="shouldPlay">True to trigger playback on rising edge.</param>
    /// <param name="shouldStop">True to trigger stop on rising edge.</param>
    /// <param name="volume">The volume level (0.0 to 1.0).</param>
    /// <param name="mute">True to mute the stream.</param>
    /// <param name="panning">The stereo panning value (-1.0 left to 1.0 right).</param>
    /// <param name="speed">The playback speed multiplier (default 1.0).</param>
    /// <param name="seek">
    /// The normalized seek position (0.0 to 1.0). This value is stored and only applied when 
    /// playback is triggered via <paramref name="shouldPlay"/>. Changing the seek value during 
    /// playback has no effect until the next play trigger. This allows setting the seek position 
    /// and triggering play in the same frame for predictable behavior.
    /// </param>
    public static void UpdateStereoOperatorPlayback(
        Guid operatorId, string filePath, bool shouldPlay, bool shouldStop,
        float volume, bool mute, float panning, float speed = 1.0f, float seek = 0f)
    {
        EnsureFrameTokenCurrent();
        
        if (!EnsureMixerInitialized()) return;

        var state = GetOrCreateState(_stereoOperatorStates, operatorId);
        
        // Mark this operator as updated for this frame (used for stale detection)
        state.LastUpdatedFrameId = _audioFrameToken;
        
        var resolvedPath = ResolveFilePath(filePath);

        if (!HandleFileChange(state, resolvedPath, operatorId,
            path => StereoOperatorAudioStream.TryLoadStream(path, AudioMixerManager.OperatorMixerHandle, out var s) ? s : null))
            return;

        if (state.Stream == null) return;

        // Store the pending seek position - will be applied on next play trigger
        if (seek >= 0f && seek <= 1f)
            state.PendingSeek = seek;

        if (HandlePlaybackTriggers(state, shouldPlay, shouldStop, operatorId))
            return;

        if (state.Stream.IsPlaying)
        {
            state.Stream.SetVolume(volume, mute);
            state.Stream.SetPanning(panning);
            state.Stream.SetSpeed(speed);
        }
    }

    /// <summary>Pauses the audio stream for the specified stereo operator.</summary>
    /// <param name="operatorId">The unique identifier of the operator.</param>
    public static void PauseOperator(Guid operatorId) => PauseOperatorInternal(_stereoOperatorStates, operatorId);
    
    /// <summary>Resumes the audio stream for the specified stereo operator.</summary>
    /// <param name="operatorId">The unique identifier of the operator.</param>
    public static void ResumeOperator(Guid operatorId) => ResumeOperatorInternal(_stereoOperatorStates, operatorId);
    
    /// <summary>Checks if the audio stream is currently playing for the specified stereo operator.</summary>
    /// <param name="operatorId">The unique identifier of the operator.</param>
    /// <returns>True if the stream is playing and not paused; otherwise, false.</returns>
    public static bool IsOperatorStreamPlaying(Guid operatorId) => IsOperatorPlaying(_stereoOperatorStates, operatorId);
    
    /// <summary>Checks if the audio stream is currently paused for the specified stereo operator.</summary>
    /// <param name="operatorId">The unique identifier of the operator.</param>
    /// <returns>True if the stream is paused; otherwise, false.</returns>
    public static bool IsOperatorPaused(Guid operatorId) => IsOperatorPausedInternal(_stereoOperatorStates, operatorId);
    
    /// <summary>Gets the current audio level for the specified stereo operator.</summary>
    /// <param name="operatorId">The unique identifier of the operator.</param>
    /// <returns>The current audio level, or 0 if the stream is not found.</returns>
    public static float GetOperatorLevel(Guid operatorId) => GetOperatorLevelInternal(_stereoOperatorStates, operatorId);

    /// <summary>
    /// Attempts to retrieve the stereo audio stream for the specified operator.
    /// </summary>
    /// <param name="operatorId">The unique identifier of the operator.</param>
    /// <param name="stream">When this method returns, contains the stream if found; otherwise, null.</param>
    /// <returns>True if the stream was found; otherwise, false.</returns>
    public static bool TryGetStereoOperatorStream(Guid operatorId, out StereoOperatorAudioStream? stream)
    {
        stream = null;
        if (_stereoOperatorStates.TryGetValue(operatorId, out var state) && state.Stream != null)
        {
            stream = state.Stream;
            return true;
        }
        return false;
    }

    #endregion

    #region Spatial Operator Playback

    /// <summary>
    /// Updates the playback state of a spatial (3D) audio stream for an operator.
    /// Spatial streams play directly to BASS output for hardware-accelerated 3D audio.
    /// </summary>
    /// <param name="operatorId">The unique identifier of the operator.</param>
    /// <param name="filePath">The file path of the audio file to play.</param>
    /// <param name="shouldPlay">True to trigger playback on rising edge.</param>
    /// <param name="shouldStop">True to trigger stop on rising edge.</param>
    /// <param name="volume">The volume level (0.0 to 1.0).</param>
    /// <param name="mute">True to mute the stream.</param>
    /// <param name="position">The 3D world position of the audio source.</param>
    /// <param name="minDistance">The distance at which the volume starts to attenuate.</param>
    /// <param name="maxDistance">The distance at which the volume reaches minimum.</param>
    /// <param name="speed">The playback speed multiplier (default 1.0).</param>
    /// <param name="seek">
    /// The normalized seek position (0.0 to 1.0). This value is stored and only applied when 
    /// playback is triggered via <paramref name="shouldPlay"/>. Changing the seek value during 
    /// playback has no effect until the next play trigger. This allows setting the seek position 
    /// and triggering play in the same frame for predictable behavior.
    /// </param>
    /// <param name="orientation">The orientation vector of the sound source for directional audio.</param>
    /// <param name="innerConeAngle">The inner cone angle in degrees for directional audio.</param>
    /// <param name="outerConeAngle">The outer cone angle in degrees for directional audio.</param>
    /// <param name="outerConeVolume">The volume level outside the outer cone.</param>
    /// <param name="mode3D">The 3D processing mode.</param>
    public static void UpdateSpatialOperatorPlayback(
        Guid operatorId, string? filePath, bool shouldPlay, bool shouldStop,
        float volume, bool mute, Vector3 position, float minDistance, float maxDistance,
        float speed = 1.0f, float seek = 0f, Vector3? orientation = null,
        float innerConeAngle = 360f, float outerConeAngle = 360f, float outerConeVolume = 1.0f, int mode3D = 0)
    {
        EnsureFrameTokenCurrent();
        
        if (!EnsureMixerInitialized()) return;

        if (!_3dInitialized)
            Set3DListenerPosition(Vector3.Zero, new Vector3(0, 0, 1), new Vector3(0, 1, 0));

        var state = GetOrCreateSpatialState(operatorId);
        
        // Mark this operator as updated for this frame (used for stale detection)
        state.LastUpdatedFrameId = _audioFrameToken;
        
        var resolvedPath = !string.IsNullOrEmpty(filePath) ? ResolveFilePath(filePath) : string.Empty;

        if (!HandleSpatialFileChange(state, resolvedPath, operatorId))
            return;

        if (state.Stream == null) return;

        // Always update 3D position
        state.Stream.Update3DPosition(position, minDistance, maxDistance);

        // Store the pending seek position - will be applied on next play trigger
        if (seek >= 0f && seek <= 1f)
            state.PendingSeek = seek;

        if (HandleSpatialPlaybackTriggers(state, shouldPlay, shouldStop, operatorId))
            return;

        if (state.Stream.IsPlaying)
        {
            state.Stream.SetVolume(volume, mute);
            state.Stream.SetSpeed(speed);

            if (orientation.HasValue && orientation.Value.Length() > 0.001f)
                state.Stream.Set3DOrientation(orientation.Value);

            if (Math.Abs(innerConeAngle - 360f) > 0.1f || Math.Abs(outerConeAngle - 360f) > 0.1f || Math.Abs(outerConeVolume - 1.0f) > 0.001f)
                state.Stream.Set3DCone(innerConeAngle, outerConeAngle, outerConeVolume);

            if (mode3D != 0)
                state.Stream.Set3DMode((Mode3D)mode3D);
        }
    }

    /// <summary>Pauses the audio stream for the specified spatial operator.</summary>
    /// <param name="operatorId">The unique identifier of the operator.</param>
    public static void PauseSpatialOperator(Guid operatorId)
    {
        if (_spatialOperatorStates.TryGetValue(operatorId, out var state) && state.Stream != null)
        {
            state.Stream.Pause();
            state.IsPaused = true;
        }
    }
    
    /// <summary>Resumes the audio stream for the specified spatial operator.</summary>
    /// <param name="operatorId">The unique identifier of the operator.</param>
    public static void ResumeSpatialOperator(Guid operatorId)
    {
        if (_spatialOperatorStates.TryGetValue(operatorId, out var state) && state.Stream != null)
        {
            state.Stream.Resume();
            state.IsPaused = false;
        }
    }
    
    /// <summary>Checks if the audio stream is currently playing for the specified spatial operator.</summary>
    /// <param name="operatorId">The unique identifier of the operator.</param>
    /// <returns>True if the stream is playing and not paused; otherwise, false.</returns>
    public static bool IsSpatialOperatorStreamPlaying(Guid operatorId)
    {
        return _spatialOperatorStates.TryGetValue(operatorId, out var state)
               && state.Stream != null
               && state.Stream.IsPlaying
               && !state.Stream.IsPaused;
    }
    
    /// <summary>Checks if the audio stream is currently paused for the specified spatial operator.</summary>
    /// <param name="operatorId">The unique identifier of the operator.</param>
    /// <returns>True if the stream is paused; otherwise, false.</returns>
    public static bool IsSpatialOperatorPaused(Guid operatorId)
    {
        return _spatialOperatorStates.TryGetValue(operatorId, out var state)
               && state.Stream != null
               && state.IsPaused;
    }
    
    /// <summary>Gets the current audio level for the specified spatial operator.</summary>
    /// <param name="operatorId">The unique identifier of the operator.</param>
    /// <returns>The current audio level, or 0 if the stream is not found.</returns>
    public static float GetSpatialOperatorLevel(Guid operatorId)
    {
        return _spatialOperatorStates.TryGetValue(operatorId, out var state) && state.Stream != null
            ? state.Stream.GetLevel()
            : 0f;
    }

    /// <summary>
    /// Attempts to retrieve the spatial audio stream for the specified operator.
    /// </summary>
    /// <param name="operatorId">The unique identifier of the operator.</param>
    /// <param name="stream">When this method returns, contains the stream if found; otherwise, null.</param>
    /// <returns>True if the stream was found; otherwise, false.</returns>
    public static bool TryGetSpatialOperatorStream(Guid operatorId, out SpatialOperatorAudioStream? stream)
    {
        stream = null;
        if (_spatialOperatorStates.TryGetValue(operatorId, out var state) && state.Stream != null)
        {
            stream = state.Stream;
            return true;
        }
        return false;
    }

    #endregion

    #region Common Operator Helpers

    private static bool EnsureMixerInitialized()
    {
        if (AudioMixerManager.IsInitialized) return true;

        Log.Gated.Audio("[AudioEngine] Mixer not initialized, initializing...");
        AudioMixerManager.Initialize();

        if (AudioMixerManager.OperatorMixerHandle == 0)
        {
            // Don't log every time - Initialize() already logs the failure once
            return false;
        }
        return true;
    }

    private static OperatorAudioState<T> GetOrCreateState<T>(Dictionary<Guid, OperatorAudioState<T>> states, Guid operatorId)
        where T : OperatorAudioStreamBase
    {
        if (!states.TryGetValue(operatorId, out var state))
        {
            state = new OperatorAudioState<T>();
            states[operatorId] = state;
            Log.Gated.Audio($"[AudioEngine] Created audio state for operator: {operatorId}");
        }
        return state;
    }

    private static string ResolveFilePath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || System.IO.File.Exists(filePath)) 
            return filePath;

        if (AssetRegistry.TryResolveAddress(filePath, null, out var absolutePath, out _))
        {
            Log.Gated.Audio($"[AudioEngine] Resolved: {filePath} → {absolutePath}");
            return absolutePath;
        }

        // Return original path - HandleFileChange will log an error once if the file doesn't exist
        // and cache the failure to avoid repeated logging
        return filePath;
    }

    private static bool HandleFileChange<T>(OperatorAudioState<T> state, string? resolvedPath, Guid operatorId,
        Func<string, T?> loadFunc) where T : OperatorAudioStreamBase
    {
        // If the path hasn't changed, check if this is a previously failed path
        if (state.CurrentFilePath == resolvedPath)
        {
            // If we have a cached error for this path, skip loading without logging again
            return state.FailedFilePath == resolvedPath && state.LastLoadError != null || true;
        }

        Log.Gated.Audio($"[AudioEngine] File changed for {operatorId}: '{state.CurrentFilePath}' → '{resolvedPath}'");

        state.Stream?.Dispose();
        state.Stream = null;
        state.CurrentFilePath = resolvedPath ?? string.Empty;
        state.PreviousPlay = false;
        state.PreviousStop = false;
        
        // Clear previous load error when path changes
        state.FailedFilePath = null;
        state.LastLoadError = null;

        if (!string.IsNullOrEmpty(resolvedPath))
        {
            // Check file existence before attempting to load
            if (!System.IO.File.Exists(resolvedPath))
            {
                var error = $"File does not exist: {resolvedPath}";
                Log.Error($"[AudioEngine] Failed to load stream for {operatorId}: {error}");
                state.FailedFilePath = resolvedPath;
                state.LastLoadError = error;
            }
            else
            {
                state.Stream = loadFunc(resolvedPath);
                if (state.Stream == null)
                {
                    var bassError = Bass.LastError;
                    var error = $"{resolvedPath} (BASS error: {bassError})";
                    Log.Error($"[AudioEngine] Failed to load stream for {operatorId}: {error}");
                    state.FailedFilePath = resolvedPath;
                    state.LastLoadError = error;
                }
            }
        }

        // During export, mark new streams as stale
        if (state.Stream == null || !Playback.Current.IsRenderingToFile) 
            return true;
        
        state.IsStale = true;
        state.Stream.SetStale(true);
        Log.Gated.Audio($"[AudioEngine] New stream during export - marking stale: {resolvedPath}");
        return false;

    }

    private static bool HandlePlaybackTriggers<T>(OperatorAudioState<T> state, bool shouldPlay, bool shouldStop, Guid operatorId)
        where T : OperatorAudioStreamBase
    {
        var playTrigger = shouldPlay && !state.PreviousPlay;
        var stopTrigger = shouldStop && !state.PreviousStop;
        
        state.PreviousPlay = shouldPlay;
        state.PreviousStop = shouldStop;

        if (stopTrigger)
        {
            state.Stream!.Stop();
            state.IsPaused = false;
            state.PendingSeek = 0f;
            return true;
        }

        if (playTrigger)
        {
            state.Stream!.Stop();
            
            // Apply pending seek position before starting playback
            if (state.PendingSeek > 0f && state.PendingSeek <= 1f)
            {
                var seekTime = (float)(state.PendingSeek * state.Stream.Duration);
                state.Stream.Seek(seekTime);
                Log.Gated.Audio($"[AudioEngine] Applied pending seek {state.PendingSeek:F3} ({seekTime:F3}s) for {operatorId}");
            }
            
            state.Stream.Play();
            state.IsPaused = false;
            state.IsStale = false;
        }

        return false;
    }

    private static void PauseOperatorInternal<T>(Dictionary<Guid, OperatorAudioState<T>> states, Guid operatorId)
        where T : OperatorAudioStreamBase
    {
        if (states.TryGetValue(operatorId, out var state) && state.Stream != null)
        {
            state.Stream.Pause();
            state.IsPaused = true;
        }
    }

    private static void ResumeOperatorInternal<T>(Dictionary<Guid, OperatorAudioState<T>> states, Guid operatorId)
        where T : OperatorAudioStreamBase
    {
        if (states.TryGetValue(operatorId, out var state) && state.Stream != null)
        {
            state.Stream.Resume();
            state.IsPaused = false;
        }
    }

    private static bool IsOperatorPlaying<T>(Dictionary<Guid, OperatorAudioState<T>> states, Guid operatorId)
        where T : OperatorAudioStreamBase
    {
        return states.TryGetValue(operatorId, out var state)
               && state.Stream != null
               && state.Stream.IsPlaying
               && !state.Stream.IsPaused;
    }

    private static bool IsOperatorPausedInternal<T>(Dictionary<Guid, OperatorAudioState<T>> states, Guid operatorId)
        where T : OperatorAudioStreamBase
    {
        return states.TryGetValue(operatorId, out var state)
               && state.Stream != null
               && state.IsPaused;
    }

    private static float GetOperatorLevelInternal<T>(Dictionary<Guid, OperatorAudioState<T>> states, Guid operatorId)
        where T : OperatorAudioStreamBase
    {
        return states.TryGetValue(operatorId, out var state) && state.Stream != null
            ? state.Stream.GetLevel()
            : 0f;
    }

    #endregion

    #region Spatial Operator Helpers (Non-Generic)

    private static SpatialOperatorState GetOrCreateSpatialState(Guid operatorId)
    {
        if (!_spatialOperatorStates.TryGetValue(operatorId, out var state))
        {
            state = new SpatialOperatorState();
            _spatialOperatorStates[operatorId] = state;
            Log.Gated.Audio($"[AudioEngine] Created spatial audio state for operator: {operatorId}");
        }
        return state;
    }

    private static bool HandleSpatialFileChange(SpatialOperatorState state, string? resolvedPath, Guid operatorId)
    {
        // If the path hasn't changed, check if this is a previously failed path
        if (state.CurrentFilePath == resolvedPath)
        {
            // If we have a cached error for this path, skip loading without logging again
            return state.FailedFilePath == resolvedPath && state.LastLoadError != null || true;
        }

        Log.Gated.Audio($"[AudioEngine] File changed for spatial {operatorId}: '{state.CurrentFilePath}' → '{resolvedPath}'");

        state.Stream?.Dispose();
        state.Stream = null;
        state.CurrentFilePath = resolvedPath ?? string.Empty;
        state.PreviousPlay = false;
        state.PreviousStop = false;
        
        // Clear previous load error when path changes
        state.FailedFilePath = null;
        state.LastLoadError = null;

        if (!string.IsNullOrEmpty(resolvedPath))
        {
            // Check file existence before attempting to load
            if (!System.IO.File.Exists(resolvedPath))
            {
                var error = $"File does not exist: {resolvedPath}";
                Log.Error($"[AudioEngine] Failed to load spatial stream for {operatorId}: {error}");
                state.FailedFilePath = resolvedPath;
                state.LastLoadError = error;
            }
            // Note: mixerHandle parameter is ignored for spatial streams - they play directly to BASS
            else if (!SpatialOperatorAudioStream.TryLoadStream(resolvedPath, 0, out var stream))
            {
                var bassError = Bass.LastError;
                var error = $"{resolvedPath} (BASS error: {bassError})";
                Log.Error($"[AudioEngine] Failed to load spatial stream for {operatorId}: {error}");
                state.FailedFilePath = resolvedPath;
                state.LastLoadError = error;
            }
            else
            {
                state.Stream = stream;
            }
        }

        // During export, mark new streams as stale
        if (state.Stream == null || !Playback.Current.IsRenderingToFile) 
            return true;
        
        state.IsStale = true;
        state.Stream.SetStale(true);
        Log.Gated.Audio($"[AudioEngine] New spatial stream during export - marking stale: {resolvedPath}");
        return false;
    }

    private static bool HandleSpatialPlaybackTriggers(SpatialOperatorState state, bool shouldPlay, bool shouldStop, Guid operatorId)
    {
        var playTrigger = shouldPlay && !state.PreviousPlay;
        var stopTrigger = shouldStop && !state.PreviousStop;
        
        state.PreviousPlay = shouldPlay;
        state.PreviousStop = shouldStop;

        if (stopTrigger)
        {
            state.Stream!.Stop();
            state.IsPaused = false;
            state.PendingSeek = 0f;
            return true;
        }

        if (playTrigger)
        {
            state.Stream!.Stop();
            
            // Apply pending seek position before starting playback
            if (state.PendingSeek > 0f && state.PendingSeek <= 1f)
            {
                var seekTime = (float)(state.PendingSeek * state.Stream.Duration);
                state.Stream.Seek(seekTime);
                Log.Gated.Audio($"[AudioEngine] Applied pending seek {state.PendingSeek:F3} ({seekTime:F3}s) for spatial {operatorId}");
            }
            
            state.Stream.Play();
            state.IsPaused = false;
            state.IsStale = false;
        }

        return false;
    }


    #endregion


    /// <summary>
    /// Unregisters an operator from audio playback and disposes its associated streams.
    /// </summary>
    /// <param name="operatorId">The unique identifier of the operator to unregister.</param>
    public static void UnregisterOperator(Guid operatorId)
    {
        if (_stereoOperatorStates.TryGetValue(operatorId, out var stereoState))
        {
            stereoState.Stream?.Dispose();
            _stereoOperatorStates.Remove(operatorId);
        }

        if (_spatialOperatorStates.TryGetValue(operatorId, out var spatialState))
        {
            spatialState.Stream?.Dispose();
            _spatialOperatorStates.Remove(operatorId);
        }
    }

    /// <summary>
    /// Clears the cached load error for an operator, allowing it to retry loading the same file.
    /// Call this when the user explicitly requests a reload or when the underlying file may have changed.
    /// </summary>
    /// <param name="operatorId">The unique identifier of the operator.</param>
    public static void ClearOperatorLoadError(Guid operatorId)
    {
        if (_stereoOperatorStates.TryGetValue(operatorId, out var stereoState))
        {
            stereoState.FailedFilePath = null;
            stereoState.LastLoadError = null;
            // Also clear the current file path to force a reload attempt
            stereoState.CurrentFilePath = string.Empty;
        }

        if (_spatialOperatorStates.TryGetValue(operatorId, out var spatialState))
        {
            spatialState.FailedFilePath = null;
            spatialState.LastLoadError = null;
            // Also clear the current file path to force a reload attempt
            spatialState.CurrentFilePath = string.Empty;
        }
    }

    /// <summary>
    /// Gets the last load error for an operator, if any.
    /// </summary>
    /// <param name="operatorId">The unique identifier of the operator.</param>
    /// <returns>The error message if a load failed, null otherwise.</returns>
    public static string? GetOperatorLoadError(Guid operatorId)
    {
        if (_stereoOperatorStates.TryGetValue(operatorId, out var stereoState) && stereoState.LastLoadError != null)
            return stereoState.LastLoadError;

        if (_spatialOperatorStates.TryGetValue(operatorId, out var spatialState) && spatialState.LastLoadError != null)
            return spatialState.LastLoadError;

        return null;
    }

    #region Stale Detection & Export

    /// <summary>
    /// Checks if a new frame has started and increments the internal frame token if so.
    /// Called automatically by operator update methods to ensure frame token is current
    /// before recording operator updates.
    /// </summary>
    private static void EnsureFrameTokenCurrent()
    {
        if (Playback.Current.IsRenderingToFile) return;
        
        var currentPlaybackFrame = Playback.FrameCount;
        if (_lastSeenPlaybackFrame != currentPlaybackFrame)
        {
            _lastSeenPlaybackFrame = currentPlaybackFrame;
            _audioFrameToken++;
        }
    }

    private static void StopStaleOperators()
    {
        if (Playback.Current.IsRenderingToFile) return;

        // Only run stale check once per frame token
        if (_lastStaleCheckFrameToken == _audioFrameToken) return;
        _lastStaleCheckFrameToken = _audioFrameToken;

        UpdateStaleStates(_stereoOperatorStates);
        UpdateSpatialStaleStates();
    }

    private static void UpdateStaleStates<T>(Dictionary<Guid, OperatorAudioState<T>> states)
        where T : OperatorAudioStreamBase
    {
        foreach (var (_, state) in states)
        {
            if (state.Stream == null) continue;

            // An operator is stale if it wasn't updated this frame
            bool isStale = (state.LastUpdatedFrameId != _audioFrameToken);
            if (state.IsStale != isStale)
            {
                state.Stream.SetStale(isStale);
                state.IsStale = isStale;
            }
        }
    }

    private static void UpdateSpatialStaleStates()
    {
        foreach (var (_, state) in _spatialOperatorStates)
        {
            if (state.Stream == null) continue;

            // An operator is stale if it wasn't updated this frame
            bool isStale = (state.LastUpdatedFrameId != _audioFrameToken);
            if (state.IsStale != isStale)
            {
                state.Stream.SetStale(isStale);
                state.IsStale = isStale;
            }
        }
    }

    /// <summary>
    /// Resets all operator audio streams in preparation for audio export.
    /// </summary>
    internal static void ResetAllOperatorStreamsForExport()
    {
        IsExporting = true;
        ResetOperatorStreamsForExport(_stereoOperatorStates);
        ResetSpatialOperatorStreamsForExport();
    }

    private static void ResetOperatorStreamsForExport<T>(Dictionary<Guid, OperatorAudioState<T>> states)
        where T : OperatorAudioStreamBase
    {
        foreach (var (_, state) in states)
        {
            if (state.Stream == null) continue;
            
            state.Stream.PrepareForExport();
            
            // Mark as stale and pause - the stale detection will un-pause if operator updates
            state.Stream.SetStale(true);
            state.IsStale = true;
        }
    }

    private static void ResetSpatialOperatorStreamsForExport()
    {
        foreach (var (_, state) in _spatialOperatorStates)
        {
            if (state.Stream == null) continue;
            
            state.Stream.PrepareForExport();
            
            // Mark as stale - spatial streams handle stale differently
            state.IsStale = true;
        }
    }

    /// <summary>
    /// Updates the stale states for all operator streams during export.
    /// Checks stale state against current token (operators that updated last frame),
    /// THEN increments the token for the next frame.
    /// </summary>
    internal static void UpdateStaleStatesForExport()
    {
        // First, check stale states against the CURRENT token
        // This identifies which operators were updated in the previous frame
        // (i.e., which operators should be producing audio for this export frame)
        UpdateStaleStates(_stereoOperatorStates);
        UpdateSpatialStaleStates();
        
        // Then increment the frame token for the next frame's operator updates
        _audioFrameToken++;
    }

    /// <summary>
    /// Restores all operator audio streams after export has completed.
    /// </summary>
    internal static void RestoreOperatorAudioStreams()
    {
        IsExporting = false;
        
        if (AudioMixerManager.GlobalMixerHandle != 0)
        {
            if (Bass.ChannelIsActive(AudioMixerManager.GlobalMixerHandle) != PlaybackState.Playing)
                Bass.ChannelPlay(AudioMixerManager.GlobalMixerHandle);
            Bass.ChannelUpdate(AudioMixerManager.GlobalMixerHandle, 0);
        }

        RestoreOperatorStreams(_stereoOperatorStates);
        RestoreSpatialOperatorStreams();

        if (AudioMixerManager.GlobalMixerHandle != 0)
            Bass.ChannelUpdate(AudioMixerManager.GlobalMixerHandle, 0);
    }

    private static void RestoreOperatorStreams<T>(Dictionary<Guid, OperatorAudioState<T>> states)
        where T : OperatorAudioStreamBase
    {
        foreach (var state in states.Values)
        {
            if (state.Stream != null)
            {
                state.Stream.ClearExportMetering();
                state.Stream.RestartAfterExport();
                state.Stream.SetStale(false);
                state.IsStale = false;
            }
        }
    }

    private static void RestoreSpatialOperatorStreams()
    {
        foreach (var state in _spatialOperatorStates.Values)
        {
            if (state.Stream != null)
            {
                state.Stream.ClearExportMetering();
                state.Stream.RestartAfterExport();
                state.Stream.SetStale(false);
                state.IsStale = false;
            }
        }
    }

    #endregion

    #region Device & Volume Management

    /// <summary>
    /// Handles audio device changes by reinitializing all audio streams.
    /// </summary>
    /// <summary>
    /// Handles audio device changes by disposing all audio streams and reinitializing.
    /// This ensures no stale BASS handles remain after Bass.Free() is called.
    /// </summary>
    public static void OnAudioDeviceChanged()
    {
        // Dispose ALL streams before calling AudioMixerManager.Shutdown() / Bass.Free()
        // This prevents invalid handle errors when streams are accessed after device change
        DisposeAllAudioStreams();

        AudioMixerManager.Shutdown();
        _bassInitialized = false; // Reset flag to allow proper reinitialization
        _bassInitFailed = false;  // Reset failure flag to allow retry
        AudioMixerManager.Initialize();

        Log.Gated.Audio("[AudioEngine] Audio device changed: all streams disposed and reinitialized.");
    }

    /// <summary>
    /// Disposes all audio streams (stereo operators, spatial operators, and soundtracks).
    /// Called during device changes to prevent invalid BASS handle errors.
    /// </summary>
    private static void DisposeAllAudioStreams()
    {
        // Dispose stereo operator streams
        foreach (var state in _stereoOperatorStates.Values)
            state.Stream?.Dispose();
        _stereoOperatorStates.Clear();

        // Dispose spatial operator streams
        foreach (var state in _spatialOperatorStates.Values)
            state.Stream?.Dispose();
        _spatialOperatorStates.Clear();

        // Dispose soundtrack streams
        foreach (var (_, clipStream) in SoundtrackClipStreams)
            clipStream.DisableSoundtrackStream();
        SoundtrackClipStreams.Clear();
        _updatedSoundtrackClipTimes.Clear();
        _obsoleteSoundtrackHandles.Clear();

        Log.Gated.Audio("[AudioEngine] Disposed all audio streams (stereo, spatial, soundtrack).");
    }

    /// <summary>
    /// Sets the global audio volume level.
    /// </summary>
    /// <param name="volume">The volume level (0.0 to 1.0).</param>
    public static void SetGlobalVolume(float volume)
    {
        ProjectSettings.Config.GlobalPlaybackVolume = volume;
        AudioMixerManager.SetGlobalVolume(volume);
    }

    /// <summary>
    /// Initializes the global volume from the stored project settings.
    /// </summary>
    public static void InitializeGlobalVolumeFromSettings()
    {
        AudioMixerManager.SetGlobalVolume(ProjectSettings.Config.GlobalPlaybackVolume);
    }

    /// <summary>
    /// Sets the global audio mute state.
    /// </summary>
    /// <param name="mute">True to mute all audio, false to unmute.</param>
    public static void SetGlobalMute(bool mute)
    {
        AudioMixerManager.SetGlobalMute(mute);
        ProjectSettings.Config.GlobalMute = mute;
    }

    /// <summary>
    /// Sets the mute state for all operator audio streams.
    /// </summary>
    /// <param name="mute">True to mute operator audio, false to unmute.</param>
    public static void SetOperatorMute(bool mute)
    {
        AudioMixerManager.SetOperatorMute(mute);
        ProjectSettings.Config.OperatorMute = mute;
    }

    #endregion

    #region Export Metering Accessors

    /// <summary>
    /// Gets all stereo operator states for export metering purposes.
    /// </summary>
    /// <returns>Enumerable of operator ID and stream state pairs.</returns>
    public static IEnumerable<KeyValuePair<Guid, (StereoOperatorAudioStream? Stream, bool IsStale)>> GetAllStereoOperatorStates()
    {
        foreach (var kvp in _stereoOperatorStates)
            yield return new KeyValuePair<Guid, (StereoOperatorAudioStream?, bool)>(kvp.Key, (kvp.Value.Stream, kvp.Value.IsStale));
    }

    /// <summary>
    /// Gets all spatial operator states for export metering purposes.
    /// </summary>
    /// <returns>Enumerable of operator ID and stream state pairs.</returns>
    public static IEnumerable<KeyValuePair<Guid, (SpatialOperatorAudioStream? Stream, bool IsStale)>> GetAllSpatialOperatorStates()
    {
        foreach (var kvp in _spatialOperatorStates)
            yield return new KeyValuePair<Guid, (SpatialOperatorAudioStream?, bool)>(kvp.Key, (kvp.Value.Stream, kvp.Value.IsStale));
    }

    #endregion
}

