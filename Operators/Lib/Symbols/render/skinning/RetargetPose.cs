#nullable enable
using T3.Core.Utils;

namespace Lib.render.skinning;

/// <summary>
/// Transfers a pose from one skeleton to another by matching joint names.
/// Rotations are transferred as deltas relative to each skeleton's rest pose, so rigs
/// with different bone orientations and proportions stay intact. Joints without a
/// name match keep their rest transform.
/// </summary>
[Guid("0f6a3d81-c925-4e57-b3a4-78d1e6f04c92")]
internal sealed class RetargetPose : Instance<RetargetPose>, IStatusProvider
{
    [Output(Guid = "84D92C07-5F6B-4A31-9E58-C1B3F7A2D045")]
    public readonly Slot<Point[]?> Result = new();

    public RetargetPose()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        _lastErrorMessage = null;

        var pose = Pose.GetValue(context);
        var sourceSetup = SourceSetup.GetValue(context);
        var targetSetup = TargetSetup.GetValue(context);
        var translationScale = TranslationScale.GetValue(context);

        if (sourceSetup == null || sourceSetup.Skeletons.Count == 0
            || targetSetup == null || targetSetup.Skeletons.Count == 0)
        {
            _lastErrorMessage = "Retargeting needs both a source and a target setup with skeletons";
            Result.Value = null;
            return;
        }

        var sourceSkeleton = sourceSetup.Skeletons[SourceSkeletonIndex.GetValue(context).Mod(sourceSetup.Skeletons.Count)];
        var targetSkeleton = targetSetup.Skeletons[TargetSkeletonIndex.GetValue(context).Mod(targetSetup.Skeletons.Count)];

        if (!ReferenceEquals(sourceSkeleton, _mappedSourceSkeleton) || !ReferenceEquals(targetSkeleton, _mappedTargetSkeleton))
        {
            BuildJointMapping(sourceSkeleton, targetSkeleton);
        }

        var targetJointCount = targetSkeleton.ParentIndices.Length;
        if (_result.Length != targetJointCount)
        {
            _result = new Point[targetJointCount];
        }

        for (var targetJointIndex = 0; targetJointIndex < targetJointCount; targetJointIndex++)
        {
            var targetRest = targetSkeleton.RestLocalTransforms[targetJointIndex];
            _result[targetJointIndex] = new Point
                                            {
                                                Position = targetRest.Translation,
                                                Orientation = targetRest.Rotation,
                                                Scale = targetRest.Scale,
                                                Color = Vector4.One,
                                                F1 = 1,
                                                F2 = targetSkeleton.ParentIndices[targetJointIndex],
                                            };

            var sourceJointIndex = _jointMapping[targetJointIndex];
            if (sourceJointIndex < 0 || pose == null || sourceJointIndex >= pose.Length)
                continue;

            var sourceRest = sourceSkeleton.RestLocalTransforms[sourceJointIndex];

            // Local-frame rotation delta from the source rest pose, reapplied on the target rest pose
            var deltaRotation = Quaternion.Inverse(sourceRest.Rotation) * pose[sourceJointIndex].Orientation;
            _result[targetJointIndex].Orientation = Quaternion.Normalize(targetRest.Rotation * deltaRotation);

            if (translationScale != 0)
            {
                _result[targetJointIndex].Position = targetRest.Translation
                                                     + (pose[sourceJointIndex].Position - sourceRest.Translation) * translationScale;
            }
        }

        Result.Value = _result;
    }

    private void BuildJointMapping(SceneSetup.SceneSkeleton sourceSkeleton, SceneSetup.SceneSkeleton targetSkeleton)
    {
        var sourceIndicesByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var sourceJointIndex = 0; sourceJointIndex < sourceSkeleton.JointNames.Length; sourceJointIndex++)
        {
            var name = sourceSkeleton.JointNames[sourceJointIndex];
            if (!string.IsNullOrEmpty(name))
            {
                sourceIndicesByName.TryAdd(name, sourceJointIndex);
            }
        }

        var targetJointCount = targetSkeleton.JointNames.Length;
        if (_jointMapping.Length != targetJointCount)
        {
            _jointMapping = new int[targetJointCount];
        }

        var matchCount = 0;
        for (var targetJointIndex = 0; targetJointIndex < targetJointCount; targetJointIndex++)
        {
            var name = targetSkeleton.JointNames[targetJointIndex];
            if (!string.IsNullOrEmpty(name) && sourceIndicesByName.TryGetValue(name, out var sourceJointIndex))
            {
                _jointMapping[targetJointIndex] = sourceJointIndex;
                matchCount++;
            }
            else
            {
                _jointMapping[targetJointIndex] = -1;
            }
        }

        if (matchCount == 0)
        {
            _lastErrorMessage = "No joint names match between the two skeletons";
        }

        _mappedSourceSkeleton = sourceSkeleton;
        _mappedTargetSkeleton = targetSkeleton;
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
    private int[] _jointMapping = [];
    private SceneSetup.SceneSkeleton? _mappedSourceSkeleton;
    private SceneSetup.SceneSkeleton? _mappedTargetSkeleton;
    private string? _lastErrorMessage;

    [Input(Guid = "47b8e296-0d53-4c1a-af67-92e4c8b5d130")]
    public readonly InputSlot<Point[]> Pose = new();

    [Input(Guid = "a2f60d94-8c37-4b52-91de-56b0f3e8a4c7")]
    public readonly InputSlot<SceneSetup> SourceSetup = new();

    [Input(Guid = "5e19c483-b7a0-4d26-8f34-d9c162e75b08")]
    public readonly InputSlot<int> SourceSkeletonIndex = new();

    [Input(Guid = "c9d47f12-3a85-4e60-b29c-08e6a1d5f374")]
    public readonly InputSlot<SceneSetup> TargetSetup = new();

    [Input(Guid = "3b04a6d8-92ef-4c71-85b3-f1d09c24e685")]
    public readonly InputSlot<int> TargetSkeletonIndex = new();

    [Input(Guid = "e67f2b90-14c8-4da3-9605-7a3ce8d1b542")]
    public readonly InputSlot<float> TranslationScale = new();
}
