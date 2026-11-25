#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;

namespace Lib.Utils;

/// <summary>
/// Carries the attributes of one domain through an op that creates, drops or splits elements. The op records, per
/// output element, the two source elements it lies between and how far (<see cref="Add"/>), then
/// <see cref="Apply"/> fills every attribute of that domain: numbers and vectors are interpolated, quaternions
/// slerped, ints take the nearer source. Reuse one instance per domain and op; it keeps its buffers.
/// </summary>
internal sealed class GeometryAttributeBlend
{
    public int Count { get; private set; }

    public void Clear() => Count = 0;

    /// <summary>The next output element lies at <paramref name="t"/> between source elements a and b.</summary>
    public void Add(int a, int b, float t)
    {
        if (Count == _a.Length)
        {
            var size = Math.Max(16, Count * 2);
            Array.Resize(ref _a, size);
            Array.Resize(ref _b, size);
            Array.Resize(ref _t, size);
        }

        _a[Count] = a;
        _b[Count] = b;
        _t[Count] = t;
        Count++;
    }

    /// <summary>The next output element is a copy of source element a.</summary>
    public void AddCopy(int a) => Add(a, a, 0);

    /// <summary>
    /// Creates every attribute of <paramref name="domain"/> from <paramref name="source"/> on <paramref name="target"/>,
    /// sized to <see cref="Count"/>, keeping name and type info.
    /// </summary>
    /// <param name="skipNames">Attributes the op writes itself, e.g. curve handles.</param>
    public void Apply(GeometryAttributes source, GeometryAttributes target, AttributeDomain domain, IReadOnlyList<string>? skipNames = null)
    {
        for (var index = 0; index < source.Count; index++)
        {
            var attribute = source[index];
            if (attribute.Domain != domain || IsSkipped(attribute.Name, skipNames))
                continue;

            switch (attribute)
            {
                case GeometryAttribute<float> floats:
                    Fill(floats, target, static (a, b, t) => a + (b - a) * t);
                    break;
                case GeometryAttribute<int> ints:
                    Fill(ints, target, static (a, b, t) => t < 0.5f ? a : b);
                    break;
                case GeometryAttribute<Vector2> vectors2:
                    Fill(vectors2, target, static (a, b, t) => Vector2.Lerp(a, b, t));
                    break;
                case GeometryAttribute<Vector3> vectors3:
                    Fill(vectors3, target, static (a, b, t) => Vector3.Lerp(a, b, t));
                    break;
                case GeometryAttribute<Vector4> vectors4 when vectors4.TypeInfo == AttributeTypeInfos.Quaternion:
                    Fill(vectors4, target, static (a, b, t) => SlerpAsVector(a, b, t));
                    break;
                case GeometryAttribute<Vector4> vectors4:
                    Fill(vectors4, target, static (a, b, t) => Vector4.Lerp(a, b, t));
                    break;
            }
        }
    }

    private void Fill<T>(GeometryAttribute<T> source, GeometryAttributes target, Func<T, T, float, T> blend) where T : unmanaged
    {
        var result = target.GetOrCreate<T>(source.Name, source.Domain, Count, source.TypeInfo).Values;
        var values = source.Values;
        for (var i = 0; i < Count; i++)
        {
            var a = values[_a[i]];
            result[i] = _a[i] == _b[i] ? a : blend(a, values[_b[i]], _t[i]);
        }
    }

    private static Vector4 SlerpAsVector(Vector4 a, Vector4 b, float t)
    {
        var q = Quaternion.Slerp(new Quaternion(a.X, a.Y, a.Z, a.W), new Quaternion(b.X, b.Y, b.Z, b.W), t);
        return new Vector4(q.X, q.Y, q.Z, q.W);
    }

    private static bool IsSkipped(string name, IReadOnlyList<string>? skipNames)
    {
        if (skipNames == null)
            return false;

        for (var i = 0; i < skipNames.Count; i++)
        {
            if (string.Equals(name, skipNames[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private int[] _a = [];
    private int[] _b = [];
    private float[] _t = [];
}
