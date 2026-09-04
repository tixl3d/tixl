#nullable enable
using SharpDX.Direct3D11;
using T3.Core.Utils;

namespace Lib.render.skinning;

/// <summary>
/// Generates skin weights for a mesh (or point buffer) that has none, using
/// distance-to-bone envelopes on the skeleton's rest pose - so any geometry can be
/// rigged to a loaded or procedural skeleton and deformed with [SkinMesh] / [SkinPoints].
/// </summary>
[Guid("82d64a0f-19cb-4e35-9a70-45f8c2e6b1d3")]
internal sealed class BindToSkeleton : Instance<BindToSkeleton>, IStatusProvider
{
    [Output(Guid = "E04C728A-63B5-4F19-8DC2-97A1E5F0834B")]
    public readonly Slot<BufferWithViews?> SkinWeights = new();

    public BindToSkeleton()
    {
        SkinWeights.UpdateAction += Update;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct BoneSegment
    {
        public Vector4 Start;
        public Vector4 End;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct BindParams
    {
        public float Radius;
        public float FalloffPower;
        public float MaxInfluences;
        public float Padding;
    }

    private void Update(EvaluationContext context)
    {
        _lastErrorMessage = null;

        var mesh = Mesh.GetValue(context);
        var points = Points.GetValue(context);
        var setup = Setup.GetValue(context);

        if (setup == null || setup.Skeletons.Count == 0)
        {
            _lastErrorMessage = "Connect a setup with a skeleton";
            SkinWeights.Value = null;
            return;
        }

        var useMesh = mesh?.VertexBuffer?.Srv is { IsDisposed: false };
        var sourceSrv = useMesh
                            ? mesh!.VertexBuffer.Srv
                            : points is { Srv.IsDisposed: false } ? points.Srv : null;
        if (sourceSrv == null)
        {
            _lastErrorMessage = "Connect a mesh or a point buffer to bind";
            SkinWeights.Value = null;
            return;
        }

        var skeleton = setup.Skeletons[SkeletonIndex.GetValue(context).Mod(setup.Skeletons.Count)];
        if (!ReferenceEquals(skeleton, _segmentsForSkeleton))
        {
            BuildBoneSegments(skeleton);
        }

        var shaderResource = useMesh
                                 ? _meshShaderResource ??= ResourceManager.CreateShaderResource<T3.Core.DataTypes.ComputeShader>(
                                       "Lib:shaders/cs/BindMeshToSkeleton-cs.hlsl", this, () => "main", null)
                                 : _pointsShaderResource ??= ResourceManager.CreateShaderResource<T3.Core.DataTypes.ComputeShader>(
                                       "Lib:shaders/cs/BindPointsToSkeleton-cs.hlsl", this, () => "main", null);

        var shader = shaderResource.Value;
        if (shader == null || _segmentBuffer.Srv == null)
        {
            _lastErrorMessage = "Binding shader is not available";
            SkinWeights.Value = null;
            return;
        }

        var bindParams = new BindParams
                             {
                                 Radius = Radius.GetValue(context).ClampMin(0.0001f),
                                 FalloffPower = FalloffPower.GetValue(context).ClampMin(0.01f),
                                 MaxInfluences = MaxInfluences.GetValue(context).Clamp(1, 4),
                             };
        ResourceManager.SetupConstBuffer(bindParams, ref _paramBuffer);

        var elementCount = sourceSrv.Description.Buffer.ElementCount;
        ResourceManager.SetupStructuredBuffer(elementCount * SkinWeightStride, SkinWeightStride, ref _weightsBuffer.Buffer);
        ResourceManager.CreateStructuredBufferSrv(_weightsBuffer.Buffer, ref _weightsBuffer.Srv);
        ResourceManager.CreateStructuredBufferUav(_weightsBuffer.Buffer, UnorderedAccessViewBufferFlags.None, ref _weightsBuffer.Uav);

        var deviceContext = ResourceManager.Device.ImmediateContext;
        var csStage = deviceContext.ComputeShader;

        csStage.Set(shader);
        csStage.SetConstantBuffer(0, _paramBuffer);
        csStage.SetShaderResource(0, sourceSrv);
        csStage.SetShaderResource(1, _segmentBuffer.Srv);
        csStage.SetUnorderedAccessView(0, _weightsBuffer.Uav);

        const int threadGroupSize = 64;
        deviceContext.Dispatch(elementCount / threadGroupSize + 1, 1, 1);

        csStage.SetUnorderedAccessView(0, null);
        csStage.SetShaderResource(0, null);
        csStage.SetShaderResource(1, null);
        csStage.SetConstantBuffer(0, null);
        csStage.Set(null);

        SkinWeights.Value = _weightsBuffer;
    }

    /// <summary>
    /// One rest-pose segment per joint: from the joint to the average of its children (or itself for leaves).
    /// </summary>
    private void BuildBoneSegments(SceneSetup.SceneSkeleton skeleton)
    {
        var jointCount = skeleton.ParentIndices.Length;
        var objectMatrices = new Matrix4x4[jointCount];
        var resolved = new bool[jointCount];
        var resolvedCount = 0;

        // Joint order isn't guaranteed parent-first, so resolve in passes
        while (resolvedCount < jointCount)
        {
            var progressed = false;
            for (var jointIndex = 0; jointIndex < jointCount; jointIndex++)
            {
                if (resolved[jointIndex])
                    continue;

                var parentIndex = skeleton.ParentIndices[jointIndex];
                if (parentIndex >= 0 && parentIndex < jointCount && !resolved[parentIndex])
                    continue;

                var local = skeleton.RestLocalTransforms[jointIndex].ToTransform();
                objectMatrices[jointIndex] = parentIndex >= 0 ? local * objectMatrices[parentIndex] : local;
                resolved[jointIndex] = true;
                resolvedCount++;
                progressed = true;
            }

            if (progressed)
                continue;

            Log.Warning("Skeleton joint hierarchy contains a cycle", this);
            break;
        }

        var segments = new BoneSegment[jointCount];
        var childSums = new Vector3[jointCount];
        var childCounts = new int[jointCount];

        for (var jointIndex = 0; jointIndex < jointCount; jointIndex++)
        {
            var parentIndex = skeleton.ParentIndices[jointIndex];
            if (parentIndex < 0 || parentIndex >= jointCount)
                continue;

            childSums[parentIndex] += objectMatrices[jointIndex].Translation;
            childCounts[parentIndex]++;
        }

        for (var jointIndex = 0; jointIndex < jointCount; jointIndex++)
        {
            var start = objectMatrices[jointIndex].Translation;
            var end = childCounts[jointIndex] > 0
                          ? childSums[jointIndex] / childCounts[jointIndex]
                          : start;

            segments[jointIndex] = new BoneSegment
                                       {
                                           Start = new Vector4(start, 1),
                                           End = new Vector4(end, 1),
                                       };
        }

        ResourceManager.SetupStructuredBuffer(segments, BoneSegmentStride * jointCount, BoneSegmentStride, ref _segmentBuffer.Buffer);
        ResourceManager.CreateStructuredBufferSrv(_segmentBuffer.Buffer, ref _segmentBuffer.Srv);
        _segmentsForSkeleton = skeleton;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        _weightsBuffer.Dispose();
        _segmentBuffer.Dispose();
        _paramBuffer?.Dispose();
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

    private const int SkinWeightStride = 32;
    private const int BoneSegmentStride = 32;

    private Resource<T3.Core.DataTypes.ComputeShader>? _meshShaderResource;
    private Resource<T3.Core.DataTypes.ComputeShader>? _pointsShaderResource;
    private readonly BufferWithViews _weightsBuffer = new();
    private readonly BufferWithViews _segmentBuffer = new();
    private Buffer? _paramBuffer;
    private SceneSetup.SceneSkeleton? _segmentsForSkeleton;
    private string? _lastErrorMessage;

    [Input(Guid = "51e0c9d4-a786-4b23-9ef1-30d5b8a2c647")]
    public readonly InputSlot<MeshBuffers> Mesh = new();

    [Input(Guid = "96a3f5e1-08c2-4d7b-b365-1ce49d07f8a2")]
    public readonly InputSlot<BufferWithViews> Points = new();

    [Input(Guid = "2c85b0f7-64ad-4193-8e50-d9b3a1c67e04")]
    public readonly InputSlot<SceneSetup> Setup = new();

    [Input(Guid = "ef67a29c-b3d0-4851-92f4-06c8e5d13a7b")]
    public readonly InputSlot<int> SkeletonIndex = new();

    [Input(Guid = "78d21b5e-4c09-4f6a-8b93-a5e0f62c48d1")]
    public readonly InputSlot<float> Radius = new();

    [Input(Guid = "0b94e6c8-d235-4a70-bf16-83c7d90a25e4")]
    public readonly InputSlot<float> FalloffPower = new();

    [Input(Guid = "a6f083d2-5be7-4c94-8027-1f4d6b9e05c8")]
    public readonly InputSlot<int> MaxInfluences = new();
}
