#nullable enable

namespace Lib.render.skinning;

/// <summary>
/// Builds a skeleton from a CPU point list, e.g. a spline chain read back with [PointsToCPU].
/// The points define the rest pose in object space; separator points (NaN scale) split chains.
/// The resulting setup feeds [PoseToSkinMatrices], [BindToSkeleton] and [PoseFromPoints].
/// </summary>
[Guid("9c2e5f70-b813-4d4a-a8c6-1d05e94f72b6")]
internal sealed class SkeletonFromPoints : Instance<SkeletonFromPoints>, IStatusProvider
{
    [Output(Guid = "3F81A6C2-77D4-4E05-9B28-C56E01D9A4F7")]
    public readonly Slot<SceneSetup?> Setup = new();

    public SkeletonFromPoints()
    {
        Setup.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        _lastErrorMessage = null;

        var pointList = Points.GetValue(context);
        var useF2AsParent = UseF2AsParent.GetValue(context);

        if (pointList is not StructuredList<Point> { NumElements: > 0 } typedList)
        {
            _lastErrorMessage = "Connect a CPU point list, e.g. via [PointsToCPU]";
            Setup.Value = null;
            return;
        }

        var points = typedList.TypedElements;
        var pointCount = typedList.NumElements;

        // Collect joints, skipping separators. Separators break chains; F2 parent
        // references are remapped from original point indices to joint indices.
        _jointSourceIndices.Clear();
        var jointIndicesByPointIndex = new int[pointCount];
        for (var pointIndex = 0; pointIndex < pointCount; pointIndex++)
        {
            if (float.IsNaN(points[pointIndex].Scale.X))
            {
                jointIndicesByPointIndex[pointIndex] = -1;
                continue;
            }

            jointIndicesByPointIndex[pointIndex] = _jointSourceIndices.Count;
            _jointSourceIndices.Add(pointIndex);
        }

        var jointCount = _jointSourceIndices.Count;
        if (jointCount == 0)
        {
            _lastErrorMessage = "Point list contains no usable points";
            Setup.Value = null;
            return;
        }

        var skeleton = new SceneSetup.SceneSkeleton
                           {
                               JointNames = new string[jointCount],
                               ParentIndices = new int[jointCount],
                               RestLocalTransforms = new SceneSetup.Transform[jointCount],
                               InverseBindMatrices = new Matrix4x4[jointCount],
                           };

        var objectMatrices = new Matrix4x4[jointCount];

        for (var jointIndex = 0; jointIndex < jointCount; jointIndex++)
        {
            var pointIndex = _jointSourceIndices[jointIndex];
            var point = points[pointIndex];

            int parentIndex;
            if (useF2AsParent)
            {
                var parentPointIndex = (int)point.F2;
                parentIndex = parentPointIndex >= 0 && parentPointIndex < pointCount && parentPointIndex != pointIndex
                                  ? jointIndicesByPointIndex[parentPointIndex]
                                  : -1;
            }
            else
            {
                // Chain mode: parent is the previous point unless a separator broke the chain
                parentIndex = pointIndex > 0 ? jointIndicesByPointIndex[pointIndex - 1] : -1;
            }

            skeleton.JointNames[jointIndex] = $"Joint{jointIndex}";
            skeleton.ParentIndices[jointIndex] = parentIndex;

            var objectMatrix = Matrix4x4.CreateScale(point.Scale)
                               * Matrix4x4.CreateFromQuaternion(point.Orientation)
                               * Matrix4x4.CreateTranslation(point.Position);
            objectMatrices[jointIndex] = objectMatrix;

            if (!Matrix4x4.Invert(objectMatrix, out skeleton.InverseBindMatrices[jointIndex]))
            {
                skeleton.InverseBindMatrices[jointIndex] = Matrix4x4.Identity;
            }

            // Local rest transform relative to the parent
            var localMatrix = objectMatrix;
            if (parentIndex >= 0 && Matrix4x4.Invert(objectMatrices[parentIndex], out var parentInverse))
            {
                localMatrix = objectMatrix * parentInverse;
            }

            if (Matrix4x4.Decompose(localMatrix, out var scale, out var rotation, out var translation))
            {
                skeleton.RestLocalTransforms[jointIndex] = new SceneSetup.Transform
                                                               {
                                                                   Translation = translation,
                                                                   Rotation = rotation,
                                                                   Scale = scale,
                                                               };
            }
            else
            {
                skeleton.RestLocalTransforms[jointIndex] = new SceneSetup.Transform
                                                               {
                                                                   Translation = localMatrix.Translation,
                                                                   Rotation = Quaternion.Identity,
                                                                   Scale = Vector3.One,
                                                               };
            }
        }

        _setup ??= new SceneSetup();
        _setup.Skeletons.Clear();
        _setup.Skeletons.Add(skeleton);
        Setup.Value = _setup;
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

    private SceneSetup? _setup;
    private readonly List<int> _jointSourceIndices = new();
    private string? _lastErrorMessage;

    [Input(Guid = "d05b39e8-42a6-4c17-b8f9-6273c580ad14")]
    public readonly InputSlot<StructuredList> Points = new();

    [Input(Guid = "68c1d4a7-95f0-4832-8ce5-b7304a61e9d2")]
    public readonly InputSlot<bool> UseF2AsParent = new();
}
