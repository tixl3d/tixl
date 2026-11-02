#nullable enable
using T3.Core.Audio.Native;

namespace T3.Core.Audio;

/// <summary>
/// Analyses a single source or bus into the same buffers <see cref="AudioAnalysis"/> produces for the global
/// mix, so an operator can react to one specific source or bus instead of everything that is audible.
/// Reading doesn't consume anything, because every source and bus keeps its own history in the native engine.
/// </summary>
public sealed class ChannelAudioAnalysis
{
    public float[] FftGain => _context.FftGainBuffer;
    public float[] FftNormalized => _context.FftNormalizedBuffer;
    public float[] FrequencyBands => _context.FrequencyBands;
    public float[] FrequencyBandPeaks => _context.FrequencyBandPeaks;
    public float[] FrequencyBandAttacks => _context.FrequencyBandAttacks;

    /// <summary>
    /// Refreshes the buffers from <paramref name="source"/>. Pass the default handle to leave the buffers
    /// cleared, so an unrouted source reads as silence rather than holding its last values.
    /// </summary>
    public void Update(AudioHandle source, float gainFactor = 1f, float decayFactor = 0.9f)
    {
        var engine = AudioMixerManager.NativeEngine;
        if (engine != null && source.IsValid && engine.ReadLatestMono(source, _samples))
        {
            _fft.ComputeMagnitudes(_samples, _context.FftGainBuffer);
            _cleared = false;
            _context.ProcessFftUpdate(gainFactor, decayFactor);
            return;
        }

        if (_cleared)
            return;

        _context.Reset();
        _cleared = true;
    }

    private readonly AudioAnalysisContext _context = new();
    private readonly Fft _fft = new(AudioConfig.FftBufferSize * 2);
    private readonly float[] _samples = new float[AudioConfig.FftBufferSize * 2];
    private bool _cleared;
}
