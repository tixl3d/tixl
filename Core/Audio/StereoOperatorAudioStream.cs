#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using T3.Core.Audio.Native;
using T3.Core.Logging;

namespace T3.Core.Audio;

/// <summary>
/// Represents a stereo audio stream for operator-based playback.
/// </summary>
public sealed class StereoOperatorAudioStream : OperatorAudioStreamBase
{
    /// <summary>Attempts to load a stereo audio stream from a file.</summary>
    internal static bool TryLoadStream(string filePath, [NotNullWhen(true)] out StereoOperatorAudioStream? stream)
    {
        stream = null;

        var engine = AudioMixerManager.NativeEngine;
        if (engine == null || !TryLoadSource(filePath, engine, out var source, out var duration, out var channels, out var sampleRate))
            return false;

        stream = new StereoOperatorAudioStream(engine, source, duration, filePath);
        Log.Gated.Audio($"[AudioPlayer] Loaded: '{Path.GetFileName(filePath)}' ({channels}ch, {sampleRate}Hz, {duration:F2}s)");
        return true;
    }

    /// <summary>Sets the stereo panning position (-1.0 = full left, 0.0 = center, 1.0 = full right).</summary>
    internal void SetPanning(float panning) => _engine.SetPan(Source, panning);

    private StereoOperatorAudioStream(NativeAudioEngine engine, AudioHandle source, double duration, string filePath)
        : base(engine, source, duration, filePath)
    {
        IsStaleStopped = true;
    }
}
