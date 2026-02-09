using Assimp;
using T3.Core.Rendering;
using Scene = Assimp.Scene;

namespace Lib.io.assimp;

[Guid("E0B5B99D-C44F-434D-920D-422E88C75870")]
internal sealed class AssimpImport : Instance<AssimpImport>
{
    private readonly BufferWithViews _indexBufferWithViews = new();
    private readonly MeshBuffers _meshData = new();
    private readonly BufferWithViews _pointBufferWithViews = new();

    private readonly Resource<Scene> _sceneResource;
    private readonly BufferWithViews _vertexBufferWithViews = new();

    [Input(Guid = "9C5D0E4F-7A1B-5C9D-2E3F-3B4C5D6E7F8A", MappedType = typeof(AxisConversion))]
    public readonly InputSlot<int> AxisConversionMode = new((int)AxisConversion.None);

    [Output(Guid = "F6A7B8C9-1D2E-4F3B-4C5D-5E6F7A8B9C1D")]
    public readonly Slot<Vector3> BoundsMax = new();

    [Output(Guid = "E5F6A7B8-9C1D-4E2F-3B4C-4D5E6F7A8B9C")]
    public readonly Slot<Vector3> BoundsMin = new();

    [Output(Guid = "C3D4E5F6-7A8B-4C9D-1E2F-2B3C4D5E6F7A")]
    public readonly Slot<int> FaceCount = new();

    [Input(Guid = "B569D0CC-9F84-419C-A3DB-91406DF6A3F8")]
    public readonly InputSlot<string> FilePath = new();

    [Input(Guid = "1A2B3C4D-5E6F-7A8B-9C0D-1E2F3A4B5C6D")]
    public readonly InputSlot<bool> FlipNormals = new(false);

    [Output(Guid = "D4E5F6A7-8B9C-4D1E-2F3B-3C4D5E6F7A8B")]
    public readonly Slot<int> MeshCount = new();

    [Output(Guid = "77A21584-B35B-46B6-8056-1FEB4196C0B7")]
    public readonly Slot<MeshBuffers> MeshData = new();

    [Output(Guid = "A7B8C9D1-2E3F-4B4C-5D6E-6F7A8B9C1D2E")]
    public readonly Slot<string> MeshNames = new();

    [Output(Guid = "A1B2C3D4-5E6F-4A7B-8C9D-0E1F2A3B4C5D")]
    public readonly Slot<string> Metadata = new();

    [Input(Guid = "8B4C9D3E-6F0A-4B8C-9D1E-1F2B3C4D5E6F")]
    public readonly InputSlot<bool> Normalize = new(true);

    [Output(Guid = "9342C958-3D1F-40B0-B5E0-696812704812")]
    public readonly Slot<BufferWithViews> Points = new();

    [Input(Guid = "6AFF02C4-35C8-4875-BB25-D2B10ADFC276", MappedType = typeof(PostProcessPreset))]
    public readonly InputSlot<int> PostProcessFlags = new((int)PostProcessPreset.Basic);

    [Input(Guid = "7A3B8C2D-5E9F-4A7B-8C9D-0E1F2A3B4C5D")]
    public readonly InputSlot<float> Scale = new(1.0f);

    [Input(Guid = "2B3C4D5E-6F7A-8B9C-1D2E-3F4A5B6C7D8E")]
    public readonly InputSlot<int> SelectedMeshIndex = new(-1);

    [Output(Guid = "B2C3D4E5-6F7A-4B8C-9D1E-1F2B3C4D5E6F")]
    public readonly Slot<int> VertexCount = new();

    private int _cachedPostProcessFlags;
    private Buffer _indexBuffer;
    private Int3[] _indexBufferData = new Int3[0];
    private Buffer _pointBuffer;
    private Point[] _pointBufferData = new Point[0];
    private Buffer _vertexBuffer;
    private PbrVertex[] _vertexBufferData = new PbrVertex[0];

    public AssimpImport()
    {
        MeshData.UpdateAction += Update;
        _sceneResource = new Resource<Scene>(FilePath, TryLoadScene, false);
        _sceneResource.AddDependentSlots(MeshData, Points);
    }

    private void Update(EvaluationContext context)
    {
        var postProcessFlags = PostProcessFlags.GetValue(context);
        var scale = Scale.GetValue(context);
        var normalize = Normalize.GetValue(context);
        var flipNormals = FlipNormals.GetValue(context);
        var axisConversion = (AxisConversion)AxisConversionMode.GetValue(context);
        var selectedMeshIndex = SelectedMeshIndex.GetValue(context);

        // Check if post process flags changed and invalidate resource cache if needed
        if (_cachedPostProcessFlags != postProcessFlags)
        {
            _cachedPostProcessFlags = postProcessFlags;
            _sceneResource.MarkFileAsChanged();
        }

        if (!_sceneResource.TryGetValue(context, out var scene))
        {
            Log.Debug("No scene loaded", this);
            MeshData.Value = null;
            Points.Value = null;
            return;
        }

        try
        {
            // Determine which meshes to load
            var startMesh = 0;
            var endMesh = scene.MeshCount;

            if (selectedMeshIndex >= 0 && selectedMeshIndex < scene.MeshCount)
            {
                startMesh = selectedMeshIndex;
                endMesh = selectedMeshIndex + 1;
            }

            // Count total vertices and faces
            var totalVertices = 0;
            var totalFaces = 0;
            var meshNamesList = new StringBuilder();

            for (var i = 0; i < scene.MeshCount; i++)
            {
                var mesh = scene.Meshes[i];
                if (i >= startMesh && i < endMesh)
                {
                    totalVertices += mesh.VertexCount;
                    totalFaces += mesh.FaceCount;
                }

                if (meshNamesList.Length > 0)
                    meshNamesList.Append('|');
                meshNamesList.Append(mesh.Name ?? $"Mesh{i}");
            }

            if (totalVertices == 0)
            {
                Log.Warning("Scene has no vertices", this);
                MeshData.Value = null;
                Points.Value = null;
                OutputMetadata(0, 0, scene.MeshCount, Vector3.Zero, Vector3.Zero, meshNamesList.ToString());
                return;
            }

            // Create vertex and index arrays
            if (_vertexBufferData.Length != totalVertices)
                _vertexBufferData = new PbrVertex[totalVertices];
            if (_indexBufferData.Length != totalFaces)
                _indexBufferData = new Int3[totalFaces];

            // Fill arrays from selected meshes
            var vertexOffset = 0;
            var faceOffset = 0;

            // Track bounding box for normalization
            var minBounds = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var maxBounds = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            for (var i = startMesh; i < endMesh; i++)
            {
                var mesh = scene.Meshes[i];

                // Copy vertices
                for (var v = 0; v < mesh.VertexCount; v++)
                {
                    var pos = mesh.Vertices[v];
                    var normal = mesh.HasNormals ? mesh.Normals[v] : new Vector3(0, 1, 0);
                    var uv = mesh.HasTextureCoords(0) ? mesh.TextureCoordinateChannels[0][v] : new Vector3(0, 0, 0);
                    var color = mesh.HasVertexColors(0) ? mesh.VertexColorChannels[0][v] : new Vector4(1, 1, 1, 1);

                    var position = new Vector3(pos.X, pos.Y, pos.Z);
                    var normalVec = new Vector3(normal.X, normal.Y, normal.Z);

                    // Apply axis conversion to position and normal
                    ApplyAxisConversion(ref position, ref normalVec, axisConversion);

                    // Apply flip normals
                    if (flipNormals)
                        normalVec = -normalVec;

                    // Track bounds for normalization
                    minBounds = Vector3.Min(minBounds, position);
                    maxBounds = Vector3.Max(maxBounds, position);

                    _vertexBufferData[vertexOffset + v] = new PbrVertex
                                                              {
                                                                  Position = position,
                                                                  Normal = normalVec,
                                                                  Tangent = Vector3.UnitX, // Will be calculated later if needed
                                                                  Bitangent = Vector3.UnitY, // Will be calculated later if needed
                                                                  Texcoord = new Vector2(uv.X, uv.Y),
                                                                  Texcoord2 = Vector2.Zero,
                                                                  Selection = color.X // Store first color channel in selection
                                                              };
                }

                // Copy faces as indices
                for (var f = 0; f < mesh.FaceCount; f++)
                {
                    var face = mesh.Faces[f];
                    if (face.IndexCount == 3)
                        _indexBufferData[faceOffset + f] = new Int3(
                                                                    face.Indices[0] + vertexOffset,
                                                                    face.Indices[1] + vertexOffset,
                                                                    face.Indices[2] + vertexOffset
                                                                   );
                }

                vertexOffset += mesh.VertexCount;
                faceOffset += mesh.FaceCount;
            }

            // Calculate tangents from geometry and UVs
            if (totalVertices > 0 && totalFaces > 0)
                CalculateTangents(_vertexBufferData, _indexBufferData, totalFaces);

            // Apply scaling and normalization
            if (scale != 1.0f || normalize)
            {
                var boundsSize = maxBounds - minBounds;
                var maxDimension = MathF.Max(boundsSize.X, MathF.Max(boundsSize.Y, boundsSize.Z));

                // First normalize to unit cube if requested
                var normalizeFactor = maxDimension > 0 && normalize ? 1.0f / maxDimension : 1.0f;
                var finalScale = normalizeFactor * scale;

                if (finalScale != 1.0f)
                {
                    // Calculate center offset for centering the model
                    var center = (minBounds + maxBounds) * 0.5f;

                    for (var i = 0; i < totalVertices; i++)
                    {
                        _vertexBufferData[i].Position = (_vertexBufferData[i].Position - center) * finalScale;
                        // Renormalize normals after uniform scaling (they should remain unit vectors)
                        _vertexBufferData[i].Normal = Vector3.Normalize(_vertexBufferData[i].Normal);
                        // Tangents and bitangents also need renormalization
                        _vertexBufferData[i].Tangent = Vector3.Normalize(_vertexBufferData[i].Tangent);
                        _vertexBufferData[i].Bitangent = Vector3.Normalize(_vertexBufferData[i].Bitangent);
                    }

                    // Update bounds after transformation
                    minBounds = (minBounds - center) * finalScale;
                    maxBounds = (maxBounds - center) * finalScale;

                    Log.Debug($"Applied scaling: normalize={normalize}, scale={scale}, final={finalScale}", this);
                }
            }

            // Create GPU buffers
            ResourceManager.SetupStructuredBuffer(_vertexBufferData, PbrVertex.Stride * totalVertices, PbrVertex.Stride, ref _vertexBuffer);
            ResourceManager.CreateStructuredBufferSrv(_vertexBuffer, ref _vertexBufferWithViews.Srv);
            ResourceManager.CreateStructuredBufferUav(_vertexBuffer, UnorderedAccessViewBufferFlags.None, ref _vertexBufferWithViews.Uav);
            _vertexBufferWithViews.Buffer = _vertexBuffer;

            const int stride = 3 * 4;
            ResourceManager.SetupStructuredBuffer(_indexBufferData, stride * totalFaces, stride, ref _indexBuffer);
            ResourceManager.CreateStructuredBufferSrv(_indexBuffer, ref _indexBufferWithViews.Srv);
            ResourceManager.CreateStructuredBufferUav(_indexBuffer, UnorderedAccessViewBufferFlags.None, ref _indexBufferWithViews.Uav);
            _indexBufferWithViews.Buffer = _indexBuffer;

            _meshData.VertexBuffer = _vertexBufferWithViews;
            _meshData.IndicesBuffer = _indexBufferWithViews;
            MeshData.Value = _meshData;

            OutputMetadata(totalVertices, totalFaces, endMesh - startMesh, minBounds, maxBounds, meshNamesList.ToString());
            Log.Debug($"Loaded {totalVertices} vertices, {totalFaces} faces from {endMesh - startMesh} meshes", this);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to convert scene to mesh buffers: {ex.Message}", this);
            MeshData.Value = null;
        }

        try
        {
            // Determine which meshes to load (same as mesh data)
            var startMesh = 0;
            var endMesh = scene.MeshCount;

            if (selectedMeshIndex >= 0 && selectedMeshIndex < scene.MeshCount)
            {
                startMesh = selectedMeshIndex;
                endMesh = selectedMeshIndex + 1;
            }

            // Count total vertices
            var totalVertices = 0;
            for (var i = startMesh; i < endMesh; i++)
                totalVertices += scene.Meshes[i].VertexCount;

            if (totalVertices == 0)
            {
                Points.Value = null;
                return;
            }

            if (_pointBufferData.Length != totalVertices)
                _pointBufferData = new Point[totalVertices];

            var pointOffset = 0;

            // Track bounds for scaling (same as mesh data)
            var minBounds = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var maxBounds = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (var i = startMesh; i < endMesh; i++)
            {
                var mesh = scene.Meshes[i];
                var hasNormals = mesh.HasNormals;
                var hasColors = mesh.HasVertexColors(0);
                var hasUVs = mesh.HasTextureCoords(0);

                for (var v = 0; v < mesh.VertexCount; v++)
                {
                    var pos = mesh.Vertices[v];
                    var normal = hasNormals ? mesh.Normals[v] : new Vector3(0, 1, 0);
                    var color = hasColors ? mesh.VertexColorChannels[0][v] : new Vector4(1, 1, 1, 1);
                    var uv = hasUVs ? mesh.TextureCoordinateChannels[0][v] : new Vector3(0, 0, 0);

                    var position = new Vector3(pos.X, pos.Y, pos.Z);
                    var normalVec = new Vector3(normal.X, normal.Y, normal.Z);

                    // Apply axis conversion
                    ApplyAxisConversion(ref position, ref normalVec, axisConversion);

                    // Apply flip normals
                    if (flipNormals)
                        normalVec = -normalVec;

                    // Track bounds
                    minBounds = Vector3.Min(minBounds, position);
                    maxBounds = Vector3.Max(maxBounds, position);

                    _pointBufferData[pointOffset++] = new Point
                                                          {
                                                              Position = position,
                                                              Orientation = Quaternion.Identity,
                                                              Scale = Vector3.One,
                                                              Color = new Vector4(color.X, color.Y, color.Z, color.W),
                                                              F1 = 1.0f
                                                          };
                }
            }

            // Apply same scaling as mesh data
            if (scale != 1.0f || normalize)
            {
                var boundsSize = maxBounds - minBounds;
                var maxDimension = MathF.Max(boundsSize.X, MathF.Max(boundsSize.Y, boundsSize.Z));
                var normalizeFactor = maxDimension > 0 && normalize ? 1.0f / maxDimension : 1.0f;
                var finalScale = normalizeFactor * scale;

                if (finalScale != 1.0f)
                {
                    var center = (minBounds + maxBounds) * 0.5f;
                    for (var i = 0; i < totalVertices; i++)
                        _pointBufferData[i].Position = (_pointBufferData[i].Position - center) * finalScale;
                }
            }

            // Create GPU buffer for points
            ResourceManager.SetupStructuredBuffer(_pointBufferData, Point.Stride * totalVertices, Point.Stride, ref _pointBuffer);
            ResourceManager.CreateStructuredBufferSrv(_pointBuffer, ref _pointBufferWithViews.Srv);
            ResourceManager.CreateStructuredBufferUav(_pointBuffer, UnorderedAccessViewBufferFlags.None, ref _pointBufferWithViews.Uav);
            _pointBufferWithViews.Buffer = _pointBuffer;

            Points.Value = _pointBufferWithViews;
            Log.Debug($"Created {totalVertices} points from scene", this);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to convert scene to points: {ex.Message}", this);
            Points.Value = null;
        }
    }

    private bool TryLoadScene(FileResource file, Scene currentValue, out Scene newValue, out string failureReason)
    {
        var absolutePath = file.AbsolutePath;

        try
        {
            using var importer = new AssimpContext();

            // Use cached post process flags value
            var postProcessSteps = GetPostProcessFlags(_cachedPostProcessFlags);

            var scene = importer.ImportFile(absolutePath, postProcessSteps);

            if (scene == null || scene.MeshCount == 0)
            {
                failureReason = $"Failed to load scene or no meshes found in {absolutePath}";
                newValue = null;
                return false;
            }

            failureReason = null;
            newValue = scene;
            return true;
        }
        catch (Exception ex)
        {
            failureReason = $"Exception loading scene: {ex.Message}";
            newValue = null;
            return false;
        }
    }

    private PostProcessSteps GetPostProcessFlags(int flagsValue)
    {
        var flags = (PostProcessPreset)flagsValue;

        return flags switch
                   {
                       PostProcessPreset.None  => PostProcessSteps.None,
                       PostProcessPreset.Basic => PostProcessSteps.Triangulate | PostProcessSteps.GenerateSmoothNormals,
                       PostProcessPreset.Full => PostProcessSteps.Triangulate |
                                                 PostProcessSteps.GenerateSmoothNormals |
                                                 PostProcessSteps.GenerateUVCoords |
                                                 PostProcessSteps.CalculateTangentSpace |
                                                 PostProcessSteps.JoinIdenticalVertices |
                                                 PostProcessSteps.RemoveRedundantMaterials |
                                                 PostProcessSteps.ImproveCacheLocality,
                       _ => PostProcessSteps.Triangulate | PostProcessSteps.GenerateSmoothNormals
                   };
    }

    private void OutputMetadata(int vertexCount, int faceCount, int meshCount, Vector3 boundsMin, Vector3 boundsMax, string meshNames)
    {
        VertexCount.Value = vertexCount;
        FaceCount.Value = faceCount;
        MeshCount.Value = meshCount;
        BoundsMin.Value = boundsMin;
        BoundsMax.Value = boundsMax;
        MeshNames.Value = meshNames;

        var sb = new StringBuilder();
        sb.AppendLine($"Vertices: {vertexCount:N0}");
        sb.AppendLine($"Faces: {faceCount:N0}");
        sb.AppendLine($"Meshes: {meshCount}");
        sb.AppendLine($"Bounds Min: ({boundsMin.X:F2}, {boundsMin.Y:F2}, {boundsMin.Z:F2})");
        sb.AppendLine($"Bounds Max: ({boundsMax.X:F2}, {boundsMax.Y:F2}, {boundsMax.Z:F2})");
        if (meshCount > 1)
            sb.AppendLine($"Mesh Names: {meshNames}");
        Metadata.Value = sb.ToString();
    }

    private void ApplyAxisConversion(ref Vector3 position, ref Vector3 normal, AxisConversion conversion)
    {
        switch (conversion)
        {
            case AxisConversion.YUpToZUp:
                // Convert Y-up to Z-up: swap Y and Z
                (position.Y, position.Z) = (position.Z, position.Y);
                (normal.Y, normal.Z) = (normal.Z, normal.Y);
                break;
            case AxisConversion.ZUpToYUp:
                // Convert Z-up to Y-up: swap Y and Z
                (position.Y, position.Z) = (position.Z, position.Y);
                (normal.Y, normal.Z) = (normal.Z, normal.Y);
                break;
            case AxisConversion.FlipX:
                position.X = -position.X;
                normal.X = -normal.X;
                break;
            case AxisConversion.FlipY:
                position.Y = -position.Y;
                normal.Y = -normal.Y;
                break;
            case AxisConversion.FlipZ:
                position.Z = -position.Z;
                normal.Z = -normal.Z;
                break;
        }
    }

    private void CalculateTangents(PbrVertex[] vertices, Int3[] indices, int faceCount)
    {
        // Initialize tangents to zero
        for (var i = 0; i < vertices.Length; i++)
        {
            vertices[i].Tangent = Vector3.Zero;
            vertices[i].Bitangent = Vector3.Zero;
        }

        // Calculate tangents for each face
        for (var i = 0; i < faceCount; i++)
        {
            var i0 = indices[i].X;
            var i1 = indices[i].Y;
            var i2 = indices[i].Z;

            var v0 = vertices[i0].Position;
            var v1 = vertices[i1].Position;
            var v2 = vertices[i2].Position;

            var uv0 = vertices[i0].Texcoord;
            var uv1 = vertices[i1].Texcoord;
            var uv2 = vertices[i2].Texcoord;

            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var deltaUv1 = uv1 - uv0;
            var deltaUv2 = uv2 - uv0;

            // Handle degenerate UVs
            var denominator = deltaUv1.X * deltaUv2.Y - deltaUv2.X * deltaUv1.Y;
            if (MathF.Abs(denominator) < 1e-6f)
                continue; // Skip faces with degenerate UV coordinates

            var f = 1.0f / denominator;

            var tangent = new Vector3(
                                      f * (deltaUv2.Y * edge1.X - deltaUv1.Y * edge2.X),
                                      f * (deltaUv2.Y * edge1.Y - deltaUv1.Y * edge2.Y),
                                      f * (deltaUv2.Y * edge1.Z - deltaUv1.Y * edge2.Z)
                                     );

            var bitangent = new Vector3(
                                        f * (-deltaUv2.X * edge1.X + deltaUv1.X * edge2.X),
                                        f * (-deltaUv2.X * edge1.Y + deltaUv1.X * edge2.Y),
                                        f * (-deltaUv2.X * edge1.Z + deltaUv1.X * edge2.Z)
                                       );

            vertices[i0].Tangent += tangent;
            vertices[i1].Tangent += tangent;
            vertices[i2].Tangent += tangent;

            vertices[i0].Bitangent += bitangent;
            vertices[i1].Bitangent += bitangent;
            vertices[i2].Bitangent += bitangent;
        }

        // Orthogonalize tangents and normalize
        for (var i = 0; i < vertices.Length; i++)
        {
            var t = vertices[i].Tangent;
            var n = vertices[i].Normal;
            var b = vertices[i].Bitangent;

            // Skip if no valid tangent was calculated
            if (t.LengthSquared() < 0.0001f)
            {
                // Generate default tangent from normal
                var absNx = MathF.Abs(n.X);
                var absNy = MathF.Abs(n.Y);
                var absNz = MathF.Abs(n.Z);

                // Choose best axis for cross product
                if (absNx <= absNy && absNx <= absNz)
                    vertices[i].Tangent = Vector3.Normalize(Vector3.Cross(Vector3.UnitX, n));
                else if (absNy <= absNx && absNy <= absNz)
                    vertices[i].Tangent = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, n));
                else
                    vertices[i].Tangent = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, n));

                vertices[i].Bitangent = Vector3.Cross(n, vertices[i].Tangent);
                continue;
            }

            // Gram-Schmidt orthogonalize
            var tOrtho = t - n * Vector3.Dot(n, t);
            if (tOrtho.LengthSquared() > 0.0001f)
                tOrtho = Vector3.Normalize(tOrtho);

            // Calculate handedness
            var handedness = Vector3.Dot(Vector3.Cross(n, t), b) < 0.0f ? -1.0f : 1.0f;

            vertices[i].Tangent = tOrtho;
            vertices[i].Bitangent = Vector3.Normalize(b) * handedness;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pointBuffer?.Dispose();
            _pointBufferWithViews?.Dispose();
            _vertexBuffer?.Dispose();
            _indexBuffer?.Dispose();
            _vertexBufferWithViews?.Dispose();
            _indexBufferWithViews?.Dispose();
            _meshData?.Dispose();
        }

        base.Dispose(disposing);
    }

    private enum PostProcessPreset
    {
        None,
        Basic,
        Full
    }

    private enum AxisConversion
    {
        None,
        YUpToZUp,
        ZUpToYUp,
        FlipX,
        FlipY,
        FlipZ
    }
}
