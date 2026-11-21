#nullable enable
using T3.Core.Rendering;
using AssimpContext = Assimp.AssimpContext;
using AssimpFace = Assimp.Face;
using AssimpMaterial = Assimp.Material;
using AssimpMesh = Assimp.Mesh;
using AssimpNode = Assimp.Node;
using AssimpScene = Assimp.Scene;
using PostProcessSteps = Assimp.PostProcessSteps;
using PrimitiveType = Assimp.PrimitiveType;

namespace Lib.Utils.AssimpIo;

/// <summary>
/// Writes mesh, polygon geometry or point data to a model file through Assimp. Works on CPU
/// snapshots only, so it can run on a worker thread while the graph keeps evaluating.
/// </summary>
internal static class AssimpModelExporter
{
    /// <summary>Format id, file extension (with dot) and description of every exporter of the native library.</summary>
    public static IReadOnlyList<(string FormatId, string Extension, string Description)> GetExportFormats()
    {
        if (!AssimpNative.TryEnsureLoaded(out var nativeError))
            throw new InvalidOperationException(nativeError);

        lock (AssimpNative.Lock)
        {
            using var context = new AssimpContext();
            var formats = new List<(string, string, string)>();
            foreach (var format in context.GetSupportedExportFormats())
            {
                formats.Add((format.FormatId, "." + format.FileExtension.TrimStart('.'), format.Description));
            }

            return formats;
        }
    }

    /// <summary>
    /// Builds the scene and writes it. Throws with a user-readable message on failure and removes a
    /// partially written file, so a failed export never looks like a successful one.
    /// </summary>
    /// <param name="data">A snapshot owned by the worker: exactly one of geometry, mesh or points is set.</param>
    public static void Export(AssimpModelData data, string absolutePath, string formatId, string meshName)
    {
        try
        {
            ExportCore(data, absolutePath, formatId, meshName);
        }
        catch
        {
            // The caller reserves a name that doesn't exist yet, so anything at this path is ours
            TryDelete(absolutePath);
            throw;
        }
    }

    private static void ExportCore(AssimpModelData data, string absolutePath, string formatId, string meshName)
    {
        if (!AssimpNative.TryEnsureLoaded(out var nativeError))
            throw new InvalidOperationException(nativeError);

        var scene = new AssimpScene { RootNode = new AssimpNode("Root") };

        // Several native exporters (FBX, Collada, 3DS, X3D) crash on scenes without a material
        scene.Materials.Add(new AssimpMaterial { Name = "Default" });

        if (data.Geometry != null)
        {
            AddGeometry(scene, data.Geometry, meshName);
        }
        else if (data.Vertices.Length > 0 && data.Triangles.Length > 0)
        {
            AddMesh(scene, scene.RootNode, data.Vertices, data.Triangles, meshName);
        }
        else if (data.Points.Length > 0)
        {
            if (!CanStorePoints(formatId))
                throw new NotSupportedException($"Format '{formatId}' can't store point clouds. Use obj, ply, gltf2, glb2, fbx or x3d.");

            AddPoints(scene, data.Points, meshName);
        }

        if (scene.MeshCount == 0)
            throw new InvalidDataException("Nothing to export");

        var directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var exportSteps = KeepsPolygons(formatId) ? PostProcessSteps.None : PostProcessSteps.Triangulate;
        lock (AssimpNative.Lock)
        {
            using var context = new AssimpContext();
            if (!context.ExportFile(scene, absolutePath, formatId, exportSteps))
                throw new IOException($"Assimp could not write '{formatId}' to {absolutePath}");
        }
    }

    private static void AddMesh(AssimpScene scene, AssimpNode parent, PbrVertex[] vertices, Int3[] triangles, string meshName)
    {
        var mesh = new AssimpMesh(meshName, PrimitiveType.Triangle) { MaterialIndex = 0 };

        var hasColors = false;
        var hasSecondUvs = false;
        foreach (var vertex in vertices)
        {
            hasColors |= vertex.ColorRgb != Vector3.One;
            hasSecondUvs |= vertex.Texcoord2 != vertex.Texcoord;
        }

        for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
        {
            ref readonly var vertex = ref vertices[vertexIndex];
            mesh.Vertices.Add(RequireFinite(vertex.Position, vertexIndex));
            mesh.Normals.Add(SanitizeDirection(vertex.Normal, Vector3.UnitY));
            mesh.Tangents.Add(SanitizeDirection(vertex.Tangent, Vector3.UnitX));
            mesh.BiTangents.Add(SanitizeDirection(vertex.Bitangent, Vector3.UnitZ));
            mesh.TextureCoordinateChannels[0].Add(new Vector3(SanitizeUv(vertex.Texcoord), 0));
            if (hasSecondUvs)
                mesh.TextureCoordinateChannels[1].Add(new Vector3(SanitizeUv(vertex.Texcoord2), 0));

            if (hasColors)
                mesh.VertexColorChannels[0].Add(new Vector4(vertex.ColorRgb, 1));
        }

        mesh.UVComponentCount[0] = 2;
        if (hasSecondUvs)
            mesh.UVComponentCount[1] = 2;

        var vertexCount = vertices.Length;
        foreach (var triangle in triangles)
        {
            if ((uint)triangle.X >= (uint)vertexCount || (uint)triangle.Y >= (uint)vertexCount || (uint)triangle.Z >= (uint)vertexCount)
                throw new InvalidDataException($"Triangle ({triangle.X}, {triangle.Y}, {triangle.Z}) references a vertex outside the {vertexCount} vertices");

            mesh.Faces.Add(new AssimpFace([triangle.X, triangle.Y, triangle.Z]));
        }

        if (mesh.FaceCount == 0)
            throw new InvalidDataException("Mesh has no faces");

        AttachMesh(scene, parent, mesh);
    }

    /// <summary>
    /// One Assimp mesh per geometry part, so DCC tools see the parts as separate objects. Corners
    /// with identical point and attributes share a vertex.
    /// </summary>
    private static void AddGeometry(AssimpScene scene, MeshGeometry geometry, string meshName)
    {
        var faceCount = geometry.FaceCornerOffsets.Length - 1;
        var parts = geometry.Parts.Length > 0
                        ? geometry.Parts
                        : [new GeometryPart(0, faceCount, Vector3.Zero, 0, 0)];

        var cornerCount = geometry.CornerPointIndices.Length;
        var normals = GetCornerValues<Vector3>(geometry, GeometryAttributeNames.Normal);
        var uvs = GetCornerValues<Vector2>(geometry, GeometryAttributeNames.TexCoord);
        var colors = GetCornerValues<Vector4>(geometry, GeometryAttributeNames.Color);
        if (normals != null && normals.Length != cornerCount
            || uvs != null && uvs.Length != cornerCount
            || colors != null && colors.Length != cornerCount)
        {
            throw new InvalidDataException("Geometry attributes don't match its corner count");
        }

        var vertexForCorner = new Dictionary<(int Point, Vector3 Normal, Vector2 Uv, Vector4 Color), int>();
        var faceIndices = new List<int>(8);

        for (var partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            var part = parts[partIndex];
            var faceStart = Math.Clamp(part.FaceStart, 0, faceCount);
            var faceEnd = Math.Clamp(part.FaceStart + part.FaceCount, faceStart, faceCount);

            var name = parts.Length > 1 ? $"{meshName}_{partIndex:D3}" : meshName;
            var mesh = new AssimpMesh(name) { MaterialIndex = 0 };
            vertexForCorner.Clear();
            var primitiveType = (PrimitiveType)0;

            for (var faceIndex = faceStart; faceIndex < faceEnd; faceIndex++)
            {
                var cornerStart = geometry.FaceCornerOffsets[faceIndex];
                var cornerEnd = geometry.FaceCornerOffsets[faceIndex + 1];
                if (cornerEnd - cornerStart < 3 || cornerStart < 0 || cornerEnd > cornerCount)
                    continue;

                faceIndices.Clear();
                for (var corner = cornerStart; corner < cornerEnd; corner++)
                {
                    var pointIndex = geometry.CornerPointIndices[corner];
                    if ((uint)pointIndex >= (uint)geometry.Positions.Length)
                        throw new InvalidDataException($"Corner {corner} references point {pointIndex} of {geometry.Positions.Length}");

                    var key = (pointIndex,
                               normals?[corner] ?? Vector3.Zero,
                               uvs?[corner] ?? Vector2.Zero,
                               colors?[corner] ?? Vector4.One);

                    if (!vertexForCorner.TryGetValue(key, out var vertexIndex))
                    {
                        vertexIndex = mesh.VertexCount;
                        vertexForCorner.Add(key, vertexIndex);
                        mesh.Vertices.Add(RequireFinite(geometry.Positions[pointIndex], pointIndex));
                        if (normals != null)
                            mesh.Normals.Add(SanitizeDirection(key.Item2, Vector3.UnitY));

                        if (uvs != null)
                            mesh.TextureCoordinateChannels[0].Add(new Vector3(SanitizeUv(key.Item3), 0));

                        if (colors != null)
                            mesh.VertexColorChannels[0].Add(key.Item4);
                    }

                    faceIndices.Add(vertexIndex);
                }

                primitiveType |= faceIndices.Count == 3 ? PrimitiveType.Triangle : PrimitiveType.Polygon;
                mesh.Faces.Add(new AssimpFace(faceIndices.ToArray()));
            }

            if (mesh.FaceCount == 0)
                continue;

            if (uvs != null)
                mesh.UVComponentCount[0] = 2;

            mesh.PrimitiveType = primitiveType;
            AttachMesh(scene, scene.RootNode, mesh);
        }

        if (scene.MeshCount == 0)
            throw new InvalidDataException("Geometry has no faces");
    }

    private static T[]? GetCornerValues<T>(MeshGeometry geometry, string name) where T : unmanaged
    {
        return geometry.Attributes.TryGet<T>(name, AttributeDomain.Corner, out var attribute) ? attribute.Values : null;
    }

    private static void AddPoints(AssimpScene scene, Point[] points, string meshName)
    {
        var mesh = new AssimpMesh(meshName, PrimitiveType.Point) { MaterialIndex = 0 };
        foreach (var point in points)
        {
            // Separators and points that collapsed to NaN carry no position worth writing
            if (Point.IsSeparator(point) || !IsFinite(point.Position))
                continue;

            mesh.Faces.Add(new AssimpFace([mesh.VertexCount]));
            mesh.Vertices.Add(point.Position);
            mesh.Normals.Add(Vector3.Transform(Vector3.UnitZ, SanitizeRotation(point.Orientation)));
            mesh.VertexColorChannels[0].Add(IsFinite(point.Color) ? point.Color : Vector4.One);
        }

        if (mesh.VertexCount == 0)
            throw new InvalidDataException("No valid points to export");

        AttachMesh(scene, scene.RootNode, mesh);
    }

    /// <summary>Meshes always go on child nodes: the 3MF exporter silently drops meshes of the root node.</summary>
    private static void AttachMesh(AssimpScene scene, AssimpNode parent, AssimpMesh mesh)
    {
        var node = new AssimpNode(mesh.Name, parent);
        parent.Children.Add(node);
        node.MeshIndices.Add(scene.MeshCount);
        scene.Meshes.Add(mesh);
    }

    /// <summary>These exporters write N-gons (or point and polygon mixes) natively; all others get triangles.</summary>
    private static bool KeepsPolygons(string formatId)
    {
        switch (formatId.ToLowerInvariant())
        {
            case "obj":
            case "objnomtl":
            case "fbx":
            case "fbxa":
            case "collada":
            case "ply":
            case "plyb":
            case "x":
            case "assbin":
            case "assxml":
            case "assjson":
                return true;
            default:
                return false;
        }
    }

    private static bool CanStorePoints(string formatId)
    {
        switch (formatId.ToLowerInvariant())
        {
            case "obj":
            case "objnomtl":
            case "ply":
            case "plyb":
            case "gltf2":
            case "glb2":
            case "gltf":
            case "glb":
            case "fbx":
            case "fbxa":
            case "x3d":
            case "assbin":
            case "assxml":
            case "assjson":
                return true;
            default:
                return false;
        }
    }

    private static Vector3 RequireFinite(Vector3 position, int index)
    {
        if (!IsFinite(position))
            throw new InvalidDataException($"Vertex {index} has an invalid position {position}");

        return position;
    }

    private static Vector3 SanitizeDirection(Vector3 direction, Vector3 fallback)
    {
        return IsFinite(direction) && direction.LengthSquared() > 1e-12f ? direction : fallback;
    }

    private static Vector2 SanitizeUv(Vector2 uv)
    {
        return float.IsFinite(uv.X) && float.IsFinite(uv.Y) ? uv : Vector2.Zero;
    }

    private static Quaternion SanitizeRotation(Quaternion rotation)
    {
        var lengthSquared = rotation.LengthSquared();
        return float.IsFinite(lengthSquared) && lengthSquared > 1e-12f ? Quaternion.Normalize(rotation) : Quaternion.Identity;
    }

    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Vector4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // A locked or already gone file can't be cleaned up; the export failure is what matters
        }
    }
}
