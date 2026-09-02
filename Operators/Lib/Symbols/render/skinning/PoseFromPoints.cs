#nullable enable
using T3.Core.Utils;

namespace Lib.render.skinning;

/// <summary>
/// Converts object-space points (e.g. an animated spline read back with [PointsToCPU])
/// into a joint-local pose for the given skeleton - the animation source for rigs built
/// with [SkeletonFromPoints]. Points must come in the same order the skeleton was built from.
/// </summary>
[Guid("ba64d1e8-30c7-4f92-8517-f6a29c08d5b3")]
internal sealed class PoseFromPoints : Instance<PoseFromPoints>, IStatusProvider
{
    [Output(Guid = "15C7E9A3-D842-4B60-97F5-28A0D61C34BE")]
    public readonly Slot<Point[]?> Pose = new();

    public PoseFromPoints()
    {
        Pose.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        _lastErrorMessage = null;

        var pointList = Points.GetValue(context);
        var setup = Setup.GetValue(context);
        var skeletonIndexInput = SkeletonIndex.GetValue(context);

        if (setup == null || setup.Skeletons.Count == 0)
        {
            _lastErrorMessage = "Connect a setup with a skeleton";
            Pose.Value = null;
            return;
        }

        var skeleton = setup.Skeletons[skeletonIndexInput.Mod(setup.Skeletons.Count)];
        var jointCount = skeleton.ParentIndices.Length;

        if (_pose.Length != jointCount)
        {
            _pose = new Point[jointCount];
            _objectMatrices = new Matrix4x4[jointCount];
        }

        // Collect object transforms, skipping separators the same way [SkeletonFromPoints] does
        var collectedCount = 0;
        if (pointList is StructuredList<Point> { NumElements: > 0 } typedList)
        {
            var points = typedList.TypedElements;
            for (var pointIndex = 0; pointIndex < typedList.NumElements && collectedCount < jointCount; pointIndex++)
            {
                var point = points[pointIndex];
                if (float.IsNaN(point.Scale.X))
                    continue;

                _objectMatrices[collectedCount] = Matrix4x4.CreateScale(point.Scale)
                                                  * Matrix4x4.CreateFromQuaternion(point.Orientation)
                                                  * Matrix4x4.CreateTranslation(point.Position);
                collectedCount++;
            }
        }

        if (collectedCount < jointCount)
        {
            _lastErrorMessage = $"Point list provides {collectedCount} of {jointCount} joints - missing joints use the rest pose";
        }

        for (var jointIndex = 0; jointIndex < jointCount; jointIndex++)
        {
            var rest = skeleton.RestLocalTransforms[jointIndex];
            _pose[jointIndex] = new Point
                                    {
                                        Position = rest.Translation,
                                        Orientation = rest.Rotation,
                                        Scale = rest.Scale,
                                        Color = Vector4.One,
                                        F1 = 1,
                                        F2 = skeleton.ParentIndices[jointIndex],
                                    };

            if (jointIndex >= collectedCount)
                continue;

            var localMatrix = _objectMatrices[jointIndex];
            var parentIndex = skeleton.ParentIndices[jointIndex];
            if (parentIndex >= 0 && parentIndex < collectedCount
                && Matrix4x4.Invert(_objectMatrices[parentIndex], out var parentInverse))
            {
                localMatrix = _objectMatrices[jointIndex] * parentInverse;
            }

            if (Matrix4x4.Decompose(localMatrix, out var scale, out var rotation, out var translation))
            {
                _pose[jointIndex].Position = translation;
                _pose[jointIndex].Orientation = rotation;
                _pose[jointIndex].Scale = scale;
            }
        }

        Pose.Value = _pose;
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

    private Point[] _pose = [];
    private Matrix4x4[] _objectMatrices = [];
    private string? _lastErrorMessage;

    [Input(Guid = "7e2a90c5-b164-4d38-8f0b-59c8e327a6d1")]
    public readonly InputSlot<StructuredList> Points = new();

    [Input(Guid = "c31f68b0-52d9-4ae7-9264-08b5f7d0c9e3")]
    public readonly InputSlot<SceneSetup> Setup = new();

    [Input(Guid = "4d90b2f6-e7a3-4c58-b1d0-63f2a85c07e9")]
    public readonly InputSlot<int> SkeletonIndex = new();
}
