#nullable enable
using System;
using System.Collections.Generic;

namespace T3.Core.Audio.Input;

/// <summary>Receives one block of captured samples, straight from the backend's callback thread.</summary>
public delegate void AudioCaptureCallback(IntPtr buffer, int lengthInBytes);

/// <summary>
/// Captures audio from a device so <see cref="AudioAnalysis"/> can react to something other than the
/// project's own soundtrack.
/// </summary>
/// <remarks>
/// WASAPI is Windows-only, so elsewhere capture goes through BASS's own recording API. The two differ in
/// what they can reach: WASAPI enumerates loopback devices, BASS on Linux sees only real inputs, because
/// neither ALSA nor the PulseAudio monitor sources surface as recording devices to it.
/// </remarks>
public abstract class AudioInputBackend
{
    public static AudioInputBackend Current { get; } = CreateForPlatform();

    /// <summary>Named in logs so an unexpected device list can be traced to the backend that produced it.</summary>
    public abstract string Name { get; }

    /// <summary>False when this system cannot capture at all; callers then skip input silently.</summary>
    public abstract bool IsAvailable { get; }

    /// <summary>Channels the running capture settled on, which the WAV writer needs for its interleave.</summary>
    public abstract int ActiveChannelCount { get; }

    public abstract void EnumerateDevices(List<AudioInputDevice> devices);

    public abstract bool TryStartCapture(AudioInputDevice device, AudioCaptureCallback onData);

    public abstract void StopCapture();

    /// <summary>
    /// Reads from the capture queue. <paramref name="lengthOrFlags"/> is a byte count for raw samples or a
    /// <c>DataFlags</c> value for an FFT, exactly as BASS overloads it. Negative results mean failure.
    /// </summary>
    public abstract int GetData(float[] buffer, int lengthOrFlags);

    /// <summary>Per-channel levels packed as <c>0xRRRRLLLL</c>, or -1 when the capture errored.</summary>
    public abstract int GetLevel();

    private static AudioInputBackend CreateForPlatform()
    {
        if (OperatingSystem.IsWindows() && BassLibrary.IsWasapiAvailable)
        {
            return new WasapiAudioInputBackend();
        }

        return new BassRecordAudioInputBackend();
    }
}
