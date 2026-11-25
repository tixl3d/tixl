#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;

namespace Lib.Utils;

/// <summary>
/// Like <see cref="GeometryAttributeBlend"/>, but each output element is a weighted sum of any number of source
/// elements, e.g. a face center averaging all corners of its face. Numbers and vectors are summed, normals and
/// quaternions renormalized, ints take the source with the largest weight. Reuse one instance per domain and op.
/// </summary>
internal sealed class GeometryAttributeStencil
{
    public int Count { get; private set; }

    public void Clear()
    {
        Count = 0;
        _sourceCount = 0;
    }

    /// <summary>Starts the next output element; follow with one <see cref="Push"/> per contributing source.</summary>
    public void Begin()
    {
        if (Count + 1 >= _starts.Length)
            Array.Resize(ref _starts, Math.Max(16, _starts.Length * 2));

        _starts[Count++] = _sourceCount;
        _starts[Count] = _sourceCount;
    }

    public void Push(int sourceIndex, float weight)
    {
        if (_sourceCount == _indices.Length)
        {
            var size = Math.Max(16, _sourceCount * 2);
            Array.Resize(ref _indices, size);
            Array.Resize(ref _weights, size);
        }

        _indices[_sourceCount] = sourceIndex;
        _weights[_sourceCount] = weight;
        _sourceCount++;
        _starts[Count] = _sourceCount;
    }

    public void AddCopy(int a)
    {
        Begin();
        Push(a, 1);
    }

    public void AddMidpoint(int a, int b)
    {
        Begin();
        Push(a, 0.5f);
        Push(b, 0.5f);
    }

    /// <summary>Creates every attribute of <paramref name="domain"/> on <paramref name="target"/>, sized to <see cref="Count"/>.</summary>
    /// <param name="skipNames">Attributes the op writes itself or drops.</param>
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
                    FillFloats(floats, target.GetOrCreate<float>(attribute.Name, domain, Count, attribute.TypeInfo).Values);
                    break;
                case GeometryAttribute<int> ints:
                    FillInts(ints, target.GetOrCreate<int>(attribute.Name, domain, Count, attribute.TypeInfo).Values);
                    break;
                case GeometryAttribute<Vector2> vectors2:
                    FillVectors2(vectors2, target.GetOrCreate<Vector2>(attribute.Name, domain, Count, attribute.TypeInfo).Values);
                    break;
                case GeometryAttribute<Vector3> vectors3:
                    FillVectors3(vectors3, target.GetOrCreate<Vector3>(attribute.Name, domain, Count, attribute.TypeInfo).Values,
                                 attribute.TypeInfo == AttributeTypeInfos.Normal);
                    break;
                case GeometryAttribute<Vector4> vectors4:
                    FillVectors4(vectors4, target.GetOrCreate<Vector4>(attribute.Name, domain, Count, attribute.TypeInfo).Values,
                                 attribute.TypeInfo == AttributeTypeInfos.Quaternion);
                    break;
            }
        }
    }

    private void FillFloats(GeometryAttribute<float> source, float[] result)
    {
        var values = source.Values;
        for (var i = 0; i < Count; i++)
        {
            var sum = 0f;
            for (var s = _starts[i]; s < _starts[i + 1]; s++)
                sum += values[_indices[s]] * _weights[s];

            result[i] = sum;
        }
    }

    private void FillInts(GeometryAttribute<int> source, int[] result)
    {
        var values = source.Values;
        for (var i = 0; i < Count; i++)
        {
            if (_starts[i + 1] == _starts[i])
            {
                result[i] = 0;
                continue;
            }

            var best = _starts[i];
            for (var s = _starts[i] + 1; s < _starts[i + 1]; s++)
            {
                if (_weights[s] > _weights[best])
                    best = s;
            }

            result[i] = values[_indices[best]];
        }
    }

    private void FillVectors2(GeometryAttribute<Vector2> source, Vector2[] result)
    {
        var values = source.Values;
        for (var i = 0; i < Count; i++)
        {
            var sum = Vector2.Zero;
            for (var s = _starts[i]; s < _starts[i + 1]; s++)
                sum += values[_indices[s]] * _weights[s];

            result[i] = sum;
        }
    }

    private void FillVectors3(GeometryAttribute<Vector3> source, Vector3[] result, bool normalize)
    {
        var values = source.Values;
        for (var i = 0; i < Count; i++)
        {
            var sum = Vector3.Zero;
            for (var s = _starts[i]; s < _starts[i + 1]; s++)
                sum += values[_indices[s]] * _weights[s];

            result[i] = normalize && sum.LengthSquared() > 1e-12f ? Vector3.Normalize(sum) : sum;
        }
    }

    /// <param name="isQuaternion">Sources are sign-aligned to the first one before summing, then renormalized.</param>
    private void FillVectors4(GeometryAttribute<Vector4> source, Vector4[] result, bool isQuaternion)
    {
        var values = source.Values;
        for (var i = 0; i < Count; i++)
        {
            var sum = Vector4.Zero;
            var first = _starts[i] < _starts[i + 1] ? values[_indices[_starts[i]]] : Vector4.Zero;
            for (var s = _starts[i]; s < _starts[i + 1]; s++)
            {
                var value = values[_indices[s]];
                if (isQuaternion && Vector4.Dot(first, value) < 0)
                    value = -value;

                sum += value * _weights[s];
            }

            result[i] = isQuaternion && sum.LengthSquared() > 1e-12f ? Vector4.Normalize(sum) : sum;
        }
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

    private int[] _starts = new int[16];
    private int[] _indices = [];
    private float[] _weights = [];
    private int _sourceCount;
}
