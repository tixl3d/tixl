#nullable enable
using T3.Core.Utils;

namespace Lib.render.skinning;

/// <summary>
/// Converts a skeleton pose into a buffer of skinning matrices consumed by [SkinMesh].
/// Without a pose input the skeleton's rest pose is used, which reproduces the bind pose.
/// </summary>
[Guid("7b8ec5a6-2d94-4c1f-b0e3-98a4d17f6c25")]
internal sealed class PoseToSkinMatrices : Instance<PoseToSkinMatrices>, IStatusProvider
{
    [Output(Guid = "5E1A9C34-B872-4D6F-A05E-3C7D28F491B6")]
    public readonly Slot<BufferWithViews?> SkinMatrices = new();

    public PoseToSkinMatrices()
    {
        SkinMatrices.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        _lastErrorMessage = null;

        var setup = Setup.GetValue(context);
        var pose = Pose.GetValue(context);
        var skeletonIndexInput = SkeletonIndex.GetValue(context);

        if (setup == null || setup.Skeletons.Count == 0)
        {
            _lastErrorMessage = "Scene has no skeletons. Connect the setup of a rigged glTF model.";
            SkinMatrices.Value = null;
            return;
        }

        var skeleton = setup.Skeletons[skeletonIndexInput.Mod(setup.Skeletons.Count)];
        var jointCount = skeleton.ParentIndices.Length;
        if (jointCount == 0)
        {
            _lastErrorMessage = "Skeleton has no joints";
            SkinMatrices.Value = null;
            return;
        }

        if (_objectPoseMatrices.Length != jointCount)
        {
            _objectPoseMatrices = new Matrix4x4[jointCount];
            _skinMatrices = new Matrix4x4[jointCount];
        }

        if (!ReferenceEquals(skeleton, _skeletonForEvalOrder))
        {
            BuildEvaluationOrder(skeleton);
        }

        var hasPose = pose is { Length: > 0 };
        if (hasPose && pose!.Length < jointCount)
        {
            _lastErrorMessage = $"Pose has {pose.Length} points but skeleton has {jointCount} joints. Using rest pose.";
            hasPose = false;
        }

        for (var orderIndex = 0; orderIndex < jointCount; orderIndex++)
        {
            var jointIndex = _evalOrder[orderIndex];

            Matrix4x4 localTransform;
            if (hasPose)
            {
                var posePoint = pose![jointIndex];
                localTransform = Matrix4x4.CreateScale(posePoint.Scale)
                                 * Matrix4x4.CreateFromQuaternion(posePoint.Orientation)
                                 * Matrix4x4.CreateTranslation(posePoint.Position);
            }
            else
            {
                localTransform = skeleton.RestLocalTransforms[jointIndex].ToTransform();
            }

            var parentIndex = skeleton.ParentIndices[jointIndex];
            _objectPoseMatrices[jointIndex] = parentIndex >= 0
                                                  ? localTransform * _objectPoseMatrices[parentIndex]
                                                  : localTransform;

            // Row-vector convention: a vertex first moves into joint space, then with the posed joint
            _skinMatrices[jointIndex] = skeleton.InverseBindMatrices[jointIndex] * _objectPoseMatrices[jointIndex];
        }

        ResourceManager.SetupStructuredBuffer(_skinMatrices, MatrixStride * jointCount, MatrixStride, ref _buffer.Buffer);
        ResourceManager.CreateStructuredBufferSrv(_buffer.Buffer, ref _buffer.Srv);
        SkinMatrices.Value = _buffer;
    }

    /// <summary>
    /// glTF doesn't guarantee parents before children in the joint list, so resolve an order once per skeleton.
    /// </summary>
    private void BuildEvaluationOrder(SceneSetup.SceneSkeleton skeleton)
    {
        var jointCount = skeleton.ParentIndices.Length;
        if (_evalOrder.Length != jointCount)
        {
            _evalOrder = new int[jointCount];
        }

        var placed = new bool[jointCount];
        var placedCount = 0;

        while (placedCount < jointCount)
        {
            var progressed = false;
            for (var jointIndex = 0; jointIndex < jointCount; jointIndex++)
            {
                if (placed[jointIndex])
                    continue;

                var parentIndex = skeleton.ParentIndices[jointIndex];
                if (parentIndex >= 0 && parentIndex < jointCount && !placed[parentIndex])
                    continue;

                _evalOrder[placedCount++] = jointIndex;
                placed[jointIndex] = true;
                progressed = true;
            }

            if (progressed)
                continue;

            Log.Warning("Skeleton joint hierarchy contains a cycle - using unordered evaluation", this);
            for (var jointIndex = 0; jointIndex < jointCount; jointIndex++)
            {
                _evalOrder[jointIndex] = jointIndex;
            }

            break;
        }

        _skeletonForEvalOrder = skeleton;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        _buffer.Dispose();
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

    private const int MatrixStride = 16 * 4;
    private readonly BufferWithViews _buffer = new();
    private Matrix4x4[] _objectPoseMatrices = [];
    private Matrix4x4[] _skinMatrices = [];
    private int[] _evalOrder = [];
    private SceneSetup.SceneSkeleton? _skeletonForEvalOrder;
    private string? _lastErrorMessage;

    [Input(Guid = "9d43f8a1-6e05-472b-8cd9-1b52a7e64c88")]
    public readonly InputSlot<SceneSetup> Setup = new();

    [Input(Guid = "4f2b6d90-83c7-49ae-b512-e96a05d3f174")]
    public readonly InputSlot<Point[]> Pose = new();

    [Input(Guid = "a17c3e58-04bd-4e92-9f66-7d80b25c41a9")]
    public readonly InputSlot<int> SkeletonIndex = new();
}
