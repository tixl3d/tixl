using System;
using T3.Core.Utils;

namespace Lib.point._cpu;

/// <summary>
/// Transforms every point of a CPU point list: scale and stretch about a pivot,
/// rotate, translate - in object space, or in each point's own space. Orientations
/// follow the rotation. Separator points pass through untouched.
/// </summary>
[Guid("a4c8e2d6-7f31-4b95-9e0a-5d3c1b8f7a26")]
internal sealed class TransformCPoints : Instance<TransformCPoints>, ITransformable
{
    [Output(Guid = "d1f7b3a9-2e64-4c08-8b5d-9a0c6e4f2d73")]
    public readonly TransformCallbackSlot<StructuredList> Result = new();

    public TransformCPoints()
    {
        Result.TransformableOp = this;
        Result.UpdateAction = Update;
    }

    IInputSlot ITransformable.TranslationInput => Translation;
    IInputSlot ITransformable.RotationInput => Rotation;
    IInputSlot ITransformable.ScaleInput => Stretch;

    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    private void Update(EvaluationContext context)
    {
        var source = Points.GetValue(context) as StructuredList<Point>;
        if (source == null || source.NumElements == 0)
        {
            Result.Value = source;
            return;
        }

        var translation = Translation.GetValue(context);
        var rotationDegrees = Rotation.GetValue(context);
        var stretch = Stretch.GetValue(context) * Scale.GetValue(context);
        var pivot = Pivot.GetValue(context);
        var space = (Spaces)Space.GetValue(context).Clamp(0, 1);
        var updateRotation = UpdateRotation.GetValue(context);

        var rotation = Quaternion.CreateFromYawPitchRoll(rotationDegrees.Y.ToRadians(), rotationDegrees.X.ToRadians(), rotationDegrees.Z.ToRadians());
        var count = source.NumElements;
        if (_output.NumElements != count)
            _output.SetLength(count);

        var input = source.TypedElements;
        var output = _output.TypedElements;
        for (var i = 0; i < count; i++)
        {
            var p = input[i];
            if (Point.IsSeparator(p))
            {
                output[i] = p;
                continue;
            }

            if (space == Spaces.ObjectSpace)
            {
                p.Position = Vector3.Transform((p.Position - pivot) * stretch, rotation) + pivot + translation;
                if (updateRotation)
                    p.Orientation = Quaternion.Normalize(rotation * p.Orientation);
            }
            else
            {
                // Point space: the offset is expressed in the point's own frame, the rotation composes locally
                var frame = p.Orientation;
                p.Position += Vector3.Transform(translation, frame);
                if (updateRotation)
                    p.Orientation = Quaternion.Normalize(frame * rotation);
            }

            output[i] = p;
        }

        Result.Value = _output;
    }

    private enum Spaces
    {
        ObjectSpace,
        PointSpace,
    }

    private readonly StructuredList<Point> _output = new(1);

    [Input(Guid = "3b9e6f1d-8c24-4a70-b5d3-e7a2c0f9d418")]
    public readonly InputSlot<StructuredList> Points = new();

    [Input(Guid = "7e2a5c9b-4d16-4f83-a9e0-1b6d8f3c2a57")]
    public readonly InputSlot<Vector3> Translation = new();

    [Input(Guid = "c5d1a8f3-6e29-4b47-8a2c-d0f7e4b9c163")]
    public readonly InputSlot<Vector3> Rotation = new();

    [Input(Guid = "9f4b2e7c-1a58-4d03-b6e9-3c8d5a1f7e02")]
    public readonly InputSlot<Vector3> Stretch = new();

    [Input(Guid = "2d8c6a1e-b3f7-4e95-a0d4-8e1b9c5f3d76")]
    public readonly InputSlot<float> Scale = new();

    [Input(Guid = "e6a3d9c2-5f18-4b60-9c7e-a4b2f1d8e305")]
    public readonly InputSlot<Vector3> Pivot = new();

    [Input(Guid = "5c7f1b4a-9e62-4d28-b8a1-6f0d3e9c2b54", MappedType = typeof(Spaces))]
    public readonly InputSlot<int> Space = new();

    [Input(Guid = "b8e4c2f6-3a91-4d75-8e0b-c1d5a7f2e938")]
    public readonly InputSlot<bool> UpdateRotation = new();
}
