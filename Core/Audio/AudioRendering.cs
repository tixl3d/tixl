#nullable enable
using System;
using System.Collections.Generic;
using ManagedBass;
using ManagedBass.Mix;
using T3.Core.Animation;
using T3.Core.IO;
using T3.Core.Logging;
using T3.Core.Operator;

namespace T3.Core.Audio;

/// <summary>
/// Handles audio rendering/export functionality.
/// For export, we temporarily remove soundtrack streams from the mixer and read directly from them.
/// </summary>
public static class AudioRendering
{
    private static bool _isRecording;
    private static readonly ExportState _exportState = new();
    private static int _frameCount;

    // Reusable buffers to reduce per-frame allocations during export
    private static float[] _mixBuffer = Array.Empty<float>();
    private static float[] _operatorBuffer = Array.Empty<float>();
    private static float[] _spatialStreamBuffer = Array.Empty<float>();
    
    // Export mixer handle - BASS handles resampling and mixing for us
    private static int _exportMixerHandle;
    private static bool _exportMixerInitialized;

    /// <summary>
    /// Ensures the buffer has at least the required capacity, reallocating if necessary.
    /// Clears the buffer up to the required length before returning.
    /// </summary>
    private static float[] EnsureBuffer(ref float[] buffer, int requiredLength)
    {
        if (buffer.Length < requiredLength)
            buffer = new float[requiredLength];
        Array.Clear(buffer, 0, requiredLength);
        return buffer;
    }

    public static void PrepareRecording(Playback playback, double fps)
    {
        if (_isRecording) return;

        _isRecording = true;
        _frameCount = 0;

        _exportState.SaveState();
        AudioExportSourceRegistry.Clear();

        // Reset audio analysis state for clean export - ensures both modes start from same state
        AudioAnalysisContext.Default.Reset();
        WaveFormProcessing.ResetExportBuffer();

        Bass.ChannelPause(AudioMixerManager.GlobalMixerHandle);
        Log.Gated.AudioRender("[AudioRendering] GlobalMixer PAUSED for export");

        AudioEngine.ResetAllOperatorStreamsForExport();

        // Create export mixer - BASS handles resampling when adding streams
        // This replaces manual ResampleAndMix with BASS's optimized resampler
        _exportMixerHandle = BassMix.CreateMixerStream(
            AudioConfig.MixerFrequency, 
            2, // stereo output
            BassFlags.Decode | BassFlags.Float | BassFlags.MixerNonStop);
        
        if (_exportMixerHandle == 0)
        {
            Log.Error($"[AudioRendering] Failed to create export mixer: {Bass.LastError}");
            _exportMixerInitialized = false;
        }
        else
        {
            _exportMixerInitialized = true;
            Log.Gated.AudioRender($"[AudioRendering] Export mixer created: Handle={_exportMixerHandle}, Freq={AudioConfig.MixerFrequency}Hz");
        }

        // Remove soundtrack streams from live mixer and add to export mixer
        foreach (var (handle, clipStream) in AudioEngine.SoundtrackClipStreams)
        {
            BassMix.MixerRemoveChannel(clipStream.StreamHandle);
            
            // Reset stream attributes for export
            float nativeFrequency = clipStream.GetDefaultFrequency();
            Bass.ChannelSetAttribute(clipStream.StreamHandle, ChannelAttribute.Frequency, nativeFrequency);
            Bass.ChannelSetAttribute(clipStream.StreamHandle, ChannelAttribute.NoRamp, 1);
            
            if (_exportMixerInitialized)
            {
                // Add to export mixer - BASS will handle resampling from native frequency to mixer frequency
                // Use MixerChanNoRamping for sample-accurate seeking during export
                if (!BassMix.MixerAddChannel(_exportMixerHandle, clipStream.StreamHandle, BassFlags.MixerChanNoRampin | BassFlags.MixerChanPause))
                {
                    Log.Warning($"[AudioRendering] Failed to add soundtrack to export mixer: {Bass.LastError}");
                }
                else
                {
                    Log.Gated.AudioRender($"[AudioRendering] Soundtrack '{handle.Clip.FilePath}' added to export mixer");
                }
            }
        }

        Log.Gated.AudioRender($"[AudioRendering] PrepareRecording: fps={fps}");
    }

    public static void EndRecording(Playback playback, double fps)
    {
        if (!_isRecording) return;

        _isRecording = false;
        Log.Gated.AudioRender($"[AudioRendering] EndRecording: Exported {_frameCount} frames");

        // Remove soundtrack streams from export mixer and re-add to live mixer
        foreach (var (_, clipStream) in AudioEngine.SoundtrackClipStreams)
        {
            if (_exportMixerInitialized)
            {
                BassMix.MixerRemoveChannel(clipStream.StreamHandle);
            }
            
            if (!BassMix.MixerAddChannel(AudioMixerManager.SoundtrackMixerHandle, clipStream.StreamHandle, BassFlags.MixerChanPause))
            {
                Log.Warning($"[AudioRendering] Failed to re-add soundtrack: {Bass.LastError}");
            }
            clipStream.UpdateTimeWhileRecording(playback, fps, true);
        }
        
        // Clean up export mixer
        if (_exportMixerInitialized && _exportMixerHandle != 0)
        {
            Bass.StreamFree(_exportMixerHandle);
            _exportMixerHandle = 0;
            _exportMixerInitialized = false;
            Log.Gated.AudioRender("[AudioRendering] Export mixer freed");
        }
        
        _exportState.RestoreState();
        AudioEngine.RestoreOperatorAudioStreams();
    }

    internal static void ExportAudioFrame(Playback playback, double frameDurationInSeconds, SoundtrackClipStream clipStream)
    {
        try
        {
            AudioEngine.UpdateFftBufferFromSoundtrack(playback);
        }
        catch (Exception ex)
        {
            Log.Error($"ExportAudioFrame error: {ex}", typeof(AudioRendering));
        }
    }

    public static float[] GetFullMixDownBuffer(double frameDurationInSeconds)
    {
        _frameCount++;
        
        // Update stale states - this marks which operators were active in the previous frame.
        // Streams that weren't updated will be marked stale, but during export they won't be paused
        // (handled in SetStale). We use the stale flag to know which streams to include in the mix.
        AudioEngine.UpdateStaleStatesForExport();

        int sampleCount = (int)Math.Max(Math.Round(frameDurationInSeconds * AudioConfig.MixerFrequency), 1);
        int floatCount = sampleCount * 2; // stereo
        var mixBuffer = EnsureBuffer(ref _mixBuffer, floatCount);
        double currentTime = Playback.Current.TimeInSecs;

        // Mix soundtrack streams using BASS export mixer (handles resampling automatically)
        if (_exportMixerInitialized)
        {
            MixSoundtracksFromExportMixer(mixBuffer, floatCount, currentTime);
        }

        // Mix operator audio (always included)
        MixOperatorAudio(mixBuffer, floatCount, currentTime, frameDurationInSeconds);

        LogMixStats(mixBuffer, floatCount, currentTime);
        UpdateOperatorMetering();
        
        // Populate waveform buffers from export mixdown (maintains rolling window like live playback)
        // This ensures AudioWaveform, PlaybackFFT, AudioReaction, etc. work correctly during rendering
        WaveFormProcessing.PopulateFromExportBuffer(mixBuffer);
        
        // Compute FFT from the accumulated waveform buffer (same as live playback uses)
        var context = AudioAnalysisContext.Default;
        AudioAnalysis.ComputeFftFromBuffer(context.InterleavedSampleBuffer, context);
        
        // Process FFT data to compute frequency bands, peaks, and attacks for AudioReaction
        // Use the same gain/decay factors from playback settings as used during normal playback
        var settings = Playback.Current.Settings;
        float gainFactor = settings?.AudioGainFactor ?? 1f;
        float decayFactor = settings?.AudioDecayFactor ?? 0.9f;
        AudioAnalysis.ProcessUpdate(gainFactor, decayFactor);

        return mixBuffer;
    }

    /// <summary>
    /// Mixes all soundtrack clips using the BASS export mixer.
    /// BASS handles resampling from each clip's native frequency to the mixer frequency.
    /// </summary>
    private static void MixSoundtracksFromExportMixer(float[] mixBuffer, int floatCount, double currentTime)
    {
        // Position each soundtrack clip and set its volume based on whether it should be active
        foreach (var (handle, clipStream) in AudioEngine.SoundtrackClipStreams)
        {
            double clipStart = Playback.Current.SecondsFromBars(handle.Clip.StartTime);
            double timeInClip = currentTime - clipStart;
            
            // Check if clip is active at this time
            bool isActive = timeInClip >= 0 && timeInClip <= handle.Clip.LengthInSeconds;
            
            if (isActive)
            {
                // Position the stream at the correct time in the clip
                long targetBytes = Bass.ChannelSeconds2Bytes(clipStream.StreamHandle, timeInClip);
                Bass.ChannelSetPosition(clipStream.StreamHandle, targetBytes);
                
                // Apply volume: clip.Volume * SoundtrackPlaybackVolume * GlobalPlaybackVolume
                float effectiveVolume = handle.Clip.Volume 
                                        * ProjectSettings.Config.SoundtrackPlaybackVolume
                                        * ProjectSettings.Config.GlobalPlaybackVolume;
                Bass.ChannelSetAttribute(clipStream.StreamHandle, ChannelAttribute.Volume, effectiveVolume);
                
                // Unpause for this frame
                BassMix.ChannelFlags(clipStream.StreamHandle, 0, BassFlags.MixerChanPause);
            }
            else
            {
                // Pause clips that shouldn't be playing
                BassMix.ChannelFlags(clipStream.StreamHandle, BassFlags.MixerChanPause, BassFlags.MixerChanPause);
            }
        }
        
        // Read mixed audio from export mixer - BASS handles all resampling
        int bytesRead = Bass.ChannelGetData(_exportMixerHandle, mixBuffer, floatCount * sizeof(float));
        
        if (bytesRead < 0)
        {
            var error = Bass.LastError;
            if (error != Errors.OK && error != Errors.Ended)
            {
                Log.Gated.AudioRender($"[AudioRendering] Export mixer read error: {error}");
            }
        }
    }

    private static void MixOperatorAudio(float[] mixBuffer, int floatCount, double currentTime, double frameDuration)
    {
        // Mix all operator audio from the operator mixer
        // This includes:
        // - Procedural/callback streams (e.g., AudioToneGenerator) 
        // - File-based stereo streams (e.g., AudioPlayer) - these are NOT paused during export
        // The OperatorMixer is a decode-only mixer, so it doesn't output to soundcard
        var operatorBuffer = EnsureBuffer(ref _operatorBuffer, floatCount);
        int bytesRead = Bass.ChannelGetData(AudioMixerManager.OperatorMixerHandle, operatorBuffer, floatCount * sizeof(float));

        if (bytesRead > 0)
        {
            int samplesRead = bytesRead / sizeof(float);
            for (int i = 0; i < Math.Min(samplesRead, mixBuffer.Length); i++)
            {
                if (!float.IsNaN(operatorBuffer[i]))
                    mixBuffer[i] += operatorBuffer[i];
            }
        }

        // Mix spatial operator audio (these are not in the mixer - they use native 3D)
        // We need to render each spatial stream separately with manual 3D processing
        MixSpatialOperatorAudio(mixBuffer, currentTime, frameDuration);
    }

    private static void MixSpatialOperatorAudio(float[] mixBuffer, double currentTime, double frameDuration)
    {
        foreach (var kvp in AudioEngine.GetAllSpatialOperatorStates())
        {
            var stream = kvp.Value.Stream;
            if (stream == null || !stream.IsPlaying || stream.IsPaused || kvp.Value.IsStale)
                continue;

            // Use reusable buffer for this stream's contribution
            var streamBuffer = EnsureBuffer(ref _spatialStreamBuffer, mixBuffer.Length);
            
            // RenderAudio handles 3D attenuation and panning for export
            stream.RenderAudio(currentTime, frameDuration, streamBuffer, AudioConfig.MixerFrequency, 2);

            // Mix into the main buffer
            for (int i = 0; i < mixBuffer.Length; i++)
            {
                if (!float.IsNaN(streamBuffer[i]))
                    mixBuffer[i] += streamBuffer[i];
            }
        }
    }

    private static void LogMixStats(float[] mixBuffer, int floatCount, double currentTime)
    {
        if (_frameCount <= 3 || _frameCount % 60 == 0)
        {
            float peak = 0;
            for (int i = 0; i < floatCount; i++)
                if (!float.IsNaN(mixBuffer[i]))
                    peak = Math.Max(peak, Math.Abs(mixBuffer[i]));

            Log.Gated.AudioRender($"[AudioRendering] Frame {_frameCount}: peak={peak:F4}, time={currentTime:F3}s");
        }
    }


    private static void UpdateOperatorMetering()
    {
        UpdateStereoMeteringForStates(AudioEngine.GetAllStereoOperatorStates());
        UpdateSpatialMeteringForStates(AudioEngine.GetAllSpatialOperatorStates());
    }

    private static void UpdateStereoMeteringForStates(IEnumerable<KeyValuePair<Guid, (StereoOperatorAudioStream? Stream, bool IsStale)>> states)
    {
        foreach (var kvp in states)
        {
            var stream = kvp.Value.Stream;
            if (stream == null || !stream.IsPlaying || stream.IsPaused || kvp.Value.IsStale)
                continue;

            var level = BassMix.ChannelGetLevel(stream.StreamHandle);
            if (level != -1)
            {
                float left = (level & 0xFFFF) / 32768f;
                float right = ((level >> 16) & 0xFFFF) / 32768f;
                stream.UpdateFromBuffer([left, right]);
            }
        }
    }

    private static void UpdateSpatialMeteringForStates(IEnumerable<KeyValuePair<Guid, (SpatialOperatorAudioStream? Stream, bool IsStale)>> states)
    {
        foreach (var kvp in states)
        {
            var stream = kvp.Value.Stream;
            if (stream == null || !stream.IsPlaying || stream.IsPaused || kvp.Value.IsStale)
                continue;

            // For spatial streams during export, use Bass.ChannelGetLevel directly (not through mixer)
            var level = Bass.ChannelGetLevel(stream.StreamHandle);
            if (level != -1)
            {
                float left = (level & 0xFFFF) / 32768f;
                float right = ((level >> 16) & 0xFFFF) / 32768f;
                stream.UpdateFromBuffer([left, right]);
            }
        }
    }

    public static void GetLastMixDownBuffer(double frameDurationInSeconds)
    {
        AudioEngine.UpdateFftBufferFromSoundtrack(Playback.Current);
    }

    public static void EvaluateAllAudioMeteringOutputs(double localFxTime, float[]? audioBuffer = null)
    {
        var context = new EvaluationContext { LocalFxTime = localFxTime };

        foreach (var source in AudioExportSourceRegistry.Sources)
        {
            if (source is not Instance operatorInstance) continue;

            foreach (var input in operatorInstance.Inputs)
                input.DirtyFlag.ForceInvalidate();

            foreach (var output in operatorInstance.Outputs)
            {
                try { output.Update(context); }
                catch (Exception ex)
                {
                    Log.Gated.AudioRender($"Failed to evaluate output: {ex.Message}");
                }
            }
        }
    }

    private sealed class ExportState
    {
        private float _savedVolume;
        private bool _wasPlaying;

        public void SaveState()
        {
            Bass.ChannelGetAttribute(AudioMixerManager.GlobalMixerHandle, ChannelAttribute.Volume, out _savedVolume);
            if (_savedVolume <= 0) _savedVolume = 1.0f;
            _wasPlaying = Bass.ChannelIsActive(AudioMixerManager.GlobalMixerHandle) == PlaybackState.Playing;
        }

        public void RestoreState()
        {
            Bass.ChannelSetAttribute(AudioMixerManager.GlobalMixerHandle, ChannelAttribute.Volume, _savedVolume);
            if (_wasPlaying)
            {
                Bass.ChannelPlay(AudioMixerManager.GlobalMixerHandle);
                Log.Gated.AudioRender("[AudioRendering] GlobalMixer RESUMED");
            }
        }
    }
}