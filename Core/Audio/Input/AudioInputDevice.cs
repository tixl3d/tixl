#nullable enable
namespace T3.Core.Audio.Input;

/// <summary>
/// An audio capture device offered by the active <see cref="AudioInputBackend"/>.
/// </summary>
public sealed class AudioInputDevice
{
    /// <summary>What the user picks in settings, so it has to stay stable across a rescan.</summary>
    public required string Name { get; init; }

    /// <summary>The rate the device mixes at; capture is opened at this rate.</summary>
    public int SampleRate { get; init; }

    /// <summary>
    /// True when the device captures what the system is playing rather than a physical input.
    /// Only WASAPI offers these; ALSA and PulseAudio monitor sources are not reachable through BASS.
    /// </summary>
    public bool IsLoopback { get; init; }

    /// <summary>How the backend that enumerated this device identifies it again when opening.</summary>
    internal int BackendIndex { get; init; }

    /// <summary>WASAPI's shortest callback interval, which sizes its buffer. Unused by other backends.</summary>
    internal double MinimumUpdatePeriod { get; init; }
}
