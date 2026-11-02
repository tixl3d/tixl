#nullable enable
namespace T3.Core.Audio.Native;

/// <summary>
/// A waveform with an ADSR envelope, computed natively on the audio thread. It starts unrouted; the audio graph
/// places it through <see cref="Handle"/>. Silent until <see cref="TriggerAttack"/>.
/// </summary>
public sealed class ToneGeneratorSource
{
    public AudioHandle Handle { get; }

    public bool IsActive => _engine.IsGeneratorActive(Handle);
    public float Level => _engine.GetLevel(Handle);

    /// <summary>Null while audio is unavailable.</summary>
    public static ToneGeneratorSource? TryCreate()
    {
        var engine = AudioMixerManager.NativeEngine;
        if (engine == null)
            return null;

        var handle = engine.CreateGenerator();
        return handle.IsValid ? new ToneGeneratorSource(engine, handle) : null;
    }

    /// <param name="durationSecs">Negative sustains until <see cref="TriggerRelease"/>.</param>
    public void SetParameters(float frequency, int waveform, float attackSecs, float decaySecs, float sustainLevel, float releaseSecs, float durationSecs)
        => _engine.SetGenerator(Handle, frequency, waveform, attackSecs, decaySecs, sustainLevel, releaseSecs, durationSecs);

    public void TriggerAttack() => _engine.TriggerGeneratorAttack(Handle);
    public void TriggerRelease() => _engine.TriggerGeneratorRelease(Handle);
    public void Reset() => _engine.ResetGenerator(Handle);

    /// <summary>Safe from a finalizer: the release happens on the main thread's next frame.</summary>
    public void Dispose() => _engine.ReleaseDeferred(Handle);

    private ToneGeneratorSource(NativeAudioEngine engine, AudioHandle handle)
    {
        _engine = engine;
        Handle = handle;
    }

    private readonly NativeAudioEngine _engine;
}
