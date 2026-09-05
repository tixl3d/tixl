#nullable enable
using T3.Core.Utils;

namespace Lib.render.skinning;

/// <summary>
/// Layers an additive pose on top of a base pose. The additive contribution is the
/// difference between the additive pose and the skeleton's rest pose, scaled by Weight -
/// so an additive clip authored relative to the rest pose leaves unposed joints untouched.
/// </summary>
[Guid("6b3f8d92-a541-4c78-b0e6-29d7c14f85a3")]
internal sealed class AdditivePose : Instance<AdditivePose>, IStatusProvider
{
    [Output(Guid = "C48A17E6-95D3-4F20-8B6C-E07D3A92F514")]
    public readonly Slot<Point[]?> Result = new();

    public AdditivePose()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        _lastErrorMessage = null;

        var basePose = BasePose.GetValue(context);
        var additivePose = Additive.GetValue(context);
        var setup = Setup.GetValue(context);
        var weight = Weight.GetValue(context);
        var skeletonIndexInput = SkeletonIndex.GetValue(context);

        if (basePose == null || basePose.Length == 0)
        {
            Result.Value = additivePose;
            return;
        }

        if (additivePose == null || additivePose.Length == 0 || weight == 0)
        {
            Result.Value = basePose;
            return;
        }

        if (setup == null || setup.Skeletons.Count == 0)
        {
            _lastErrorMessage = "Additive blending needs the skeleton's rest pose as reference. Connect the scene setup.";
            Result.Value = basePose;
            return;
        }

        var skeleton = setup.Skeletons[skeletonIndexInput.Mod(setup.Skeletons.Count)];
        var jointCount = basePose.Length;
        if (_result.Length != jointCount)
        {
            _result = new Point[jointCount];
        }

        for (var jointIndex = 0; jointIndex < jointCount; jointIndex++)
        {
            var baseJoint = basePose[jointIndex];
            _result[jointIndex] = baseJoint;

            if (jointIndex >= additivePose.Length || jointIndex >= skeleton.RestLocalTransforms.Length)
                continue;

            var additiveJoint = additivePose[jointIndex];
            var rest = skeleton.RestLocalTransforms[jointIndex];

            // Rotation delta relative to the rest pose, applied in the joint's local frame
            var deltaRotation = Quaternion.Inverse(rest.Rotation) * additiveJoint.Orientation;
            deltaRotation = Quaternion.Slerp(Quaternion.Identity, deltaRotation, weight);
            _result[jointIndex].Orientation = Quaternion.Normalize(baseJoint.Orientation * deltaRotation);

            _result[jointIndex].Position = baseJoint.Position + (additiveJoint.Position - rest.Translation) * weight;
            _result[jointIndex].Scale = baseJoint.Scale + (additiveJoint.Scale - rest.Scale) * weight;
        }

        Result.Value = _result;
    }

    #region status provider
    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel()
    {
        return _lastErrorMessage == null ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    string IStatusProvider.GetStatusMessage()
    {
        return _lastErrorMessage ?? string.Empty;
    }
    #endregion

    private Point[] _result = [];
    private string? _lastErrorMessage;

    [Input(Guid = "38e6c0a4-d19b-4572-9fe8-b25a41c6d793")]
    public readonly InputSlot<Point[]> BasePose = new();

    [Input(Guid = "f90d54b7-6a28-4e13-85cf-1c74b09e62d8")]
    public readonly InputSlot<Point[]> Additive = new();

    [Input(Guid = "04c7f2e9-8b35-4d61-a9f0-57e3d18c46b2")]
    public readonly InputSlot<SceneSetup> Setup = new();

    [Input(Guid = "92b5e731-4f0c-48a6-bd27-c690f5a3e814")]
    public readonly InputSlot<int> SkeletonIndex = new();

    [Input(Guid = "5df03a86-27c4-4b91-8e35-fa1608d92c47")]
    public readonly InputSlot<float> Weight = new();
}
