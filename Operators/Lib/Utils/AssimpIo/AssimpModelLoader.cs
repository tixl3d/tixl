#nullable enable
using System.Threading;
using Assimp.Unmanaged;
using T3.Core.Rendering;
using AssimpBone = Assimp.Bone;
using AssimpMesh = Assimp.Mesh;
using AssimpNode = Assimp.Node;
using AssimpScene = Assimp.Scene;
using AnimationInterpolation = Assimp.AnimationInterpolation;
using PostProcessSteps = Assimp.PostProcessSteps;

namespace Lib.Utils.AssimpIo;

/// <summary>
/// Imports a model file with Assimp and converts it into <see cref="AssimpModelData"/>.
/// Pure CPU work without graphics calls, so it is meant to run on a worker thread.
/// </summary>
/// <remarks>
/// The file is parsed once. Assimp's scene is snapshotted before triangulation (feeding the
/// N-gon <see cref="MeshGeometry"/>) and after it (feeding the GPU mesh). Node transforms are
/// baked into the vertices, and the import transform (axis conversion, normalization, scale)
/// is carried by a synthetic root joint, so skinning stays consistent with the transformed mesh.
/// Assimp matrices are stored for column vectors; they are transposed into the row-vector
/// convention of System.Numerics on read.
/// </remarks>
internal static class AssimpModelLoader
{
    private readonly record struct MeshInstance(int MeshIndex, AssimpNode Node, Matrix4x4 FinalTransform, Matrix4x4 NormalTransform, bool ReverseWinding);

    private readonly record struct SceneNodeEntry(AssimpNode Node, Matrix4x4 Global);

    /// <summary>Upper bound that keeps the vertex and index arrays within reasonable GPU buffer sizes.</summary>
    public const int MaxElementCount = 50_000_000;

    /// <summary>
    /// Parses the file. The result only depends on the file and the preset, so callers can keep it
    /// and run <see cref="Convert"/> again when other settings change. Safe to share between threads
    /// once returned, because conversion only reads it.
    /// </summary>
    public static AssimpImportedScenes Import(string absolutePath, AssimpPostProcessPresets preset, CancellationToken token)
    {
        if (!File.Exists(absolutePath))
            throw new FileNotFoundException($"File not found: {absolutePath}");

        // Assimp reads the file into memory and expands it several times while converting; a file that
        // already fills a large part of the address space would take the process down before any
        // conversion limit could apply. The cap is generous for real-world models.
        var fileLength = new FileInfo(absolutePath).Length;
        if (fileLength > MaxInputFileBytes)
            throw new InvalidDataException($"File is too large ({fileLength / (1024.0 * 1024 * 1024):0.0} GB; limit is "
                                           + $"{MaxInputFileBytes / (1024.0 * 1024 * 1024):0.0} GB)");

        if (!AssimpNative.TryEnsureLoaded(out var nativeError))
            throw new InvalidOperationException(nativeError);

        lock (AssimpNative.Lock)
        {
            token.ThrowIfCancellationRequested();
            ImportSnapshots(absolutePath, preset, out var polygonScene, out var triangleScene);
            return new AssimpImportedScenes(absolutePath, preset, polygonScene, triangleScene);
        }
    }

    public static AssimpModelData Load(string absolutePath, in AssimpImportSettings settings, CancellationToken token)
    {
        var scenes = Import(absolutePath, settings.Preset, token);
        token.ThrowIfCancellationRequested();
        return Convert(scenes, settings, reusePreviousMaterials: false, token);
    }

    #region native import
    private static void ImportSnapshots(string absolutePath, AssimpPostProcessPresets preset,
                                        out AssimpScene polygonScene, out AssimpScene triangleScene)
    {
        var library = AssimpLibrary.Instance;
        var propertyStore = library.CreatePropertyStore();
        var scenePointer = IntPtr.Zero;
        try
        {
            scenePointer = library.ImportFile(absolutePath, GetImportSteps(preset), IntPtr.Zero, propertyStore);
            if (scenePointer == IntPtr.Zero)
                throw new InvalidDataException($"Assimp could not read the file: {library.GetErrorString()}");

            polygonScene = AssimpScene.FromUnmanagedScene(scenePointer);

            var processedPointer = library.ApplyPostProcessing(scenePointer, GetTriangulationSteps(preset));
            if (processedPointer == IntPtr.Zero)
            {
                // Assimp releases the scene itself when post-processing fails
                scenePointer = IntPtr.Zero;
                throw new InvalidDataException($"Assimp failed to triangulate the file: {library.GetErrorString()}");
            }

            scenePointer = processedPointer;
            triangleScene = AssimpScene.FromUnmanagedScene(scenePointer);
        }
        finally
        {
            if (scenePointer != IntPtr.Zero)
                library.ReleaseImport(scenePointer);

            library.ReleasePropertyStore(propertyStore);
        }
    }

    private static PostProcessSteps GetImportSteps(AssimpPostProcessPresets preset)
    {
        // Validation turns malformed files into import errors instead of out-of-range data
        const PostProcessSteps alwaysApplied = PostProcessSteps.ValidateDataStructure | PostProcessSteps.LimitBoneWeights;

        return preset switch
                   {
                       AssimpPostProcessPresets.None => alwaysApplied,
                       AssimpPostProcessPresets.Full => alwaysApplied
                                                       | PostProcessSteps.GenerateSmoothNormals
                                                       | PostProcessSteps.GenerateUVCoords
                                                       | PostProcessSteps.CalculateTangentSpace
                                                       | PostProcessSteps.JoinIdenticalVertices
                                                       | PostProcessSteps.RemoveRedundantMaterials,
                       _ => alwaysApplied | PostProcessSteps.GenerateSmoothNormals,
                   };
    }

    private static PostProcessSteps GetTriangulationSteps(AssimpPostProcessPresets preset)
    {
        return preset == AssimpPostProcessPresets.Full
                   ? PostProcessSteps.Triangulate | PostProcessSteps.ImproveCacheLocality
                   : PostProcessSteps.Triangulate;
    }
    #endregion

    #region conversion
    /// <summary>
    /// Builds all outputs from parsed scenes. Pure CPU work, meant for a worker thread. With
    /// <paramref name="reusePreviousMaterials"/> materials are only assigned to chunks, not read and
    /// decoded again; the caller guarantees the same scenes and mesh selection as before.
    /// </summary>
    public static AssimpModelData Convert(AssimpImportedScenes scenes, in AssimpImportSettings settings, bool reusePreviousMaterials,
                                          CancellationToken token)
    {
        var triangleScene = scenes.TriangleScene;
        var polygonScene = scenes.PolygonScene;
        if (triangleScene.RootNode == null)
            throw new InvalidDataException("File has no scene hierarchy");

        var data = new AssimpModelData
                       {
                           FileMeshCount = triangleScene.MeshCount,
                           MeshNames = BuildMeshNames(triangleScene),
                       };

        var nodes = FlattenNodes(triangleScene.RootNode, data.Warnings, token);
        var nodeInstances = CollectNodeInstances(triangleScene, nodes, settings.SelectedMeshIndex);
        if (nodeInstances.Count == 0)
        {
            throw new InvalidDataException(settings.SelectedMeshIndex >= 0
                                               ? $"Mesh index {settings.SelectedMeshIndex} is not used by the file ({triangleScene.MeshCount} meshes)"
                                               : "File contains no meshes");
        }

        var importTransform = ComputeImportTransform(triangleScene, nodeInstances, settings);
        var importMatrix = importTransform.ToTransform();

        var instances = new List<MeshInstance>(nodeInstances.Count);
        foreach (var (meshIndex, node, global) in nodeInstances)
        {
            var finalTransform = global * importMatrix;
            if (!Matrix4x4.Invert(finalTransform, out var inverse))
            {
                data.Warnings.Add($"Skipped mesh {meshIndex} in node '{node.Name}': degenerate transform");
                continue;
            }

            var isMirrored = finalTransform.GetDeterminant() < 0;
            instances.Add(new MeshInstance(meshIndex, node, finalTransform, Matrix4x4.Transpose(inverse), isMirrored ^ settings.FlipNormals));
        }

        token.ThrowIfCancellationRequested();
        BuildMesh(triangleScene, instances, settings.FlipNormals, data, token);

        token.ThrowIfCancellationRequested();
        BuildMaterials(triangleScene, scenes.AbsolutePath, instances, settings.ImportMaterials, reusePreviousMaterials, data, token);

        token.ThrowIfCancellationRequested();
        var geometryScene = polygonScene.MeshCount == triangleScene.MeshCount ? polygonScene : triangleScene;
        data.Geometry = BuildGeometry(geometryScene, instances, settings.FlipNormals, data.Warnings, token);

        token.ThrowIfCancellationRequested();
        BuildSkeleton(triangleScene, nodes, instances, importTransform, importMatrix, data, token);

        data.AnimationNames = BuildAnimationNames(data.AnimationClips);
        data.UsedMeshInstanceCount = instances.Count;
        return data;
    }

    /// <summary>Pre-order traversal (parents before children) with the scene-root-relative transform of every node.</summary>
    private static List<SceneNodeEntry> FlattenNodes(AssimpNode root, List<string> warnings, CancellationToken token)
    {
        const int maxNodeCount = 1_000_000;

        var nodes = new List<SceneNodeEntry>();
        var stack = new Stack<SceneNodeEntry>();
        stack.Push(new SceneNodeEntry(root, Matrix4x4.Transpose(root.Transform)));

        while (stack.Count > 0)
        {
            if ((nodes.Count & 0xFFF) == 0)
                token.ThrowIfCancellationRequested();

            var entry = stack.Pop();
            nodes.Add(entry);
            if (nodes.Count > maxNodeCount)
            {
                warnings.Add($"Scene hierarchy truncated after {maxNodeCount} nodes");
                break;
            }

            var children = entry.Node.Children;
            for (var childIndex = children.Count - 1; childIndex >= 0; childIndex--)
            {
                var child = children[childIndex];
                stack.Push(new SceneNodeEntry(child, Matrix4x4.Transpose(child.Transform) * entry.Global));
            }
        }

        return nodes;
    }

    private static List<(int MeshIndex, AssimpNode Node, Matrix4x4 Global)> CollectNodeInstances(AssimpScene scene,
                                                                                                  List<SceneNodeEntry> nodes,
                                                                                                  int selectedMeshIndex)
    {
        var instances = new List<(int, AssimpNode, Matrix4x4)>();
        foreach (var (node, global) in nodes)
        {
            if (!node.HasMeshes)
                continue;

            foreach (var meshIndex in node.MeshIndices)
            {
                if (meshIndex < 0 || meshIndex >= scene.MeshCount)
                    continue;

                if (selectedMeshIndex >= 0 && meshIndex != selectedMeshIndex)
                    continue;

                instances.Add((meshIndex, node, global));
            }
        }

        return instances;
    }

    /// <summary>
    /// Axis conversion, then optional normalization into a unit cube around the origin, then scale.
    /// Expressed as TRS so the synthetic skeleton root can carry it.
    /// </summary>
    private static SceneSetup.Transform ComputeImportTransform(AssimpScene scene,
                                                              List<(int MeshIndex, AssimpNode Node, Matrix4x4 Global)> instances,
                                                              in AssimpImportSettings settings)
    {
        var axisRotation = Quaternion.Identity;
        var axisScale = Vector3.One;
        switch (settings.AxisConversion)
        {
            case AssimpAxisConversions.YUpToZUp:
                axisRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
                break;
            case AssimpAxisConversions.ZUpToYUp:
                axisRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2);
                break;
            case AssimpAxisConversions.FlipX:
                axisScale = new Vector3(-1, 1, 1);
                break;
            case AssimpAxisConversions.FlipY:
                axisScale = new Vector3(1, -1, 1);
                break;
            case AssimpAxisConversions.FlipZ:
                axisScale = new Vector3(1, 1, -1);
                break;
        }

        var scale = MathF.Abs(settings.Scale) < MinimumScale ? MathF.CopySign(MinimumScale, settings.Scale) : settings.Scale;
        if (!float.IsFinite(scale))
            scale = 1;

        var center = Vector3.Zero;
        if (settings.Normalize)
        {
            var axisMatrix = Matrix4x4.CreateScale(axisScale) * Matrix4x4.CreateFromQuaternion(axisRotation);
            var min = new Vector3(float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity);
            foreach (var (meshIndex, _, global) in instances)
            {
                var transform = global * axisMatrix;
                foreach (var vertex in scene.Meshes[meshIndex].Vertices)
                {
                    var position = Vector3.Transform(vertex, transform);
                    min = Vector3.Min(min, position);
                    max = Vector3.Max(max, position);
                }
            }

            if (float.IsFinite(min.X) && float.IsFinite(max.X))
            {
                var size = max - min;
                var maxDimension = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
                center = (min + max) * 0.5f;
                if (maxDimension > 0)
                    scale /= maxDimension;
            }
        }

        return new SceneSetup.Transform
                   {
                       Translation = -center * scale,
                       Rotation = axisRotation,
                       Scale = axisScale * scale,
                   };
    }

    private static void BuildMesh(AssimpScene scene, List<MeshInstance> instances, bool flipNormals, AssimpModelData data, CancellationToken token)
    {
        long vertexCount = 0;
        long triangleCount = 0;
        foreach (var instance in instances)
        {
            var mesh = scene.Meshes[instance.MeshIndex];
            vertexCount += mesh.VertexCount;
            triangleCount += CountTriangles(mesh);
        }

        if (vertexCount > MaxElementCount || triangleCount > MaxElementCount)
            throw new InvalidDataException($"Model is too large ({vertexCount:N0} vertices, {triangleCount:N0} triangles; limit is {MaxElementCount:N0})");

        var vertices = new PbrVertex[vertexCount];
        var triangles = new Int3[triangleCount];
        var points = new Point[vertexCount];
        var chunks = new MeshChunkDef[instances.Count];
        var needsTangents = new bool[vertexCount];

        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        var vertexOffset = 0;
        var triangleOffset = 0;
        var skippedFaceCount = 0;
        var invalidValueCount = 0;

        for (var instanceIndex = 0; instanceIndex < instances.Count; instanceIndex++)
        {
            token.ThrowIfCancellationRequested();

            var instance = instances[instanceIndex];
            var mesh = scene.Meshes[instance.MeshIndex];
            var meshVertexCount = mesh.VertexCount;

            var normals = mesh.HasNormals && mesh.Normals.Count == meshVertexCount ? mesh.Normals : null;
            var uvs = GetTextureChannel(mesh, 0);
            var uvs2 = GetTextureChannel(mesh, 1);
            var colors = mesh.HasVertexColors(0) && mesh.VertexColorChannels[0].Count == meshVertexCount ? mesh.VertexColorChannels[0] : null;
            var hasTangentBasis = mesh.HasTangentBasis
                                  && mesh.Tangents.Count == meshVertexCount
                                  && mesh.BiTangents.Count == meshVertexCount;

            var positions = mesh.Vertices;
            for (var localIndex = 0; localIndex < meshVertexCount; localIndex++)
            {
                var vertexIndex = vertexOffset + localIndex;
                var position = Vector3.Transform(positions[localIndex], instance.FinalTransform);
                if (!IsFinite(position))
                {
                    // A single NaN would spread through bounds, normalization and the GPU buffers
                    position = Vector3.Zero;
                    invalidValueCount++;
                }

                var normal = normals != null ? TransformDirection(normals[localIndex], instance.NormalTransform) : Vector3.Zero;
                var tangent = Vector3.UnitX;
                var bitangent = Vector3.UnitY;
                if (hasTangentBasis)
                {
                    tangent = TransformDirection(mesh.Tangents[localIndex], instance.FinalTransform);
                    bitangent = TransformDirection(mesh.BiTangents[localIndex], instance.FinalTransform);
                }
                else
                {
                    needsTangents[vertexIndex] = true;
                }

                if (flipNormals)
                {
                    normal = -normal;
                    bitangent = -bitangent;
                }

                var uv = uvs != null ? uvs[localIndex] : Vector3.Zero;
                var uv2 = uvs2 != null ? uvs2[localIndex] : uv;
                var color = colors != null ? colors[localIndex] : Vector4.One;
                var texcoord = SanitizeUv(uv);
                var texcoord2 = SanitizeUv(uv2);
                var sanitizedColor = SanitizeColor(color);
                if (texcoord != new Vector2(uv.X, uv.Y) || texcoord2 != new Vector2(uv2.X, uv2.Y) || sanitizedColor != color)
                    invalidValueCount++;

                vertices[vertexIndex] = new PbrVertex
                                            {
                                                Position = position,
                                                Normal = normal,
                                                Tangent = tangent,
                                                Bitangent = bitangent,
                                                Texcoord = texcoord,
                                                Texcoord2 = texcoord2,
                                                Selection = 1,
                                                ColorRgb = new Vector3(sanitizedColor.X, sanitizedColor.Y, sanitizedColor.Z),
                                            };

                points[vertexIndex] = new Point
                                          {
                                              Position = position,
                                              F1 = 1,
                                              Orientation = Quaternion.Identity,
                                              Color = sanitizedColor,
                                              Scale = Vector3.One,
                                              F2 = 1,
                                          };

                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            var chunkTriangleStart = triangleOffset;
            foreach (var face in mesh.Faces)
            {
                if (face.IndexCount != 3)
                    continue;

                var indices = face.Indices;
                var a = indices[0];
                var b = indices[1];
                var c = indices[2];
                if ((uint)a >= (uint)meshVertexCount || (uint)b >= (uint)meshVertexCount || (uint)c >= (uint)meshVertexCount)
                {
                    skippedFaceCount++;
                    continue;
                }

                triangles[triangleOffset++] = instance.ReverseWinding
                                                  ? new Int3(vertexOffset + a, vertexOffset + c, vertexOffset + b)
                                                  : new Int3(vertexOffset + a, vertexOffset + b, vertexOffset + c);
            }

            if (normals == null)
                ComputeVertexNormals(vertices, triangles, chunkTriangleStart, triangleOffset);

            chunks[instanceIndex] = new MeshChunkDef
                                        {
                                            StartFaceIndex = chunkTriangleStart,
                                            FaceCount = triangleOffset - chunkTriangleStart,
                                            StartVertexIndex = vertexOffset,
                                            VertexCount = meshVertexCount,
                                        };
            vertexOffset += meshVertexCount;
        }

        if (triangleOffset < triangles.Length)
            Array.Resize(ref triangles, triangleOffset);

        if (skippedFaceCount > 0)
            data.Warnings.Add($"Skipped {skippedFaceCount} faces with invalid indices");

        if (invalidValueCount > 0)
            data.Warnings.Add($"Replaced {invalidValueCount} invalid vertex values (NaN/infinite)");

        CalculateMissingTangents(vertices, triangles, needsTangents);

        data.Vertices = vertices;
        data.Triangles = triangles;
        data.Points = points;
        data.Chunks = chunks;
        data.VertexCount = vertices.Length;
        data.FaceCount = triangles.Length;
        data.BoundsMin = vertexCount > 0 ? min : Vector3.Zero;
        data.BoundsMax = vertexCount > 0 ? max : Vector3.Zero;
    }

    private static void BuildMaterials(AssimpScene scene, string modelPath, List<MeshInstance> instances, bool importMaterials,
                                       bool reusePreviousMaterials, AssimpModelData data, CancellationToken token)
    {
        var chunkMaterials = new int[instances.Count];
        Array.Fill(chunkMaterials, -1);
        data.ChunkMaterialIndices = chunkMaterials;
        if (!importMaterials)
            return;

        data.ReusesPreviousMaterials = reusePreviousMaterials;
        var reader = new AssimpMaterialReader(scene, modelPath, data.Warnings, token);
        var dataIndexByFileIndex = new Dictionary<int, int>();
        var materialCount = 0;
        for (var instanceIndex = 0; instanceIndex < instances.Count; instanceIndex++)
        {
            var fileMaterialIndex = scene.Meshes[instances[instanceIndex].MeshIndex].MaterialIndex;
            if ((uint)fileMaterialIndex >= (uint)scene.MaterialCount)
                continue;

            // Indices are assigned in first-use order, so a reused material list lines up again
            if (!dataIndexByFileIndex.TryGetValue(fileMaterialIndex, out var dataIndex))
            {
                dataIndex = materialCount++;
                if (!reusePreviousMaterials)
                    data.Materials.Add(reader.Read(scene.Materials[fileMaterialIndex]));

                dataIndexByFileIndex.Add(fileMaterialIndex, dataIndex);
            }

            chunkMaterials[instanceIndex] = dataIndex;
        }
    }

    /// <summary>
    /// Builds the polygonal geometry. Positions are welded per mesh instance so faces split at
    /// UV or normal seams still share their edges, which beveling and edge topology rely on.
    /// </summary>
    private static MeshGeometry BuildGeometry(AssimpScene scene, List<MeshInstance> instances, bool flipNormals, List<string> warnings,
                                              CancellationToken token)
    {
        var positions = new List<Vector3>();
        var faceOffsets = new List<int> { 0 };
        var cornerPoints = new List<int>();
        var cornerNormals = new List<Vector3>();
        var cornerUvs = new List<Vector2>();
        var cornerUvs2 = new List<Vector2>();
        var cornerColors = new List<Vector4>();
        var parts = new List<GeometryPart>(instances.Count);

        var hasUvs = false;
        var hasUvs2 = false;
        var hasColors = false;
        var invalidValueCount = 0;
        var weldedPointIndices = new Dictionary<Vector3, int>();
        var pointIndexForVertex = new List<int>();

        for (var instanceIndex = 0; instanceIndex < instances.Count; instanceIndex++)
        {
            token.ThrowIfCancellationRequested();

            var instance = instances[instanceIndex];
            var mesh = scene.Meshes[instance.MeshIndex];
            var meshVertexCount = mesh.VertexCount;
            var normals = mesh.HasNormals && mesh.Normals.Count == meshVertexCount ? mesh.Normals : null;
            var uvs = GetTextureChannel(mesh, 0);
            var uvs2 = GetTextureChannel(mesh, 1);
            var colors = mesh.HasVertexColors(0) && mesh.VertexColorChannels[0].Count == meshVertexCount ? mesh.VertexColorChannels[0] : null;
            hasUvs |= uvs != null;
            hasUvs2 |= uvs2 != null;
            hasColors |= colors != null;

            weldedPointIndices.Clear();
            pointIndexForVertex.Clear();
            var firstPointIndex = positions.Count;
            foreach (var vertex in mesh.Vertices)
            {
                var position = Vector3.Transform(vertex, instance.FinalTransform);
                if (!IsFinite(position))
                {
                    position = Vector3.Zero;
                    invalidValueCount++;
                }

                if (!weldedPointIndices.TryGetValue(position, out var pointIndex))
                {
                    pointIndex = positions.Count;
                    positions.Add(position);
                    weldedPointIndices.Add(position, pointIndex);
                }

                pointIndexForVertex.Add(pointIndex);
            }

            var faceStart = faceOffsets.Count - 1;
            foreach (var face in mesh.Faces)
            {
                var indices = face.Indices;
                var cornerCount = indices.Count;
                if (cornerCount < 3 || !AreIndicesValid(indices, meshVertexCount))
                    continue;

                var firstCorner = cornerPoints.Count;
                for (var cornerIndex = 0; cornerIndex < cornerCount; cornerIndex++)
                {
                    // Reversing keeps the first corner in place so fan triangulation stays stable
                    var sourceIndex = instance.ReverseWinding && cornerIndex > 0 ? cornerCount - cornerIndex : cornerIndex;
                    var vertexIndex = indices[sourceIndex];

                    cornerPoints.Add(pointIndexForVertex[vertexIndex]);

                    var normal = Vector3.Zero;
                    if (normals != null)
                    {
                        normal = TransformDirection(normals[vertexIndex], instance.NormalTransform);
                        if (flipNormals)
                            normal = -normal;
                    }

                    cornerNormals.Add(normal);

                    var uv = uvs != null ? uvs[vertexIndex] : Vector3.Zero;
                    var uv2 = uvs2 != null ? uvs2[vertexIndex] : uv;
                    var sourceColor = colors != null ? colors[vertexIndex] : Vector4.One;
                    var texcoord = SanitizeUv(uv);
                    var texcoord2 = SanitizeUv(uv2);
                    var color = SanitizeColor(sourceColor);
                    if (texcoord != new Vector2(uv.X, uv.Y) || texcoord2 != new Vector2(uv2.X, uv2.Y) || color != sourceColor)
                        invalidValueCount++;

                    cornerUvs.Add(texcoord);
                    cornerUvs2.Add(texcoord2);
                    cornerColors.Add(color);
                }

                if (normals == null)
                    FillFaceNormal(positions, cornerPoints, cornerNormals, firstCorner);

                faceOffsets.Add(cornerPoints.Count);
            }

            var faceCount = faceOffsets.Count - 1 - faceStart;
            if (faceCount == 0)
                continue;

            var pivot = Vector3.Zero;
            for (var pointIndex = firstPointIndex; pointIndex < positions.Count; pointIndex++)
            {
                pivot += positions[pointIndex];
            }

            if (positions.Count > firstPointIndex)
                pivot /= positions.Count - firstPointIndex;

            parts.Add(new GeometryPart(faceStart, faceCount, pivot, parts.Count, instance.MeshIndex));
        }

        var geometry = new MeshGeometry
                           {
                               Positions = positions.ToArray(),
                               FaceCornerOffsets = faceOffsets.ToArray(),
                               CornerPointIndices = cornerPoints.ToArray(),
                               Parts = parts.ToArray(),
                           };

        CopyToAttribute(geometry, GeometryAttributeNames.Normal, cornerNormals);
        if (hasUvs)
            CopyToAttribute(geometry, GeometryAttributeNames.TexCoord, cornerUvs);

        if (hasUvs2)
            CopyToAttribute(geometry, GeometryAttributeNames.TexCoord2, cornerUvs2);

        if (hasColors)
            CopyToAttribute(geometry, GeometryAttributeNames.Color, cornerColors);

        if (invalidValueCount > 0)
            warnings.Add($"Replaced {invalidValueCount} invalid geometry values (NaN/infinite)");

        geometry.InvalidateTopologyCaches();
        return geometry;
    }

    /// <summary>
    /// Builds a skeleton from all bones of the imported meshes plus their ancestor nodes (which carry
    /// hierarchy transforms and are often animated themselves), skin weights for every vertex, and
    /// the animation clips targeting those nodes.
    /// </summary>
    private static void BuildSkeleton(AssimpScene scene,
                                      List<SceneNodeEntry> nodes,
                                      List<MeshInstance> instances,
                                      SceneSetup.Transform importTransform,
                                      Matrix4x4 importMatrix,
                                      AssimpModelData data,
                                      CancellationToken token)
    {
        if (!Matrix4x4.Invert(importMatrix, out var inverseImportMatrix))
            return;

        // Inverse bind matrix of each bone in the space of the baked vertices: inv(Import) * inv(MeshGlobal) * Offset
        var inverseBindMatricesByBone = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
        foreach (var instance in instances)
        {
            var mesh = scene.Meshes[instance.MeshIndex];
            if (!mesh.HasBones)
                continue;

            if (!Matrix4x4.Invert(instance.FinalTransform, out var inverseFinal))
                continue;

            foreach (var bone in mesh.Bones)
            {
                if (string.IsNullOrEmpty(bone.Name) || inverseBindMatricesByBone.ContainsKey(bone.Name))
                    continue;

                inverseBindMatricesByBone[bone.Name] = inverseFinal * Matrix4x4.Transpose(bone.OffsetMatrix);
            }
        }

        if (inverseBindMatricesByBone.Count == 0)
            return;

        token.ThrowIfCancellationRequested();

        var nodeEntryIndexByNode = new Dictionary<AssimpNode, int>(nodes.Count);
        var nodeEntryIndexByName = new Dictionary<string, int>(nodes.Count, StringComparer.Ordinal);
        for (var entryIndex = 0; entryIndex < nodes.Count; entryIndex++)
        {
            var node = nodes[entryIndex].Node;
            nodeEntryIndexByNode[node] = entryIndex;
            if (node.Name != null)
                nodeEntryIndexByName.TryAdd(node.Name, entryIndex);
        }

        var isJointEntry = new bool[nodes.Count];
        foreach (var boneName in inverseBindMatricesByBone.Keys)
        {
            if (!nodeEntryIndexByName.TryGetValue(boneName, out var entryIndex))
            {
                data.Warnings.Add($"Bone '{boneName}' has no node in the scene hierarchy");
                continue;
            }

            for (var node = nodes[entryIndex].Node; node != null; node = node.Parent)
            {
                if (!nodeEntryIndexByNode.TryGetValue(node, out var ancestorIndex) || isJointEntry[ancestorIndex])
                    break;

                isJointEntry[ancestorIndex] = true;
            }
        }

        // Joint 0 carries the import transform; scene nodes follow in pre-order, so parents precede children
        var jointIndexByEntry = new int[nodes.Count];
        var jointCount = 1;
        for (var entryIndex = 0; entryIndex < nodes.Count; entryIndex++)
        {
            jointIndexByEntry[entryIndex] = isJointEntry[entryIndex] ? jointCount++ : -1;
        }

        var skeleton = new SceneSetup.SceneSkeleton
                           {
                               JointNames = new string[jointCount],
                               ParentIndices = new int[jointCount],
                               RestLocalTransforms = new SceneSetup.Transform[jointCount],
                               InverseBindMatrices = new Matrix4x4[jointCount],
                           };
        var restPoints = new Point[jointCount];

        skeleton.JointNames[0] = ImportRootJointName;
        skeleton.ParentIndices[0] = -1;
        skeleton.RestLocalTransforms[0] = importTransform;
        skeleton.InverseBindMatrices[0] = inverseImportMatrix;
        restPoints[0] = CreateJointPoint(importMatrix, -1);

        var hasDecomposeFailure = false;
        var jointIndexByName = new Dictionary<string, int>(jointCount, StringComparer.Ordinal);
        for (var entryIndex = 0; entryIndex < nodes.Count; entryIndex++)
        {
            var jointIndex = jointIndexByEntry[entryIndex];
            if (jointIndex < 0)
                continue;

            var (node, global) = nodes[entryIndex];
            var parentIndex = 0;
            if (node.Parent != null && nodeEntryIndexByNode.TryGetValue(node.Parent, out var parentEntry) && jointIndexByEntry[parentEntry] > 0)
                parentIndex = jointIndexByEntry[parentEntry];

            var objectTransform = global * importMatrix;
            skeleton.JointNames[jointIndex] = node.Name ?? $"Joint {jointIndex}";
            skeleton.ParentIndices[jointIndex] = parentIndex;
            skeleton.RestLocalTransforms[jointIndex] = DecomposeTransform(Matrix4x4.Transpose(node.Transform), ref hasDecomposeFailure);

            if (node.Name != null && inverseBindMatricesByBone.TryGetValue(node.Name, out var inverseBind))
            {
                skeleton.InverseBindMatrices[jointIndex] = inverseBind;
            }
            else
            {
                skeleton.InverseBindMatrices[jointIndex] = Matrix4x4.Invert(objectTransform, out var inverseObject) ? inverseObject : Matrix4x4.Identity;
            }

            restPoints[jointIndex] = CreateJointPoint(objectTransform, parentIndex);
            if (node.Name != null)
                jointIndexByName.TryAdd(node.Name, jointIndex);
        }

        if (hasDecomposeFailure)
            data.Warnings.Add("Some joint transforms contain shear and were approximated");

        token.ThrowIfCancellationRequested();
        data.SkinWeights = BuildSkinWeights(scene, instances, data.Vertices.Length, jointIndexByName, nodeEntryIndexByNode, jointIndexByEntry);

        token.ThrowIfCancellationRequested();
        ExtractAnimationClips(scene, jointIndexByName, data);

        data.Skeleton = skeleton;
        data.SkeletonRestPoints = restPoints;
        data.BoneCount = inverseBindMatricesByBone.Count;
    }

    private static AssimpSkinWeight[] BuildSkinWeights(AssimpScene scene,
                                                       List<MeshInstance> instances,
                                                       int vertexCount,
                                                       Dictionary<string, int> jointIndexByName,
                                                       Dictionary<AssimpNode, int> nodeEntryIndexByNode,
                                                       int[] jointIndexByEntry)
    {
        var weights = new AssimpSkinWeight[vertexCount];
        var vertexOffset = 0;
        foreach (var instance in instances)
        {
            var mesh = scene.Meshes[instance.MeshIndex];
            var meshVertexCount = mesh.VertexCount;

            if (mesh.HasBones)
            {
                foreach (var bone in mesh.Bones)
                {
                    if (bone.Name == null || !jointIndexByName.TryGetValue(bone.Name, out var jointIndex))
                        continue;

                    AddBoneInfluences(bone, jointIndex, weights, vertexOffset, meshVertexCount);
                }
            }

            // Unweighted vertices follow their mesh node rigidly, or stay put if that node isn't a joint
            var rigidJointIndex = nodeEntryIndexByNode.TryGetValue(instance.Node, out var entryIndex) && jointIndexByEntry[entryIndex] > 0
                                      ? jointIndexByEntry[entryIndex]
                                      : 0;

            for (var vertexIndex = vertexOffset; vertexIndex < vertexOffset + meshVertexCount; vertexIndex++)
            {
                ref var weight = ref weights[vertexIndex];
                var sum = weight.Weights.X + weight.Weights.Y + weight.Weights.Z + weight.Weights.W;
                if (sum > 1e-6f)
                {
                    weight.Weights /= sum;
                }
                else
                {
                    weight.JointIndices = new Int4(rigidJointIndex, 0, 0, 0);
                    weight.Weights = new Vector4(1, 0, 0, 0);
                }
            }

            vertexOffset += meshVertexCount;
        }

        return weights;
    }

    /// <summary>Keeps the four strongest influences per vertex.</summary>
    private static void AddBoneInfluences(AssimpBone bone, int jointIndex, AssimpSkinWeight[] weights, int vertexOffset, int meshVertexCount)
    {
        foreach (var vertexWeight in bone.VertexWeights)
        {
            var influence = vertexWeight.Weight;
            if ((uint)vertexWeight.VertexID >= (uint)meshVertexCount || !(influence > 0))
                continue;

            ref var weight = ref weights[vertexOffset + vertexWeight.VertexID];

            var weakestSlot = 0;
            var weakestValue = weight.Weights.X;
            for (var slot = 1; slot < 4; slot++)
            {
                var value = weight.Weights[slot];
                if (value < weakestValue)
                {
                    weakestValue = value;
                    weakestSlot = slot;
                }
            }

            if (influence <= weakestValue)
                continue;

            weight.Weights[weakestSlot] = influence;
            switch (weakestSlot)
            {
                case 0: weight.JointIndices.X = jointIndex; break;
                case 1: weight.JointIndices.Y = jointIndex; break;
                case 2: weight.JointIndices.Z = jointIndex; break;
                default: weight.JointIndices.W = jointIndex; break;
            }
        }
    }

    private static void ExtractAnimationClips(AssimpScene scene, Dictionary<string, int> jointIndexByName, AssimpModelData data)
    {
        var ignoredChannelCount = 0;
        var invalidKeyCount = 0;
        for (var animationIndex = 0; animationIndex < scene.AnimationCount; animationIndex++)
        {
            var animation = scene.Animations[animationIndex];
            var ticksPerSecond = double.IsFinite(animation.TicksPerSecond) && animation.TicksPerSecond > 0
                                     ? animation.TicksPerSecond
                                     : DefaultTicksPerSecond;

            var duration = (float)(animation.DurationInTicks / ticksPerSecond);
            if (!float.IsFinite(duration) || duration < 0)
                duration = 0;

            var clip = new SceneSetup.SceneAnimClip
                           {
                               Name = string.IsNullOrEmpty(animation.Name) ? $"Clip {animationIndex}" : animation.Name,
                               Duration = duration,
                           };

            var lastKeyTime = 0f;
            foreach (var channel in animation.NodeAnimationChannels)
            {
                if (channel.NodeName == null || !jointIndexByName.TryGetValue(channel.NodeName, out var jointIndex))
                {
                    ignoredChannelCount++;
                    continue;
                }

                var jointChannel = new SceneSetup.JointAnimChannel
                                       {
                                           SkeletonIndex = 0,
                                           JointIndex = jointIndex,
                                       };

                if (channel.HasPositionKeys)
                {
                    jointChannel.TranslationSampler = CreateVectorSampler(channel.PositionKeys, ticksPerSecond, ref lastKeyTime, ref invalidKeyCount);
                }

                if (channel.HasRotationKeys)
                {
                    jointChannel.RotationSampler = CreateRotationSampler(channel.RotationKeys, ticksPerSecond, ref lastKeyTime, ref invalidKeyCount);
                }

                if (channel.HasScalingKeys)
                {
                    jointChannel.ScaleSampler = CreateVectorSampler(channel.ScalingKeys, ticksPerSecond, ref lastKeyTime, ref invalidKeyCount);
                }

                clip.Channels.Add(jointChannel);
            }

            if (clip.Channels.Count == 0)
                continue;

            if (!(clip.Duration > 0))
                clip.Duration = lastKeyTime;

            data.AnimationClips.Add(clip);
        }

        if (ignoredChannelCount > 0)
            data.Warnings.Add($"Ignored {ignoredChannelCount} animation channels of nodes outside the skeleton");

        if (invalidKeyCount > 0)
            data.Warnings.Add($"Skipped {invalidKeyCount} animation keys with invalid times or values");
    }

    private static AssimpVector3Sampler? CreateVectorSampler(List<Assimp.VectorKey> keys, double ticksPerSecond, ref float lastKeyTime,
                                                             ref int invalidKeyCount)
    {
        if (keys.Count == 0)
            return null;

        var times = new float[keys.Count];
        var values = new Vector3[keys.Count];
        var isStep = new bool[keys.Count];
        var count = 0;
        foreach (var key in keys)
        {
            var time = (float)(key.Time / ticksPerSecond);
            if (!float.IsFinite(time) || !IsFinite(key.Value))
            {
                invalidKeyCount++;
                continue;
            }

            times[count] = time;
            values[count] = key.Value;
            isStep[count] = key.Interpolation == AnimationInterpolation.Step;
            count++;
        }

        if (count == 0)
            return null;

        if (count < times.Length)
        {
            Array.Resize(ref times, count);
            Array.Resize(ref values, count);
            Array.Resize(ref isStep, count);
        }

        SortKeys(times, values, isStep);
        lastKeyTime = MathF.Max(lastKeyTime, times[^1]);
        return new AssimpVector3Sampler(times, values, isStep);
    }

    private static AssimpQuaternionSampler? CreateRotationSampler(List<Assimp.QuaternionKey> keys, double ticksPerSecond, ref float lastKeyTime,
                                                                  ref int invalidKeyCount)
    {
        if (keys.Count == 0)
            return null;

        var times = new float[keys.Count];
        var values = new Quaternion[keys.Count];
        var isStep = new bool[keys.Count];
        var count = 0;
        foreach (var key in keys)
        {
            var time = (float)(key.Time / ticksPerSecond);
            var lengthSquared = key.Value.LengthSquared();
            if (!float.IsFinite(time) || !float.IsFinite(lengthSquared) || lengthSquared < 1e-20f)
            {
                invalidKeyCount++;
                continue;
            }

            times[count] = time;
            values[count] = Quaternion.Normalize(key.Value);
            isStep[count] = key.Interpolation == AnimationInterpolation.Step;
            count++;
        }

        if (count == 0)
            return null;

        if (count < times.Length)
        {
            Array.Resize(ref times, count);
            Array.Resize(ref values, count);
            Array.Resize(ref isStep, count);
        }

        SortKeys(times, values, isStep);
        lastKeyTime = MathF.Max(lastKeyTime, times[^1]);
        return new AssimpQuaternionSampler(times, values, isStep);
    }

    /// <summary>Keys are sorted in practice; this keeps binary search valid for files that aren't.</summary>
    private static void SortKeys<T>(float[] times, T[] values, bool[] isStep)
    {
        var isSorted = true;
        for (var keyIndex = 1; keyIndex < times.Length && isSorted; keyIndex++)
        {
            isSorted = times[keyIndex - 1] <= times[keyIndex];
        }

        if (isSorted)
            return;

        var order = new int[times.Length];
        for (var keyIndex = 0; keyIndex < order.Length; keyIndex++)
        {
            order[keyIndex] = keyIndex;
        }

        var sortKeys = (float[])times.Clone();
        Array.Sort(sortKeys, order);

        var sortedValues = new T[values.Length];
        var sortedIsStep = new bool[isStep.Length];
        for (var keyIndex = 0; keyIndex < order.Length; keyIndex++)
        {
            sortedValues[keyIndex] = values[order[keyIndex]];
            sortedIsStep[keyIndex] = isStep[order[keyIndex]];
        }

        sortKeys.CopyTo(times, 0);
        sortedValues.CopyTo(values, 0);
        sortedIsStep.CopyTo(isStep, 0);
    }
    #endregion

    #region helpers
    private static int CountTriangles(AssimpMesh mesh)
    {
        var count = 0;
        foreach (var face in mesh.Faces)
        {
            if (face.IndexCount == 3)
                count++;
        }

        return count;
    }

    private static List<Vector3>? GetTextureChannel(AssimpMesh mesh, int channel)
    {
        return mesh.HasTextureCoords(channel) && mesh.TextureCoordinateChannels[channel].Count == mesh.VertexCount
                   ? mesh.TextureCoordinateChannels[channel]
                   : null;
    }

    private static Vector3 TransformDirection(Vector3 direction, in Matrix4x4 transform)
    {
        var transformed = Vector3.TransformNormal(direction, transform);
        var lengthSquared = transformed.LengthSquared();
        return lengthSquared > 1e-20f && float.IsFinite(lengthSquared) ? transformed / MathF.Sqrt(lengthSquared) : Vector3.Zero;
    }

    private static Vector2 SanitizeUv(Vector3 uv) => SanitizeUv(new Vector2(uv.X, uv.Y));

    private static Vector2 SanitizeUv(Vector2 uv) => float.IsFinite(uv.X) && float.IsFinite(uv.Y) ? uv : Vector2.Zero;

    private static Vector4 SanitizeColor(Vector4 color)
    {
        return IsFinite(color) ? color : Vector4.One;
    }

    private static Quaternion NormalizeOrIdentity(Quaternion rotation)
    {
        var lengthSquared = rotation.LengthSquared();
        return float.IsFinite(lengthSquared) && lengthSquared > 1e-20f ? Quaternion.Normalize(rotation) : Quaternion.Identity;
    }

    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Vector4 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static bool AreIndicesValid(List<int> indices, int vertexCount)
    {
        foreach (var index in indices)
        {
            if ((uint)index >= (uint)vertexCount)
                return false;
        }

        return true;
    }

    /// <summary>Area-weighted vertex normals for meshes that have none.</summary>
    private static void ComputeVertexNormals(PbrVertex[] vertices, Int3[] triangles, int triangleStart, int triangleEnd)
    {
        for (var triangleIndex = triangleStart; triangleIndex < triangleEnd; triangleIndex++)
        {
            var triangle = triangles[triangleIndex];
            var p0 = vertices[triangle.X].Position;
            var faceNormal = Vector3.Cross(vertices[triangle.Y].Position - p0, vertices[triangle.Z].Position - p0);
            vertices[triangle.X].Normal += faceNormal;
            vertices[triangle.Y].Normal += faceNormal;
            vertices[triangle.Z].Normal += faceNormal;
        }

        for (var triangleIndex = triangleStart; triangleIndex < triangleEnd; triangleIndex++)
        {
            var triangle = triangles[triangleIndex];
            NormalizeVertexNormal(ref vertices[triangle.X]);
            NormalizeVertexNormal(ref vertices[triangle.Y]);
            NormalizeVertexNormal(ref vertices[triangle.Z]);
        }
    }

    private static void NormalizeVertexNormal(ref PbrVertex vertex)
    {
        var lengthSquared = vertex.Normal.LengthSquared();
        if (lengthSquared > 1e-20f && MathF.Abs(lengthSquared - 1) > 1e-6f)
            vertex.Normal /= MathF.Sqrt(lengthSquared);
    }

    /// <summary>Newell's method, robust for N-gons.</summary>
    private static void FillFaceNormal(List<Vector3> positions, List<int> cornerPoints, List<Vector3> cornerNormals, int firstCorner)
    {
        var lastCorner = cornerPoints.Count;
        var normal = Vector3.Zero;
        for (var corner = firstCorner; corner < lastCorner; corner++)
        {
            var next = corner + 1 == lastCorner ? firstCorner : corner + 1;
            var p0 = positions[cornerPoints[corner]];
            var p1 = positions[cornerPoints[next]];
            normal += new Vector3((p0.Y - p1.Y) * (p0.Z + p1.Z),
                                  (p0.Z - p1.Z) * (p0.X + p1.X),
                                  (p0.X - p1.X) * (p0.Y + p1.Y));
        }

        normal = normal.LengthSquared() > 1e-20f ? Vector3.Normalize(normal) : Vector3.UnitY;
        for (var corner = firstCorner; corner < lastCorner; corner++)
        {
            cornerNormals[corner] = normal;
        }
    }

    /// <summary>
    /// Lengyel's per-triangle tangents accumulated per vertex, Gram-Schmidt orthogonalized against
    /// the normal. Only touches vertices whose mesh brought no tangent basis.
    /// </summary>
    private static void CalculateMissingTangents(PbrVertex[] vertices, Int3[] triangles, bool[] needsTangents)
    {
        var anyNeeded = false;
        for (var vertexIndex = 0; vertexIndex < needsTangents.Length && !anyNeeded; vertexIndex++)
        {
            anyNeeded = needsTangents[vertexIndex];
        }

        if (!anyNeeded)
            return;

        var tangents = new Vector3[vertices.Length];
        var bitangents = new Vector3[vertices.Length];

        foreach (var triangle in triangles)
        {
            if (!needsTangents[triangle.X] && !needsTangents[triangle.Y] && !needsTangents[triangle.Z])
                continue;

            ref readonly var v0 = ref vertices[triangle.X];
            ref readonly var v1 = ref vertices[triangle.Y];
            ref readonly var v2 = ref vertices[triangle.Z];

            var edge1 = v1.Position - v0.Position;
            var edge2 = v2.Position - v0.Position;
            var deltaUv1 = v1.Texcoord - v0.Texcoord;
            var deltaUv2 = v2.Texcoord - v0.Texcoord;

            var denominator = deltaUv1.X * deltaUv2.Y - deltaUv2.X * deltaUv1.Y;
            if (MathF.Abs(denominator) < 1e-12f)
                continue;

            var f = 1.0f / denominator;
            var tangent = (edge1 * deltaUv2.Y - edge2 * deltaUv1.Y) * f;
            var bitangent = (edge2 * deltaUv1.X - edge1 * deltaUv2.X) * f;

            tangents[triangle.X] += tangent;
            tangents[triangle.Y] += tangent;
            tangents[triangle.Z] += tangent;
            bitangents[triangle.X] += bitangent;
            bitangents[triangle.Y] += bitangent;
            bitangents[triangle.Z] += bitangent;
        }

        for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
        {
            if (!needsTangents[vertexIndex])
                continue;

            ref var vertex = ref vertices[vertexIndex];
            var normal = vertex.Normal;
            if (normal.LengthSquared() < 1e-12f)
            {
                // Degenerate geometry can leave no usable normal; an orthonormal frame still needs one
                normal = Vector3.UnitY;
                vertex.Normal = normal;
            }

            var tangent = tangents[vertexIndex] - normal * Vector3.Dot(normal, tangents[vertexIndex]);

            if (tangent.LengthSquared() < 1e-12f || !float.IsFinite(tangent.X))
            {
                // No usable UVs: any frame perpendicular to the normal
                var reference = MathF.Abs(normal.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
                tangent = Vector3.Cross(reference, normal);
                if (tangent.LengthSquared() < 1e-12f)
                    tangent = Vector3.UnitX;

                tangent = Vector3.Normalize(tangent);
                vertex.Tangent = tangent;
                vertex.Bitangent = Vector3.Cross(normal, tangent);
                continue;
            }

            tangent = Vector3.Normalize(tangent);
            var handedness = Vector3.Dot(Vector3.Cross(normal, tangent), bitangents[vertexIndex]) < 0 ? -1f : 1f;
            vertex.Tangent = tangent;
            vertex.Bitangent = Vector3.Cross(normal, tangent) * handedness;
        }
    }

    private static void CopyToAttribute<T>(MeshGeometry geometry, string name, List<T> values) where T : unmanaged
    {
        var attribute = geometry.Attributes.GetOrCreate<T>(name, AttributeDomain.Corner, values.Count);
        values.CopyTo(attribute.Values);
    }

    private static SceneSetup.Transform DecomposeTransform(Matrix4x4 matrix, ref bool hasFailure)
    {
        if (Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var translation))
        {
            return new SceneSetup.Transform { Translation = translation, Rotation = NormalizeOrIdentity(rotation), Scale = scale };
        }

        hasFailure = true;
        var axisX = new Vector3(matrix.M11, matrix.M12, matrix.M13);
        var axisY = new Vector3(matrix.M21, matrix.M22, matrix.M23);
        var axisZ = new Vector3(matrix.M31, matrix.M32, matrix.M33);
        var approximatedScale = new Vector3(axisX.Length(), axisY.Length(), axisZ.Length());
        var approximatedRotation = Quaternion.Identity;
        if (approximatedScale.X > 0 && approximatedScale.Y > 0 && approximatedScale.Z > 0)
        {
            var rotationMatrix = new Matrix4x4(axisX.X / approximatedScale.X, axisX.Y / approximatedScale.X, axisX.Z / approximatedScale.X, 0,
                                               axisY.X / approximatedScale.Y, axisY.Y / approximatedScale.Y, axisY.Z / approximatedScale.Y, 0,
                                               axisZ.X / approximatedScale.Z, axisZ.Y / approximatedScale.Z, axisZ.Z / approximatedScale.Z, 0,
                                               0, 0, 0, 1);
            approximatedRotation = NormalizeOrIdentity(Quaternion.CreateFromRotationMatrix(rotationMatrix));
        }

        return new SceneSetup.Transform { Translation = matrix.Translation, Rotation = approximatedRotation, Scale = approximatedScale };
    }

    /// <summary>Rest pose point for visualization; parent index rides in F2 like the glTF skeleton points.</summary>
    private static Point CreateJointPoint(Matrix4x4 objectTransform, int parentIndex)
    {
        var orientation = Matrix4x4.Decompose(objectTransform, out _, out var rotation, out _) ? NormalizeOrIdentity(rotation) : Quaternion.Identity;
        return new Point
                   {
                       Position = objectTransform.Translation,
                       F1 = 1,
                       Orientation = orientation,
                       Color = Vector4.One,
                       Scale = Vector3.One,
                       F2 = parentIndex,
                   };
    }

    private static string BuildMeshNames(AssimpScene scene)
    {
        var builder = new StringBuilder();
        for (var meshIndex = 0; meshIndex < scene.MeshCount; meshIndex++)
        {
            if (meshIndex > 0)
                builder.Append('|');

            var name = scene.Meshes[meshIndex].Name;
            builder.Append(string.IsNullOrEmpty(name) ? $"Mesh{meshIndex}" : name);
        }

        return builder.ToString();
    }

    /// <summary>Names of the extracted clips, in the order clip indices of [SampleGltfAnimation] address them.</summary>
    private static string BuildAnimationNames(List<SceneSetup.SceneAnimClip> clips)
    {
        var builder = new StringBuilder();
        for (var clipIndex = 0; clipIndex < clips.Count; clipIndex++)
        {
            if (clipIndex > 0)
                builder.Append('|');

            builder.Append(clips[clipIndex].Name);
        }

        return builder.ToString();
    }
    #endregion

    private const string ImportRootJointName = "ImportRoot";

    /** Assimp's documented fallback when a file doesn't specify ticks per second. */
    private const double DefaultTicksPerSecond = 25.0;

    private const float MinimumScale = 1e-6f;

    /** Assimp reads and expands the whole file, so a huge input would exhaust memory before any conversion limit applies. */
    private const long MaxInputFileBytes = 2L * 1024 * 1024 * 1024;
}

/// <summary>Assimp's scene before and after triangulation, as parsed from one state of a file.</summary>
internal sealed record AssimpImportedScenes(string AbsolutePath, AssimpPostProcessPresets Preset, AssimpScene PolygonScene, AssimpScene TriangleScene);
