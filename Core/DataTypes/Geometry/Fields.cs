#nullable enable
using System;

namespace T3.Core.DataTypes;

/// <summary>
/// The evaluation context handed to CPU field delegates. A struct so it can grow
/// (element id, seed, ...) without breaking the delegate signatures.
/// </summary>
public readonly struct FieldSample(Vector3 position)
{
    public readonly Vector3 Position = position;
}

public delegate float ScalarFieldFn(in FieldSample sample);
public delegate float RemapCurveFn(float value);
public delegate Vector3 VectorFieldFn(in FieldSample sample);

/// <summary>
/// A per-sample scalar function flowing through the graph as a connection
/// (conceptually like Blender's fields): consumers evaluate it lazily at their
/// element positions. Callable-first by design; <see cref="DescriptionNode"/> is
/// reserved for a future structural representation (fusion / HLSL translation) -
/// a bare delegate is forever opaque, the optional node keeps that door open.
///
/// Delegates must be pure and thread-safe: capture immutable snapshots or read
/// the producing op's state object, never mutate on evaluation.
/// </summary>
public sealed class ScalarField(ScalarFieldFn evaluate)
{
    public readonly ScalarFieldFn Evaluate = evaluate;
    public object? DescriptionNode;

    public float Sample(Vector3 position) => Evaluate(new FieldSample(position));
}

/// <summary>A scalar remapping function (gain/bias, curves) flowing as a connection.</summary>
public sealed class RemapCurve(RemapCurveFn remap)
{
    public readonly RemapCurveFn Remap = remap;
    public object? DescriptionNode;
}

/// <summary>A per-sample vector function flowing as a connection. See <see cref="ScalarField"/>.</summary>
public sealed class VectorField(VectorFieldFn evaluate)
{
    public readonly VectorFieldFn Evaluate = evaluate;
    public object? DescriptionNode;

    public Vector3 Sample(Vector3 position) => Evaluate(new FieldSample(position));
}
