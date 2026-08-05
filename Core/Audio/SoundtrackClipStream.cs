#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using ManagedBass;
using ManagedBass.Mix;
using T3.Core.Animation;
using T3.Core.IO;
using T3.Core.Settings;
using T3.Core.Logging;

namespace T3.Core.Audio;

/// <summary>
/// Controls the playback of a <see cref="TimelineAudioClip"/> with BASS by the <see cref="AudioEngine"/>.
/// 
/// The stream is created as a decode stream and added to the SoundtrackMixer.
/// For live playback, audio flows through: Stream -> SoundtrackMixer -> GlobalMixer -> Soundcard
/// For export, we read directly from the stream using BassMix.ChannelGetData().
/// </summary>
internal sealed class SoundtrackClipStream
{
    // Private constructor
    private SoundtrackClipStream()
    {
    }

    public double Duration;
    internal int StreamHandle;
    internal bool IsInUse;
    public bool IsNew = true;
    private float DefaultPlaybackFrequency { get; init; }
    internal double TargetTime { get; set; }

    internal AudioClipResourceHandle ResourceHandle = null!;

    /// <summary>
    /// Gets the native/default playback frequency of this stream.
    /// This is the original sample rate of the audio file.
    /// </summary>
    public float GetDefaultFrequency() => DefaultPlaybackFrequency;

    internal void UpdateSoundtrackPlaybackSpeed(double newSpeed)
    {
        if (newSpeed == 0.0)
        {
            // Pause in mixer
            BassMix.ChannelFlags(StreamHandle, BassFlags.MixerChanPause, BassFlags.MixerChanPause);
        }
        else if (newSpeed < 0.0)
        {
            // Play backwards
            Bass.ChannelSetAttribute(StreamHandle, ChannelAttribute.ReverseDirection, -1);
            Bass.ChannelSetAttribute(StreamHandle, ChannelAttribute.Frequency, DefaultPlaybackFrequency * -newSpeed);
            BassMix.ChannelFlags(StreamHandle, 0, BassFlags.MixerChanPause); // Unpause
        }
        else
        {
            // Play forward
            Bass.ChannelSetAttribute(StreamHandle, ChannelAttribute.ReverseDirection, 1);
            Bass.ChannelSetAttribute(StreamHandle, ChannelAttribute.Frequency, DefaultPlaybackFrequency * newSpeed);
            BassMix.ChannelFlags(StreamHandle, 0, BassFlags.MixerChanPause); // Unpause
        }
    }

    /// <summary>
    /// Creates a <see cref="SoundtrackClipStream"/> by loading an <see cref="AudioClipResourceHandle"/>. 
    /// The stream is created as a decode stream and added to the SoundtrackMixer.
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

        var path = fileInfo.FullName;
        
        // Create as a DECODE stream so we can add it to the SoundtrackMixer
        var streamHandle = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Prescan | BassFlags.Float);

        if (streamHandle == 0)
        {
            Log.Error($"Error loading audio clip '{path}': {Bass.LastError}.");
            return false;
        }

        Bass.ChannelGetAttribute(streamHandle, ChannelAttribute.Frequency, out var defaultPlaybackFrequency);

        var bytes = Bass.ChannelGetLength(streamHandle);
        if (bytes < 0)
        {
            Log.Error($"Failed to get length for audio clip {path}.");
            Bass.StreamFree(streamHandle);
            return false;
        }

        var duration = (float)Bass.ChannelBytes2Seconds(streamHandle, bytes);
        handle.Clip.LengthInSeconds = duration;

        // Add to SoundtrackMixer for live playback
        // The SoundtrackMixer feeds into the GlobalMixer for output
        // Note: We do NOT use MixerChanBuffer for soundtracks because we need accurate
        // position tracking for sync. The buffer would introduce latency that's hard to compensate.
        // Skip when the clip is already graph-routed: the [AudioBus] owns its mixer membership, and adding it
        // here would blip it at full volume through the SoundtrackMixer for the frame before the bus grabs it.
        if (AudioMixerManager.SoundtrackMixerHandle != 0 && !handle.Clip.IsRoutedToGraph)
        {
            if (!BassMix.MixerAddChannel(AudioMixerManager.SoundtrackMixerHandle, streamHandle,
                    BassFlags.MixerChanPause))
            {
                Log.Warning($"Failed to add soundtrack to mixer: {Bass.LastError}. Audio may not play correctly.");
            }
            else
            {
                Log.Gated.Audio($"[SoundtrackClipStream] Added '{handle.Clip.AssetPath}' to SoundtrackMixer");
            }
        }

        stream = new SoundtrackClipStream()
                         {
                             ResourceHandle = handle,
                             StreamHandle = streamHandle,
                             DefaultPlaybackFrequency = defaultPlaybackFrequency,
                             Duration = duration,
                         };

        // Start playing (unpaused in mixer)
        stream.UpdateSoundtrackPlaybackSpeed(1.0);
        handle.LoadingAttemptFailed = false;
        return true;
    }

    /// <summary>
    /// We try to find a compromise between letting bass play the audio clip in the correct playback speed which
    /// eventually will drift away from Tooll's Playback time. If the delta between playback and audio-clip time exceeds
    /// a threshold, we resync.
    /// 
    /// Frequent resync causes audio glitches.
    /// Too large of a threshold can disrupt syncing and increase latency.
    /// </summary>
    internal void UpdateSoundtrackTime(Playback playback)
    {
        // Render-export steps time with PlaybackSpeed 0, so recording counts as running.
        if (playback.PlaybackSpeed == 0 && !AudioRendering.IsRecording)
        {
            BassMix.ChannelFlags(StreamHandle, BassFlags.MixerChanPause, BassFlags.MixerChanPause);
            return;
        }

        var clip = ResourceHandle.Clip;

        // Graph hand-off: while the clip's AudioReference is wired into a bus, the graph moves this channel out
        // of the SoundtrackMixer into its submix and owns its level. When it stops being routed, reclaim it into
        // the SoundtrackMixer — but only once the bus has actually released it (a channel lives in exactly one
        // mixer). Checking real membership every frame instead of a one-shot transition avoids racing the bus,
        // whose removal can run later in the same frame. Position-sync (below) is a per-channel BASS op and
        // works in whichever mixer holds the channel.
        if (!clip.IsRoutedToGraph
            && AudioMixerManager.SoundtrackMixerHandle != 0
            && BassMix.ChannelGetMixer(StreamHandle) == 0)
        {
            BassMix.MixerAddChannel(AudioMixerManager.SoundtrackMixerHandle, StreamHandle, BassFlags.MixerChanPause);
        }

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
        
        // Check if paused in mixer
        var flags = BassMix.ChannelFlags(StreamHandle, 0, 0);
        var isPaused = (flags & BassFlags.MixerChanPause) != 0;

        if (isOutOfBounds)
        {
            if (!isPaused)
            {
                BassMix.ChannelFlags(StreamHandle, BassFlags.MixerChanPause, BassFlags.MixerChanPause);
            }
            return;
        }

        if (isPaused)
        {
            BassMix.ChannelFlags(StreamHandle, 0, BassFlags.MixerChanPause); // Unpause
        }

        // Export steps time with PlaybackSpeed 0 — sync as if playing at 1× speed, otherwise the
        // speed-scaled drift and threshold both collapse to 0 and clips never reseek into the render range.
        var effectiveSpeed = AudioRendering.IsRecording ? 1.0 : playback.PlaybackSpeed;

        // Get the current playback position from the mixer
        var currentStreamBufferPos = BassMix.ChannelGetPosition(StreamHandle);
        var currentPosInSec = Bass.ChannelBytes2Seconds(StreamHandle, currentStreamBufferPos) - AudioSyncingOffset;
        var soundDelta = (currentPosInSec - targetSourcePosSecs) * effectiveSpeed;

        // Set volume on the stream — unless the graph owns this clip's level (routed into a bus), in which case
        // the [AudioBus] applies the per-source gain and setting it here too would fight that every frame.
        var audio = CompositionSettings.Current.Audio;
        if (!clip.IsRoutedToGraph)
        {
            Bass.ChannelSetAttribute(StreamHandle, ChannelAttribute.Volume,
                                     clip.Volume
                                     * audio.SoundtrackVolume
                                     * CoreSettings.Config.AppVolume
                                     * (clip.IsMuted ? 0f:1f)
                                     * (audio.SoundtrackMute ? 0f:1f)
                                     * (CoreSettings.Config.AppMute ? 0f:1f));
        }

        // We may not fall behind or skip ahead in playback
        var maxSoundDelta = audio.AudioResyncThreshold * Math.Abs(effectiveSpeed);
        if (Math.Abs(soundDelta) <= maxSoundDelta)
            return;

        // Resync
        var resyncOffset = AudioTriggerDelayOffset * effectiveSpeed + AudioSyncingOffset;
        var newStreamPos = Bass.ChannelSeconds2Bytes(StreamHandle, targetSourcePosSecs + resyncOffset);
        BassMix.ChannelSetPosition(StreamHandle, newStreamPos, PositionFlags.Bytes | PositionFlags.MixerReset);
    }

    /// <summary>
    /// Update time when recording, returns number of bytes of the position from the stream start
    /// </summary>
    internal long UpdateTimeWhileRecording(Playback playback, double fps, bool reinitialize)
    {
        // Offset timing dependent on position in clip. SourceOffsetSecs lets a clip skip
        // into the middle of the file at its start; defaults to 0 for migrated clips.
        var clip = ResourceHandle.Clip;
        var elapsedInClipSecs = playback.TimeInSecs - playback.SecondsFromBars(clip.TimeRange.Start);
        var targetSourcePosSecs = clip.SourceOffsetSecs + elapsedInClipSecs + RecordSyncingOffset;
        var newStreamPos = targetSourcePosSecs < 0
                               ? -Bass.ChannelSeconds2Bytes(StreamHandle, -targetSourcePosSecs)
                               : Bass.ChannelSeconds2Bytes(StreamHandle, targetSourcePosSecs);

        // Re-initialize playback?
        if (!reinitialize)
            return newStreamPos;

        Bass.ChannelSetAttribute(StreamHandle, ChannelAttribute.NoRamp, 1);
        Bass.ChannelSetAttribute(StreamHandle, ChannelAttribute.Volume, 1);
        Bass.ChannelSetAttribute(StreamHandle, ChannelAttribute.ReverseDirection, 1);
        Bass.ChannelSetAttribute(StreamHandle, ChannelAttribute.Frequency, DefaultPlaybackFrequency);
        
        // Position in the mixer
        BassMix.ChannelSetPosition(StreamHandle, Math.Max(newStreamPos, 0), 
            PositionFlags.Bytes | PositionFlags.MixerNoRampIn | PositionFlags.MixerReset);

        return newStreamPos;
    }

    internal void DisableSoundtrackStream()
    {
        // Remove from mixer first
        BassMix.MixerRemoveChannel(StreamHandle);
        Bass.StreamFree(StreamHandle);
    }

    /// <summary>
    /// Render audio for export, filling the buffer at the requested sample rate and channel count.
    /// </summary>
    public int RenderAudio(double startTime, double duration, float[] outputBuffer, int targetSampleRate, int targetChannels)
    {
        int nativeSampleRate = AudioEngine.GetClipSampleRate(ResourceHandle);
        int nativeChannels = AudioEngine.GetClipChannelCount(ResourceHandle);
        OperatorAudioUtils.FillAndResample(
            RenderNativeAudio,
            startTime, duration, outputBuffer,
            nativeSampleRate, nativeChannels, targetSampleRate, targetChannels);
        return outputBuffer.Length;
    }

    // Native render: fill buffer at native sample rate/channels
    private int RenderNativeAudio(double startTime, double duration, float[] buffer)
    {
        int sampleCount = buffer.Length / AudioEngine.GetClipChannelCount(ResourceHandle);
        int bytesToRead = sampleCount * AudioEngine.GetClipChannelCount(ResourceHandle) * sizeof(float);
        // Use BassMix.ChannelGetData for streams in a mixer
        int bytesRead = BassMix.ChannelGetData(StreamHandle, buffer, bytesToRead);
        return bytesRead > 0 ? bytesRead / sizeof(float) : 0;
    }

    private const double AudioSyncingOffset = -2.0 / 60.0;
    private const double AudioTriggerDelayOffset = 2.0 / 60.0;
    private const double RecordSyncingOffset = -1.0 / 60.0;
}