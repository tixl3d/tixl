#nullable enable
using System;

namespace T3.Core.Audio;

/// <summary>
/// Downmixes interleaved float audio to mono and resamples it linearly to a fixed target rate, chunk by chunk.
/// Carries the fractional position and the last sample across chunks so the output is seamless.
/// Allocation-free in steady state; safe to call from an audio callback.
/// </summary>
internal sealed class MonoResampler
{
    public MonoResampler(int targetSampleRate)
    {
        _targetSampleRate = targetSampleRate;
    }

    public void Reset()
    {
        _position = 0;
        _lastMonoSample = 0;
    }

    /// <summary>
    /// Appends the resampled mono samples of one chunk to <paramref name="output"/> starting at
    /// <paramref name="outputCount"/>, growing the array when needed.
    /// </summary>
    /// <returns>The number of samples appended.</returns>
    public int Append(ReadOnlySpan<float> interleaved, int channelCount, int sourceSampleRate, ref float[] output, int outputCount)
    {
        var frameCount = interleaved.Length / channelCount;
        if (frameCount == 0)
            return 0;

        // Index 0 holds the previous chunk's last sample so interpolation can reach back across the boundary.
        var monoCount = frameCount + 1;
        if (_monoBuffer.Length < monoCount)
            _monoBuffer = new float[monoCount * 2];

        _monoBuffer[0] = _lastMonoSample;
        var channelScale = 1f / channelCount;
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var sum = 0f;
            var baseIndex = frameIndex * channelCount;
            for (var channel = 0; channel < channelCount; channel++)
            {
                sum += interleaved[baseIndex + channel];
            }

            _monoBuffer[frameIndex + 1] = sum * channelScale;
        }

        _lastMonoSample = _monoBuffer[monoCount - 1];

        var step = sourceSampleRate / (double)_targetSampleRate;
        var maxOutput = (int)(frameCount / step) + 2;
        if (output.Length < outputCount + maxOutput)
            Array.Resize(ref output, Math.Max(output.Length * 2, outputCount + maxOutput));

        var appended = 0;
        var pos = _position;
        while (pos + 1 < monoCount)
        {
            var index = (int)pos;
            var frac = (float)(pos - index);
            var a = _monoBuffer[index];
            var b = _monoBuffer[index + 1];
            output[outputCount + appended] = a + (b - a) * frac;
            appended++;
            pos += step;
        }

        _position = pos - (monoCount - 1);
        return appended;
    }

    private readonly int _targetSampleRate;
    private float[] _monoBuffer = new float[4096];
    private double _position;
    private float _lastMonoSample;
}
