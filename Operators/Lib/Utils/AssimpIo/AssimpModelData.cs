#nullable enable
using T3.Core.Rendering;

namespace Lib.Utils.AssimpIo;

internal enum AssimpPostProcessPresets
{
    None,
    Basic,
    Full,
}

internal enum AssimpAxisConversions
{
    None,
    YUpToZUp,
    ZUpToYUp,
    FlipX,
    FlipY,
    FlipZ,
}

/// <summary>Everything that changes the result of an import. Equality decides whether a reload is needed.</summary>
internal readonly record struct AssimpImportSettings(AssimpPostProcessPresets Preset,
                                                     float Scale,
                                                     bool Normalize,
                                                     AssimpAxisConversions AxisConversion,
                                                     bool FlipNormals,
                                                     int SelectedMeshIndex,
                                                     bool ImportMaterials);

/// <summary>
/// Per-vertex joint influences in the layout of the skinning compute shader used by [SkinMesh]
/// (identical to the one [LoadGltfScene] produces).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Stride)]
internal struct AssimpSkinWeight
{
    [FieldOffset(0)]
    public Int4 JointIndices;

    [FieldOffset(16)]
    public Vector4 Weights;

    public const int Stride = 32;
}

/// <summary>
/// CPU-side result of an Assimp import. Built on a worker thread; only the owning operator
/// turns it into GPU resources, on the render thread. Never mutated after publication, except
/// that the owner drops the CPU copies once they are on the GPU.
/// </summary>
internal sealed class AssimpModelData
{
    public PbrVertex[] Vertices = [];
    public Int3[] Triangles = [];

    /// <summary>Element counts survive <see cref="DropUploadedBuffers"/>, for outputs and metadata.</summary>
    public int VertexCount;
    public int FaceCount;

    /// <summary>One chunk per imported mesh instance, so chunk draws can address single objects of the file.</summary>
    public MeshChunkDef[] Chunks = [];

    /// <summary>Per chunk: index into <see cref="Materials"/>, or -1 for meshes without a usable material.</summary>
    public int[] ChunkMaterialIndices = [];

    /// <summary>
    /// Only the materials used by the imported meshes; empty when material import is off or when
    /// <see cref="ReusesPreviousMaterials"/> is set.
    /// </summary>
    public readonly List<AssimpMaterialData> Materials = [];

    /// <summary>The materials are the ones of the previous load of the same parsed file and mesh selection.</summary>
    public bool ReusesPreviousMaterials;

    public Point[] Points = [];

    /// <summary>Polygonal geometry (N-gons preserved, positions welded) for the procedural geometry operators.</summary>
    public MeshGeometry? Geometry;

    /// <summary>Parallel to <see cref="Vertices"/>; null for files without bones.</summary>
    public AssimpSkinWeight[]? SkinWeights;

    public SceneSetup.SceneSkeleton? Skeleton;
    public Point[] SkeletonRestPoints = [];
    public readonly List<SceneSetup.SceneAnimClip> AnimationClips = [];

    public Vector3 BoundsMin;
    public Vector3 BoundsMax;

    public int FileMeshCount;
    public int UsedMeshInstanceCount;
    public int BoneCount;
    public string MeshNames = string.Empty;
    public string AnimationNames = string.Empty;

    public readonly List<string> Warnings = [];

    /// <summary>
    /// Drops the CPU copies that the GPU buffers now own. A 50M-vertex model keeps several hundred MB
    /// per array alive otherwise, on top of the GPU buffers and the polygon geometry.
    /// </summary>
    public void DropUploadedBuffers()
    {
        Vertices = [];
        Triangles = [];
        Points = [];
        SkinWeights = null;
        SkeletonRestPoints = [];
    }
}
