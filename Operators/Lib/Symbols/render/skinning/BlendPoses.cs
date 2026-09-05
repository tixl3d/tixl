#nullable enable

namespace Lib.render.skinning;

/// <summary>
/// Blends two poses joint by joint: positions and scales are interpolated linearly,
/// orientations with slerp. An optional weight mask scales the blend factor per joint.
/// </summary>
[Guid("4a7e9c25-83d1-4b6f-95c0-e2f8a61d37b4")]
internal sealed class BlendPoses : Instance<BlendPoses>
{
    [Output(Guid = "9F1B6E83-2C47-4DA5-B380-71E5C92A04D6")]
    public readonly Slot<Point[]?> Result = new();

    public BlendPoses()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var poseA = PoseA.GetValue(context);
        var poseB = PoseB.GetValue(context);
        var blend = Blend.GetValue(context);
        var weightMask = WeightMask.GetValue(context);

        if (poseA == null || poseA.Length == 0)
        {
            Result.Value = poseB;
            return;
        }

        if (poseB == null || poseB.Length == 0)
        {
            Result.Value = poseA;
            return;
        }

        var jointCount = poseA.Length;
        if (_result.Length != jointCount)
        {
            _result = new Point[jointCount];
        }

        for (var jointIndex = 0; jointIndex < jointCount; jointIndex++)
        {
            var a = poseA[jointIndex];
            if (jointIndex >= poseB.Length)
            {
                _result[jointIndex] = a;
                continue;
            }

            var b = poseB[jointIndex];

            var t = blend;
            if (weightMask != null && jointIndex < weightMask.Length)
            {
                t *= weightMask[jointIndex].F1;
            }

            _result[jointIndex] = a;
            _result[jointIndex].Position = Vector3.Lerp(a.Position, b.Position, t);
            _result[jointIndex].Orientation = Quaternion.Slerp(a.Orientation, b.Orientation, t);
            _result[jointIndex].Scale = Vector3.Lerp(a.Scale, b.Scale, t);
        }

        Result.Value = _result;
    }

    private Point[] _result = [];

    [Input(Guid = "5c82d1f7-940b-4e6a-a3d8-16fb27c95e40")]
    public readonly InputSlot<Point[]> PoseA = new();

    [Input(Guid = "e3a95b18-6d72-4c04-bf59-8a41d0c7263f")]
    public readonly InputSlot<Point[]> PoseB = new();

    [Input(Guid = "71f4c8d9-25ae-4b63-90e7-3dc5f81b42a9")]
    public readonly InputSlot<float> Blend = new();

    [Input(Guid = "ad60be52-17c9-4f38-b654-90e2c73a8f15")]
    public readonly InputSlot<Point[]> WeightMask = new();
}
