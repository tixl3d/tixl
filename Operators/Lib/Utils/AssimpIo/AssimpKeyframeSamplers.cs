#nullable enable
using SharpGLTF.Animations;

namespace Lib.Utils.AssimpIo;

/// <summary>
/// Keyframe tracks extracted from Assimp animation channels, exposed through the curve sampler
/// interface that <see cref="SceneSetup.JointAnimChannel"/> uses for glTF clips. Sampling is
/// allocation free (binary search over sorted key times), so it is safe in per-frame code.
/// </summary>
internal abstract class AssimpKeyframeSampler<T> : ICurveSampler<T>
{
    protected AssimpKeyframeSampler(float[] times, T[] values, bool[] isStep)
    {
        _times = times;
        _values = values;
        _isStep = isStep;
    }

    public T GetPoint(float offset)
    {
        var count = _times.Length;
        if (count == 1 || offset <= _times[0])
            return _values[0];

        if (offset >= _times[count - 1])
            return _values[count - 1];

        var upper = Array.BinarySearch(_times, offset);
        if (upper >= 0)
            return _values[upper];

        upper = ~upper;
        var lower = upper - 1;
        if (_isStep[lower])
            return _values[lower];

        var duration = _times[upper] - _times[lower];
        var fraction = duration > 0 ? (offset - _times[lower]) / duration : 0;
        return Interpolate(_values[lower], _values[upper], fraction);
    }

    /// <summary>Key times in seconds, ascending. Exposed for serialization only.</summary>
    public ReadOnlySpan<float> Times => _times;

    public ReadOnlySpan<T> Values => _values;
    public ReadOnlySpan<bool> IsStep => _isStep;

    protected abstract T Interpolate(T a, T b, float fraction);

    private readonly float[] _times;
    private readonly T[] _values;
    private readonly bool[] _isStep;
}

internal sealed class AssimpVector3Sampler(float[] times, Vector3[] values, bool[] isStep)
    : AssimpKeyframeSampler<Vector3>(times, values, isStep)
{
    protected override Vector3 Interpolate(Vector3 a, Vector3 b, float fraction) => Vector3.Lerp(a, b, fraction);
}

internal sealed class AssimpQuaternionSampler(float[] times, Quaternion[] values, bool[] isStep)
    : AssimpKeyframeSampler<Quaternion>(times, values, isStep)
{
    protected override Quaternion Interpolate(Quaternion a, Quaternion b, float fraction) => Quaternion.Slerp(a, b, fraction);
}
