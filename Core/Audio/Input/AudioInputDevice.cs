#nullable enable
namespace T3.Core.Audio.Input;

/// <summary>
/// An audio capture device offered by the native engine.
/// </summary>
public sealed class AudioInputDevice
{
    /// <summary>What the user picks in settings, so it has to stay stable across a rescan.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// True when the device captures what the system is playing rather than a physical input: an output
    /// recorded through WASAPI loopback, or a PipeWire / PulseAudio monitor source.
    /// </summary>
    public bool IsLoopback { get; init; }

    /// <summary>Position in the native device list, which is enumerated again when opening.</summary>
    internal int NativeIndex { get; init; }
}
