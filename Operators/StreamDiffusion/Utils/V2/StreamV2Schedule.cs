namespace t3.streamdiffusion.V2;

/// <summary>
/// Flow-matching timestep/sigma table the V2 generator was distilled with
/// (1000 training steps, shift 8, sigma from 1 towards 0).
/// </summary>
internal sealed class FlowMatchSchedule
{
    public const int TrainTimestepCount = 1000;

    private readonly double[] _sigmas = new double[TrainTimestepCount];
    private readonly double[] _timesteps = new double[TrainTimestepCount];

    public FlowMatchSchedule(double shift = 8.0)
    {
        for (var index = 0; index < TrainTimestepCount; index++)
        {
            var linear = 1.0 - index / (double)TrainTimestepCount;
            var shifted = shift * linear / (1.0 + (shift - 1.0) * linear);
            _sigmas[index] = shifted;
            _timesteps[index] = shifted * TrainTimestepCount;
        }
    }

    /// <summary>Sigma of the table entry whose timestep is closest to <paramref name="timestep"/>.</summary>
    public float SigmaAt(int timestep)
    {
        var closest = 0;
        var closestDistance = double.MaxValue;
        for (var index = 0; index < TrainTimestepCount; index++)
        {
            var distance = Math.Abs(_timesteps[index] - timestep);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = index;
            }
        }

        return (float)_sigmas[closest];
    }

    /// <summary>Writes <c>(1 - sigma) * clean + sigma * noise</c>; <paramref name="destination"/> may alias an input.</summary>
    public void AddNoise(ReadOnlySpan<float> clean, ReadOnlySpan<float> noise, int timestep, Span<float> destination)
    {
        var sigma = SigmaAt(timestep);
        for (var index = 0; index < destination.Length; index++)
            destination[index] = (1f - sigma) * clean[index] + sigma * noise[index];
    }
}

/// <summary>
/// Where each latent frame's keys/values live in the fixed-size KV cache. The first
/// <c>SinkFrames</c> frames stay forever; later frames rotate through the remaining slots.
/// </summary>
internal static class KvSlotPolicy
{
    public static int SlotForFrame(int frame, int cacheFrames, int sinkFrames)
    {
        if (frame < cacheFrames)
            return frame;

        return sinkFrames + (frame - sinkFrames) % (cacheFrames - sinkFrames);
    }
}

/// <summary>Per-denoising-element view of which cache slots hold a frame and which frame it is.</summary>
internal sealed class KvCacheState
{
    public const int EmptySlot = -1;

    private readonly int[] _frameInSlot;

    public KvCacheState(int cacheFrames)
    {
        _frameInSlot = new int[cacheFrames];
        Reset();
    }

    public int SlotCount => _frameInSlot.Length;

    public void Reset() => Array.Fill(_frameInSlot, EmptySlot);

    public void MarkWritten(int slot, int frame) => _frameInSlot[slot] = frame;

    /// <summary>First slot without a frame, or -1 when the cache is full; used to park the writes of idle batch elements.</summary>
    public int FindEmptySlot()
    {
        for (var slot = 0; slot < _frameInSlot.Length; slot++)
        {
            if (_frameInSlot[slot] == EmptySlot)
                return slot;
        }

        return -1;
    }

    /// <summary>Additive attention mask over cache tokens: 0 for slots holding a frame, -inf for empty ones and the slot about to be overwritten.</summary>
    public void FillMask(Span<float> mask, int tokensPerFrame, int overwrittenSlot)
    {
        for (var slot = 0; slot < _frameInSlot.Length; slot++)
        {
            var visible = _frameInSlot[slot] != EmptySlot && slot != overwrittenSlot;
            mask.Slice(slot * tokensPerFrame, tokensPerFrame).Fill(visible ? 0f : float.NegativeInfinity);
        }
    }
}

/// <summary>
/// Adapts the input noise level to scene motion: fast motion lowers it so the output follows the input,
/// still frames let the prompt take over.
/// </summary>
internal static class MotionNoiseController
{
    public const float DefaultNoiseScale = 0.8f;

    /// <summary>
    /// <paramref name="frameChange"/> is the RMS pixel change between consecutive input frames in [-1, 1] units.
    /// Returns the smoothed noise scale and the timestep for the newest batch element.
    /// </summary>
    public static (float NoiseScale, int Timestep) Update(float frameChange, float previousNoiseScale, float initialNoiseScale)
    {
        // Double precision keeps the truncation to an integer timestep identical to the reference (0.8 must give 700, not 699)
        var motion = Math.Clamp(frameChange / 0.2, 0.0, 1.0);
        var noiseScale = (initialNoiseScale - 0.1 * motion) * 0.9 + previousNoiseScale * 0.1;
        return ((float)noiseScale, (int)(1000.0 * noiseScale) - 100);
    }
}
