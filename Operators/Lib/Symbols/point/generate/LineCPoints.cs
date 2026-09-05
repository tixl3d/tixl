using System;
using T3.Core.Utils;

namespace Lib.point.generate;

/// <summary>
/// CPU twin of [LinePoints]: generates points along a line with the same
/// parameters and math as the GPU op, for graphs that need the list on the CPU
/// (geometry ops, fields, fracture seeds).
/// </summary>
[Guid("796a5efb-2ccf-4cae-b01c-d3f20a070181")]
internal sealed class LineCPoints : Instance<LineCPoints>, ITransformable
{
    [Output(Guid = "C8E35D0A-7B10-42D8-9984-006502195FDE")]
    public readonly TransformCallbackSlot<StructuredList> PointList = new();

    [Output(Guid = "A67DF589-3C51-49A7-805D-5CC0657D491C")]
    public readonly Slot<Point[]> Result = new();

    public LineCPoints()
    {
        PointList.TransformableOp = this;
        PointList.UpdateAction = Update;
        Result.UpdateAction = Update;
    }

    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;

    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    private void Update(EvaluationContext context)
    {
        var count = Count.GetValue(context).Clamp(1, 100_000);
        var center = Center.GetValue(context);
        var direction = Direction.GetValue(context);
        var length = Length.GetValue(context);
        var pivot = Pivot.GetValue(context);
        var gainAndBias = GainAndBias.GetValue(context);
        var scaleRange = Scale.GetValue(context);
        var f1Range = F1.GetValue(context);
        var f2Range = F2.GetValue(context);
        var colorA = ColorA.GetValue(context);
        var colorB = ColorB.GetValue(context);
        var orientationMode = (OrientationModes)Orientation.GetValue(context).Clamp(0, 1);
        var twist = Twist.GetValue(context);
        var orientationAxis = OrientationAxis.GetValue(context);
        var orientationAngle = OrientationAngle.GetValue(context);
        var addSeparator = AddSeparator.GetValue(context);

        var listCount = count + (addSeparator ? 1 : 0);
        if (_points.Length != listCount)
        {
            _points = new Point[listCount];
            _pointList.SetLength(listCount);
        }

        // Matches LinePoints.hlsl: fixed look-at frame with twist, or a manual axis
        var lookAt = Quaternion.Identity;
        if (orientationMode == OrientationModes.UsingUpVector)
        {
            var forward = direction.LengthSquared() > 1e-10f ? Vector3.Normalize(direction) : Vector3.UnitX;
            var upVector = MathF.Abs(Vector3.Dot(forward, Vector3.UnitZ)) > 0.999f ? Vector3.UnitY : Vector3.UnitZ;
            lookAt = LookAtQuaternion(forward, upVector);
        }

        var rollAxis = new Quaternion(0, 0, MathF.Sin(MathF.PI / 4), MathF.Cos(MathF.PI / 4)); // 90° around Z

        var steps = Math.Max(count - 1, 0);
        for (var index = 0; index < count; index++)
        {
            var f1 = ApplyGainAndBias(steps > 0 ? (float)index / steps : 0.5f, gainAndBias.X, gainAndBias.Y);
            var f = f1 - pivot;

            var angle = (orientationAngle + twist * f) * MathUtils.ToRad;
            Quaternion rotation;
            if (orientationMode == OrientationModes.UsingUpVector)
            {
                var rotate = rollAxis * Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle);
                rotation = Quaternion.Normalize(rotate * lookAt);
            }
            else
            {
                var axis = orientationAxis.LengthSquared() > 1e-10f ? Vector3.Normalize(orientationAxis) : Vector3.UnitZ;
                rotation = Quaternion.CreateFromAxisAngle(axis, angle);
            }

            _points[index] = new Point
                                 {
                                     Position = center + direction * (length * f),
                                     Orientation = rotation,
                                     Scale = Vector3.One * (scaleRange.X + scaleRange.Y * f1),
                                     F1 = f1Range.X + f1Range.Y * f1,
                                     F2 = f2Range.X + f2Range.Y * f1,
                                     Color = Vector4.Lerp(colorA, colorB, f1),
                                 };
            _pointList[index] = _points[index];
        }

        if (addSeparator)
        {
            _points[listCount - 1] = Point.Separator();
            _pointList[listCount - 1] = _points[listCount - 1];
        }

        Result.Value = _points;
        PointList.Value = _pointList;
    }

    /// <summary>Port of qLookAt() in quat-functions.hlsl (right/up/forward rotation rows).</summary>
    private static Quaternion LookAtQuaternion(Vector3 forward, Vector3 up)
    {
        var right = Vector3.Normalize(Vector3.Cross(forward, up));
        up = Vector3.Normalize(Vector3.Cross(forward, right));
        var m = new Matrix4x4(right.X, right.Y, right.Z, 0,
                              up.X, up.Y, up.Z, 0,
                              forward.X, forward.Y, forward.Z, 0,
                              0, 0, 0, 1);
        return Quaternion.CreateFromRotationMatrix(m);
    }

    private static float ApplyGainAndBias(float value, float gain, float bias)
    {
        if (value > 0.9999f)
            return 1;

        if (value < 0.00001f)
            return 0;

        gain = Math.Clamp(gain, 0.001f, 0.999f);
        bias = Math.Clamp(bias, 0.001f, 0.999f);
        if (gain < 0.5f)
        {
            value = GetBias(bias, value);
            return GetSchlickBias(gain, value);
        }

        value = GetSchlickBias(gain, value);
        return GetBias(bias, value);
    }

    private static float GetBias(float bias, float x)
    {
        return x / ((1f / bias - 2f) * (1f - x) + 1f);
    }

    private static float GetSchlickBias(float g, float x)
    {
        if (x < 0.5f)
            return 0.5f * GetBias(g, x * 2f);

        return 0.5f * GetBias(1f - g, x * 2f - 1f) + 0.5f;
    }

    private enum OrientationModes
    {
        UsingUpVector,
        Simple,
    }

    private Point[] _points = [];
    private readonly StructuredList<Point> _pointList = new(10);

    [Input(Guid = "759BFAAC-13DD-478A-A4DB-FE52B94CDAEC")]
    public readonly InputSlot<int> Count = new();

    [Input(Guid = "1d1e40c2-5f68-4b0e-a7c3-89d2e6b4f051")]
    public readonly InputSlot<Vector3> Center = new();

    [Input(Guid = "7b3f9a80-42cd-4e17-95b6-e04c8d2a6f39")]
    public readonly InputSlot<Vector3> Direction = new();

    [Input(Guid = "c92e5d14-6a08-4b73-8f2e-0d7b1c4a9e56")]
    public readonly InputSlot<float> Length = new();

    [Input(Guid = "38a6c0f5-d791-4e28-b45a-16e9d2c7b083")]
    public readonly InputSlot<float> Pivot = new();

    [Input(Guid = "e50b7d28-93a4-4c61-af07-25c8b6e1d394")]
    public readonly InputSlot<Vector2> GainAndBias = new();

    [Input(Guid = "94d1e6a3-07b8-4f52-9c3e-b72a0d5c8f16")]
    public readonly InputSlot<Vector2> Scale = new();

    [Input(Guid = "6fc2b795-e148-4d03-8a6b-d09e3c5a7f24")]
    public readonly InputSlot<Vector2> F1 = new();

    [Input(Guid = "a83d5f60-24c9-4b17-96d0-4e7b8c2f1a58")]
    public readonly InputSlot<Vector2> F2 = new();

    [Input(Guid = "50e9c3b7-861a-4f45-b2d8-79c0e4d6a132")]
    public readonly InputSlot<Vector4> ColorA = new();

    [Input(Guid = "d27a4e91-b350-4c86-8e1f-63b9d0c5a247")]
    public readonly InputSlot<Vector4> ColorB = new();

    [Input(Guid = "0b8e6d43-97f2-4a15-bc60-e58a1c3d9f72", MappedType = typeof(OrientationModes))]
    public readonly InputSlot<int> Orientation = new();

    [Input(Guid = "f14c8a59-30d6-4e92-a7b4-8c25e0d1b963")]
    public readonly InputSlot<float> Twist = new();

    [Input(Guid = "27d0b5e8-c463-4f19-90a2-5f8e6b3c1d47")]
    public readonly InputSlot<Vector3> OrientationAxis = new();

    [Input(Guid = "b96e2c07-58ad-4d31-8f5c-01d7a4e9c625")]
    public readonly InputSlot<float> OrientationAngle = new();

    [Input(Guid = "43f7a1d2-e685-4c08-b9e3-72c5d8a0f316")]
    public readonly InputSlot<bool> AddSeparator = new();
}
