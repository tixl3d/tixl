#nullable enable
using System;
using System.Collections.Generic;
using ManagedBass;
using ManagedBass.Mix;
using T3.Core.Animation;
using T3.Core.Audio.Native;
using T3.Core.IO;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Settings;

namespace T3.Core.Audio;

/// <summary>
/// Handles audio rendering/export functionality.
/// For export, we temporarily remove soundtrack streams from the mixer and read directly from them.
/// </summary>
public static class AudioRendering
{
    /// <summary>True while a video/image-sequence export is capturing audio.</summary>
    public static bool IsRecording => _isRecording;

    private static bool _isRecording;
    private static readonly ExportState _exportState = new();
    private static readonly List<IAudioExportSource> _exportEvaluationSources = new();
    private static int _frameCount;

    // Reusable buffers to reduce per-frame allocations during export
    private static float[] _mixBuffer = Array.Empty<float>();
    
    private static readonly Fft _exportFft = new(AudioConfig.FftBufferSize * 2);

    /** The newest mono samples of the export mix: a video frame holds fewer samples than one FFT window. */
    private static readonly float[] _exportFftSamples = new float[AudioConfig.FftBufferSize * 2];

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

        // Snapshot which audio ops (buses) were organically evaluated when export starts — those get
        // force-evaluated every exported frame so graph audio keeps flowing even though export only
        // evaluates the exported op-chain. Ops that were already silent/stale stay out, matching live.
        _exportEvaluationSources.Clear();
        foreach (var source in AudioExportSourceRegistry.Sources)
        {
            if (source.IsActiveForExport)
                _exportEvaluationSources.Add(source);
        }

        Log.Gated.AudioRender($"[AudioRendering] Export evaluation sources: {_exportEvaluationSources.Count} of {AudioExportSourceRegistry.Sources.Count} registered");

        // Clear what live playback left ringing (effect tails, buffered samples) so the render doesn't open
        // with a remnant of what was last heard — and so two renders of the same range match.
        foreach (var source in _exportEvaluationSources)
            source.ResetForExport();

        // Reset audio analysis state for clean export - ensures both modes start from same state
        AudioAnalysisContext.Default.Reset();
        WaveFormProcessing.ResetExportBuffer();

        Bass.ChannelPause(AudioMixerManager.GlobalMixerHandle);
        Log.Gated.AudioRender("[AudioRendering] GlobalMixer PAUSED for export");

        AudioEngine.ResetAllOperatorStreamsForExport();

        Array.Clear(_exportFftSamples);

        // The export pulls the engine itself, so the graph keeps its state while no device runs.
        AudioMixerManager.NativeEngine?.DetachDevice();
        foreach (var (_, clipStream) in AudioEngine.SoundtrackClipStreams)
            clipStream.PrepareForExport();

        Log.Gated.AudioRender($"[AudioRendering] PrepareRecording: fps={fps}");
    }

    public static void EndRecording(Playback playback, double fps)
    {
        if (!_isRecording) return;

        _isRecording = false;
        Log.Gated.AudioRender($"[AudioRendering] EndRecording: Exported {_frameCount} frames");

        var engine = AudioMixerManager.NativeEngine;
        if (engine != null && !engine.AttachDefaultDevice())
            Log.Warning("[AudioRendering] Could not reopen the audio device after the export; audio stays silent.");

        // The speed only gets reapplied when it changes, and the export has reset it to 1.
        foreach (var (_, clipStream) in AudioEngine.SoundtrackClipStreams)
            clipStream.UpdateSoundtrackPlaybackSpeed(playback.PlaybackSpeed);

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

        RenderNativeGraph(mixBuffer, sampleCount, currentTime);

        // Mix operator audio (always included)

        LogMixStats(mixBuffer, floatCount, currentTime);
        
        // Populate waveform buffers from export mixdown (maintains rolling window like live playback)
        // This ensures AudioWaveform, PlaybackFFT, AudioReaction, etc. work correctly during rendering
        WaveFormProcessing.PopulateFromExportBuffer(mixBuffer);
        
        var context = AudioAnalysisContext.Default;
        UpdateExportFft(mixBuffer, sampleCount, context);
        
        // Process FFT data to compute frequency bands, peaks, and attacks for AudioReaction
        // Use the same gain/decay factors from playback settings as used during normal playback
        var settings = Playback.Current.Settings;
        float gainFactor = settings?.Playback.AudioGainFactor ?? 1f;
        float decayFactor = settings?.Playback.AudioDecayFactor ?? 0.9f;
        AudioAnalysis.ProcessUpdate(gainFactor, decayFactor);

        return mixBuffer;
    }

    /// <summary>Renders the whole native graph: soundtracks, operators, buses and video audio.</summary>
    private static void RenderNativeGraph(float[] mixBuffer, int frameCount, double currentTime)
    {
        var engine = AudioMixerManager.NativeEngine;
        if (engine == null)
            return;

        foreach (var (_, clipStream) in AudioEngine.SoundtrackClipStreams)
            clipStream.UpdateForExport(currentTime);

        engine.Render(mixBuffer, (uint)frameCount);
    }

    private static void UpdateExportFft(float[] mixBuffer, int frameCount, AudioAnalysisContext context)
    {
        var history = _exportFftSamples;
        var newFrames = Math.Min(frameCount, history.Length);
        Array.Copy(history, newFrames, history, 0, history.Length - newFrames);

        var firstFrame = frameCount - newFrames;
        var writeIndex = history.Length - newFrames;
        for (var frame = 0; frame < newFrames; frame++)
        {
            var left = mixBuffer[(firstFrame + frame) * 2];
            var right = mixBuffer[(firstFrame + frame) * 2 + 1];
            history[writeIndex + frame] = (left + right) * 0.5f;
        }

        _exportFft.ComputeMagnitudes(history, context.FftGainBuffer);
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


    public static void GetLastMixDownBuffer(double frameDurationInSeconds)
    {
        AudioEngine.UpdateFftBufferFromSoundtrack(Playback.Current);
    }

    public static void EvaluateAllAudioMeteringOutputs(double localFxTime, float[]? audioBuffer = null)
    {
        var context = new EvaluationContext { LocalFxTime = localFxTime };

        foreach (var source in _exportEvaluationSources)
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