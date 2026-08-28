#nullable enable
using ManagedBass;
using ManagedBass.Mix;

namespace T3.Core.Audio;

/// <summary>
/// Analyses a single BASS channel into the same buffers <see cref="AudioAnalysis"/> produces for the global
/// mix, so an operator can react to one specific source or bus instead of everything that is audible.
///
/// The channel must be a mixer source added with <c>MixerChanBuffer</c> — which is how <c>[AudioBus]</c>
/// realises its submixes. That is what makes the read non-consuming: the plain
/// <see cref="Bass.ChannelGetData(int,float[],int)"/> would take the samples away from playback, whereas the
/// mixer variant inspects the buffer the mixer already holds.
/// </summary>
public sealed class ChannelAudioAnalysis
{
    public float[] FftGain => _context.FftGainBuffer;
    public float[] FftNormalized => _context.FftNormalizedBuffer;
    public float[] FrequencyBands => _context.FrequencyBands;
    public float[] FrequencyBandPeaks => _context.FrequencyBandPeaks;
    public float[] FrequencyBandAttacks => _context.FrequencyBandAttacks;

    /// <summary>
    /// Refreshes the buffers from <paramref name="channel"/>. Pass 0 (or an unrealised channel) to leave the
    /// buffers cleared, so an unrouted source reads as silence rather than holding its last values.
    /// </summary>
    public void Update(int channel, float gainFactor = 1f, float decayFactor = 0.9f)
    {
        if (channel != 0 && BassMix.ChannelGetData(channel, _context.FftGainBuffer, (int)DataFlags.FFT2048) >= 0)
        {
            _cleared = false;
            _context.ProcessFftUpdate(gainFactor, decayFactor);
            return;
        }

        // Nothing readable — unrouted, or a channel the mixer isn't buffering. Read as silence rather than
        // holding the last values, which would look like a signal that is still playing.
        if (_cleared)
            return;

        _context.Reset();
        _cleared = true;
    }

    private readonly AudioAnalysisContext _context = new();
    private bool _cleared;
}
