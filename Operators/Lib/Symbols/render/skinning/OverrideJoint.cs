#nullable enable
using T3.Core.Utils;

namespace Lib.render.skinning;

/// <summary>
/// Adjusts a single joint of a pose with offsets from graph inputs - e.g. to aim a head
/// or twist a bone procedurally on top of sampled animation.
/// </summary>
[Guid("1e94b7c3-58f2-4a06-9d81-3b6ac0e527f9")]
internal sealed class OverrideJoint : Instance<OverrideJoint>
{
    [Output(Guid = "7D25A90F-C463-4E18-B57A-92F01C8D63E5")]
    public readonly Slot<Point[]?> Result = new();

    public OverrideJoint()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var pose = Pose.GetValue(context);
        var jointIndex = JointIndex.GetValue(context);
        var rotationOffset = RotationOffset.GetValue(context);
        var translationOffset = TranslationOffset.GetValue(context);
        var scaleFactor = ScaleFactor.GetValue(context);

        if (pose == null || pose.Length == 0)
        {
            Result.Value = pose;
            return;
        }

        if (_result.Length != pose.Length)
        {
            _result = new Point[pose.Length];
        }

        Array.Copy(pose, _result, pose.Length);

        if (jointIndex >= 0 && jointIndex < pose.Length)
        {
            var offsetRotation = Quaternion.CreateFromYawPitchRoll(rotationOffset.Y * MathUtils.ToRad,
                                                                   rotationOffset.X * MathUtils.ToRad,
                                                                   rotationOffset.Z * MathUtils.ToRad);

            // Offset applied in the joint's local frame
            _result[jointIndex].Orientation = Quaternion.Normalize(pose[jointIndex].Orientation * offsetRotation);
            _result[jointIndex].Position += translationOffset;
            _result[jointIndex].Scale *= scaleFactor;
        }

        Result.Value = _result;
    }

    private Point[] _result = [];

    [Input(Guid = "6f38d5a1-09e7-4c42-8b96-d472e1f0a583")]
    public readonly InputSlot<Point[]> Pose = new();

    [Input(Guid = "b1c86f24-73d9-4058-a6ce-08f5b3d19e67")]
    public readonly InputSlot<int> JointIndex = new();

    [Input(Guid = "29a7d0e5-4c81-4f36-92b8-6d1e50c4a7f3")]
    public readonly InputSlot<Vector3> RotationOffset = new();

    [Input(Guid = "8c50e3b9-16df-4a72-b048-3f9a6d28c1e5")]
    public readonly InputSlot<Vector3> TranslationOffset = new();

    [Input(Guid = "d764a1f8-3e92-4b05-8c17-25e0b9f4d6a3")]
    public readonly InputSlot<Vector3> ScaleFactor = new();
}
