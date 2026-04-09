using Assimp;
using SharpDX;
using SharpDX.Direct3D11;
using T3.Core.Rendering;
using T3.Core.Utils;
using Scene = Assimp.Scene;

namespace Lib.io.assimp;

/// <summary>
///     Export 3D models and point clouds to various formats including OBJ, STL, PLY, and more.
///     Supported formats depend on your Assimp build configuration.
///     Files are automatically named with a counter (MeshName_001.ext, MeshName_002.ext, etc.)
/// </summary>
[Guid("F66C3CD9-B648-4E19-A564-D87AA48D6D88")]
internal sealed class AssimpExport : Instance<AssimpExport>
{
    private const int DefaultExportFormat = 1; // OBJ (most widely supported)

    [Output(Guid = "369771FD-4C3F-4877-B44E-D42B28702282")]
    public readonly Slot<string> StatusMessage = new();

    // Outputs
    [Output(Guid = "7FDE5B8D-B541-43D3-8EC4-67037F47BF31")]
    public readonly Slot<bool> Success = new();

    [Output(Guid = "A1B2C3D4-E5F6-7890-ABCD-EF1234567890")]
    public readonly Slot<string> SupportedFormats = new();

    public AssimpExport()
    {
        Success.UpdateAction += Update;
    }

    #region Update Method
    private void Update(EvaluationContext context)
    {
        // Query supported formats on first update
        QuerySupportedFormatsIfNeeded();

        // Handle counter reset
        if (ResetCounter.GetValue(context))
        {
            _exportCounter = 1;
            ResetCounter.SetTypedInputValue(false);
            SetResult(true, "Export counter reset to 1");
            return;
        }

        // Handle export triggers
        var exportMeshTriggered = MathUtils.WasTriggered(ExportMesh.GetValue(context), ref _exportMeshTriggered);
        var exportPointsTriggered = MathUtils.WasTriggered(ExportPoints.GetValue(context), ref _exportPointsTriggered);

        if (exportMeshTriggered)
        {
            ExportMesh.SetTypedInputValue(false);
            DoExport(context, ExportType.Mesh);
        }
        else if (exportPointsTriggered)
        {
            ExportPoints.SetTypedInputValue(false);
            DoExport(context, ExportType.Points);
        }
        else
        {
            // Maintain last state
            Success.Value = _lastSuccess;
            StatusMessage.Value = _lastStatus;
        }

        // Update supported formats output
        if (_supportedFormats.Count > 0)
            SupportedFormats.Value = string.Join(", ", _supportedFormats.OrderBy(x => x));
    }
    #endregion

    #region Export
    private enum ExportType
    {
        Mesh,
        Points
    }

    private void DoExport(EvaluationContext context, ExportType exportType)
    {
        // Validate folder path
        var folderPath = FolderPath.GetValue(context);
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            SetResult(false, "Export folder path is empty");
            return;
        }

        // Get and sanitize mesh name
        var meshName = SanitizeFilename(MeshName.GetValue(context) ?? "mesh");

        // Get file extension for selected format
        var extension = GetFileExtension(context);
        if (string.IsNullOrEmpty(extension))
        {
            SetResult(false, "Unknown export format");
            return;
        }

        // Generate output path
        var fullPath = BuildOutputPath(folderPath, meshName, extension);
        if (string.IsNullOrEmpty(fullPath))
        {
            SetResult(false, "Failed to build output path");
            return;
        }

        // Ensure output directory exists
        if (!EnsureOutputDirectory(fullPath))
            return;

        // Get input data
        if (!GetInputData(context, exportType, out var meshData, out var pointData))
            return;

        // Perform export
        try
        {
            var scene = BuildScene(meshData, pointData, meshName, context);
            if (scene == null)
                return; // Error already set

            if (ExportScene(scene, fullPath, context))
            {
                _exportCounter++;
                SetResult(true, $"Exported: {Path.GetFileName(fullPath)}");
            }
            else
            {
                SetResult(false, "Export failed (Assimp error)");
            }
        }
        catch (Exception ex)
        {
            SetResult(false, $"Export failed: {ex.Message}");
        }
    }

    private bool GetInputData(EvaluationContext context, ExportType exportType, out MeshBuffers meshData, out BufferWithViews pointData)
    {
        meshData = null;
        pointData = null;

        if (exportType == ExportType.Mesh)
        {
            meshData = MeshData.GetValue(context);
            if (meshData == null)
            {
                SetResult(false, "No mesh data connected");
                return false;
            }
        }
        else // ExportType.Points
        {
            pointData = Points.GetValue(context);
            if (pointData == null)
            {
                SetResult(false, "No point data connected");
                return false;
            }
        }

        return true;
    }

    private string BuildOutputPath(string folderPath, string meshName, string extension)
    {
        try
        {
            var fileName = $"{meshName}_{_exportCounter:D3}{extension}";
            return Path.IsPathRooted(folderPath)
                       ? Path.Combine(folderPath, fileName)
                       : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folderPath, fileName);
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to build output path: {ex.Message}", this);
            return string.Empty;
        }
    }

    private bool EnsureOutputDirectory(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
            return true;

        if (Directory.Exists(directory))
            return true;

        try
        {
            Directory.CreateDirectory(directory);
            Log.Debug($"Created output directory: {directory}", this);
            return true;
        }
        catch (Exception ex)
        {
            SetResult(false, $"Failed to create directory: {ex.Message}");
            return false;
        }
    }

    private Scene BuildScene(MeshBuffers meshData, BufferWithViews pointData, string meshName, EvaluationContext context)
    {
        var scene = new Scene();

        if (meshData != null)
        {
            if (!BuildMeshScene(scene, meshData, meshName, context))
                return null;
        }
        else if (pointData != null)
        {
            if (!BuildPointsScene(scene, pointData, meshName, context))
                return null;
        }
        else
        {
            // Neither mesh nor point data - should not happen if GetInputData validated correctly
            return null;
        }

        return scene;
    }

    private bool BuildMeshScene(Scene scene, MeshBuffers meshData, string meshName, EvaluationContext context)
    {
        var vertices = ReadVertexBuffer(meshData.VertexBuffer);
        var indices = ReadIndexBuffer(meshData.IndicesBuffer);

        if (vertices.Length == 0)
        {
            SetResult(false, "Mesh has no vertices");
            return false;
        }

        if (indices == null || indices.Length == 0)
        {
            SetResult(false, "Mesh has no faces");
            return false;
        }

        // Validate vertex data
        for (var i = 0; i < vertices.Length; i++)
        {
            var v = vertices[i];
            // Check for NaN or infinity in position
            if (float.IsNaN(v.Position.X) || float.IsInfinity(v.Position.X) ||
                float.IsNaN(v.Position.Y) || float.IsInfinity(v.Position.Y) ||
                float.IsNaN(v.Position.Z) || float.IsInfinity(v.Position.Z))
            {
                SetResult(false, $"Vertex {i} has invalid position");
                return false;
            }
        }

        // Validate face indices
        for (var i = 0; i < indices.Length; i++)
        {
            var face = indices[i];
            if (face.X < 0 || face.X >= vertices.Length ||
                face.Y < 0 || face.Y >= vertices.Length ||
                face.Z < 0 || face.Z >= vertices.Length)
            {
                SetResult(false, $"Face {i} has invalid indices");
                return false;
            }
        }

        // Apply vertex limit if set (> 0)
        var maxVertices = MaxVertexCount.GetValue(context);
        if (maxVertices > 0 && vertices.Length > maxVertices)
        {
            var limitedVertices = new PbrVertex[maxVertices];
            Array.Copy(vertices, limitedVertices, maxVertices);
            vertices = limitedVertices;

            // Keep only faces that reference vertices within the limited range
            var limitedIndicesList = new List<Int3>();
            foreach (var face in indices)
                if (face.X < maxVertices && face.Y < maxVertices && face.Z < maxVertices)
                    limitedIndicesList.Add(face);

            indices = limitedIndicesList.ToArray();

            // Check if we still have faces after filtering
            if (indices.Length == 0)
            {
                SetResult(false, "Mesh has no valid faces after vertex limit");
                return false;
            }
        }

        var assimpMesh = new Mesh(meshName, PrimitiveType.Triangle);
        AddMeshVertices(assimpMesh, vertices);
        AddMeshFaces(assimpMesh, indices);
        AddMeshOptionalAttributes(assimpMesh, vertices);

        scene.Meshes.Add(assimpMesh);
        scene.RootNode = new Node("Root");
        scene.RootNode.MeshIndices.Add(0);

        return true;
    }

    private bool BuildPointsScene(Scene scene, BufferWithViews pointData, string meshName, EvaluationContext context)
    {
        var points = ReadPointBuffer(pointData);

        if (points.Length == 0)
        {
            SetResult(false, "Point buffer is empty");
            return false;
        }

        // Apply point limit if set (> 0)
        var maxVertices = MaxVertexCount.GetValue(context);
        if (maxVertices > 0 && points.Length > maxVertices)
        {
            var limitedPoints = new Point[maxVertices];
            Array.Copy(points, limitedPoints, maxVertices);
            points = limitedPoints;
        }

        var assimpMesh = new Mesh(meshName, PrimitiveType.Point);

        for (var i = 0; i < points.Length; i++)
        {
            var p = points[i];
            assimpMesh.Vertices.Add(new Vector3(p.Position.X, p.Position.Y, p.Position.Z));
            assimpMesh.Normals.Add(new Vector3(0, 1, 0));
            assimpMesh.VertexColorChannels[0].Add(new Vector4(p.Color.X, p.Color.Y, p.Color.Z, p.Color.W));
            // Note: Point clouds don't have faces - vertices are the primitives
        }

        scene.Meshes.Add(assimpMesh);
        scene.RootNode = new Node("Root");
        scene.RootNode.MeshIndices.Add(0);

        return true;
    }

    private static bool _formatsLogged;
    private static readonly HashSet<string> _supportedFormats = new();

    private bool ExportScene(Scene scene, string fullPath, EvaluationContext context)
    {
        var formatId = GetExportFormatId(context);
        if (string.IsNullOrEmpty(formatId))
        {
            SetResult(false, "Invalid export format ID");
            return false;
        }

        // Validate scene before export
        if (scene.Meshes.Count == 0)
        {
            SetResult(false, "Scene has no meshes");
            return false;
        }

        var mesh = scene.Meshes[0];
        if (mesh.VertexCount == 0)
        {
            SetResult(false, "Mesh has no vertices");
            return false;
        }

        // Validate scene integrity
        if (!ValidateSceneForExport(mesh))
            return false;

        try
        {
            using var exporter = new AssimpContext();

            // Check if format is supported
            if (!_supportedFormats.Contains(formatId))
            {
                var allFormats = string.Join(", ", _supportedFormats.OrderBy(x => x));
                SetResult(false, $"Format '{formatId}' is not supported. Available: {allFormats}");
                return false;
            }

            ApplyExportOptions(exporter, formatId, context);

            // Add additional pre-export validation
            if (mesh.FaceCount == 0 && mesh.PrimitiveType != PrimitiveType.Point)
            {
                SetResult(false, "Mesh has no faces");
                return false;
            }

            var result = exporter.ExportFile(scene, fullPath, formatId);
            if (!result)
                SetResult(false, $"Assimp export failed for format '{formatId}'");
            return result;
        }
        catch (AccessViolationException)
        {
            SetResult(false, $"Native crash exporting to '{formatId}' - format may be unstable");
            Log.Error($"Assimp native access violation for format '{formatId}'", this);
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning($"Assimp export error for '{formatId}': {ex.Message}", this);
            SetResult(false, $"Export failed: {ex.Message}");
            return false;
        }
    }

    private static void QuerySupportedFormatsIfNeeded()
    {
        if (_formatsLogged)
            return;

        try
        {
            using var exporter = new AssimpContext();
            var formats = exporter.GetSupportedExportFormats();
            _supportedFormats.Clear();
            foreach (var format in formats)
                _supportedFormats.Add(format.FormatId.ToLowerInvariant());
            Log.Debug($"Assimp supported export formats: {string.Join(", ", _supportedFormats.OrderBy(x => x))}", typeof(AssimpExport));
            _formatsLogged = true;
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to query supported export formats: {ex.Message}", typeof(AssimpExport));
        }
    }

    private bool ValidateSceneForExport(Mesh mesh)
    {
        // Check for invalid vertex data that could cause native crashes
        for (var i = 0; i < mesh.VertexCount; i++)
        {
            var v = mesh.Vertices[i];
            if (float.IsNaN(v.X) || float.IsInfinity(v.X) ||
                float.IsNaN(v.Y) || float.IsInfinity(v.Y) ||
                float.IsNaN(v.Z) || float.IsInfinity(v.Z))
            {
                SetResult(false, $"Invalid vertex data at index {i}");
                return false;
            }
        }

        // Check face indices are valid
        if (mesh.HasFaces && mesh.FaceCount > 0)
            for (var i = 0; i < mesh.FaceCount; i++)
            {
                var face = mesh.Faces[i];
                foreach (var index in face.Indices)
                    if (index < 0 || index >= mesh.VertexCount)
                    {
                        SetResult(false, $"Invalid face index {index} at face {i}");
                        return false;
                    }
            }

        return true;
    }
    #endregion

    #region Mesh Data Helpers
    private void AddMeshVertices(Mesh assimpMesh, PbrVertex[] vertices)
    {
        for (var i = 0; i < vertices.Length; i++)
        {
            var v = vertices[i];
            assimpMesh.Vertices.Add(new Vector3(v.Position.X, v.Position.Y, v.Position.Z));
            assimpMesh.Normals.Add(new Vector3(v.Normal.X, v.Normal.Y, v.Normal.Z));
            assimpMesh.TextureCoordinateChannels[0].Add(new Vector3(v.Texcoord.X, v.Texcoord.Y, 0));
        }
    }

    private void AddMeshFaces(Mesh assimpMesh, Int3[] indices)
    {
        for (var i = 0; i < indices.Length; i++)
        {
            var idx = indices[i];
            assimpMesh.Faces.Add(new Face(new[] { idx.X, idx.Y, idx.Z }));
        }
    }

    private void AddMeshOptionalAttributes(Mesh assimpMesh, PbrVertex[] vertices)
    {
        var hasAnyTangents = false;
        var hasAnyBitangents = false;
        var hasAnyColors = false;

        // Check which attributes are present
        foreach (var v in vertices)
        {
            if (v.Tangent.LengthSquared() > 0.0001f)
                hasAnyTangents = true;
            if (v.Bitangent.LengthSquared() > 0.0001f)
                hasAnyBitangents = true;
            if (HasVertexColor(v))
                hasAnyColors = true;
        }

        // Add attributes only if at least one vertex has them
        foreach (var v in vertices)
        {
            if (hasAnyTangents)
            {
                if (v.Tangent.LengthSquared() > 0.0001f)
                    assimpMesh.Tangents.Add(new Vector3(v.Tangent.X, v.Tangent.Y, v.Tangent.Z));
                else
                    assimpMesh.Tangents.Add(new Vector3(1, 0, 0));
            }

            if (hasAnyBitangents)
            {
                if (v.Bitangent.LengthSquared() > 0.0001f)
                    assimpMesh.BiTangents.Add(new Vector3(v.Bitangent.X, v.Bitangent.Y, v.Bitangent.Z));
                else
                    assimpMesh.BiTangents.Add(new Vector3(0, 1, 0));
            }

            if (hasAnyColors)
                // Vertex colors: Selection (R) and Texcoord2.Y (A)
                assimpMesh.VertexColorChannels[0].Add(new Vector4(
                                                                  v.Selection, v.Selection, v.Selection, v.Texcoord2.Y));
        }
    }

    private static bool HasVertexColor(PbrVertex v)
    {
        return Math.Abs(v.Selection - 1.0f) > 0.001f || Math.Abs(v.Texcoord2.Y - 1.0f) > 0.001f;
    }
    #endregion

    #region Buffer Reading
    private PbrVertex[] ReadVertexBuffer(BufferWithViews bufferWithViews)
    {
        if (bufferWithViews?.Buffer == null)
            return Array.Empty<PbrVertex>();

        var device = ResourceManager.Device;
        var d3dContext = device.ImmediateContext;
        var desc = bufferWithViews.Buffer.Description;
        var vertexCount = desc.SizeInBytes / PbrVertex.Stride;

        var stagingDesc = new BufferDescription
                              {
                                  SizeInBytes = desc.SizeInBytes,
                                  Usage = ResourceUsage.Staging,
                                  BindFlags = BindFlags.None,
                                  CpuAccessFlags = CpuAccessFlags.Read,
                                  OptionFlags = ResourceOptionFlags.BufferStructured,
                                  StructureByteStride = desc.StructureByteStride
                              };

        using var staging = new Buffer(device, stagingDesc);
        d3dContext.CopyResource(bufferWithViews.Buffer, staging);

        var dataBox = d3dContext.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
        try
        {
            using var stream = new DataStream(dataBox.DataPointer, dataBox.RowPitch, true, false);
            return stream.ReadRange<PbrVertex>(vertexCount).ToArray();
        }
        finally
        {
            if (dataBox.DataPointer != IntPtr.Zero)
                d3dContext.UnmapSubresource(staging, 0);
        }
    }

    private Int3[] ReadIndexBuffer(BufferWithViews bufferWithViews)
    {
        if (bufferWithViews?.Buffer == null)
            return Array.Empty<Int3>();

        var device = ResourceManager.Device;
        var d3dContext = device.ImmediateContext;
        var desc = bufferWithViews.Buffer.Description;
        var indexCount = desc.SizeInBytes / (3 * 4);

        var stagingDesc = new BufferDescription
                              {
                                  SizeInBytes = desc.SizeInBytes,
                                  Usage = ResourceUsage.Staging,
                                  BindFlags = BindFlags.None,
                                  CpuAccessFlags = CpuAccessFlags.Read,
                                  OptionFlags = ResourceOptionFlags.BufferStructured,
                                  StructureByteStride = desc.StructureByteStride
                              };

        using var staging = new Buffer(device, stagingDesc);
        d3dContext.CopyResource(bufferWithViews.Buffer, staging);

        var dataBox = d3dContext.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
        try
        {
            using var stream = new DataStream(dataBox.DataPointer, dataBox.RowPitch, true, false);
            return stream.ReadRange<Int3>(indexCount).ToArray();
        }
        finally
        {
            if (dataBox.DataPointer != IntPtr.Zero)
                d3dContext.UnmapSubresource(staging, 0);
        }
    }

    private Point[] ReadPointBuffer(BufferWithViews bufferWithViews)
    {
        if (bufferWithViews?.Buffer == null)
            return Array.Empty<Point>();

        var device = ResourceManager.Device;
        var d3dContext = device.ImmediateContext;
        var desc = bufferWithViews.Buffer.Description;
        var pointCount = desc.SizeInBytes / Point.Stride;

        var stagingDesc = new BufferDescription
                              {
                                  SizeInBytes = desc.SizeInBytes,
                                  Usage = ResourceUsage.Staging,
                                  BindFlags = BindFlags.None,
                                  CpuAccessFlags = CpuAccessFlags.Read,
                                  OptionFlags = ResourceOptionFlags.BufferStructured,
                                  StructureByteStride = desc.StructureByteStride
                              };

        using var staging = new Buffer(device, stagingDesc);
        d3dContext.CopyResource(bufferWithViews.Buffer, staging);

        var dataBox = d3dContext.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
        try
        {
            using var stream = new DataStream(dataBox.DataPointer, dataBox.RowPitch, true, false);
            return stream.ReadRange<Point>(pointCount).ToArray();
        }
        finally
        {
            if (dataBox.DataPointer != IntPtr.Zero)
                d3dContext.UnmapSubresource(staging, 0);
        }
    }
    #endregion

    #region Format Helpers
    private enum ExportFormatPreset
    {
        Auto, // Falls back to OBJ if unsupported
        Obj, // OBJ - Widely supported (mesh only)
        Fbx, // FBX - May not be available in all builds
        Gltf, // glTF 2.0 - Requires ASSIMP_BUILD_EXPORT_GLTF2
        Glb, // GLB 2.0 - Requires ASSIMP_BUILD_EXPORT_GLTF2
        Stl, // STL - Usually available (mesh only)
        Ply, // PLY - Usually available
        Collada, // Collada (DAE) - Requires ASSIMP_BUILD_EXPORT_COLLADA
        ThreeDs, // 3DS - Legacy format, limited support
        Custom // Use custom format ID
    }

    private enum GltfExportOptions
    {
        Default,
        PrettyJson,
        NoBuffersEmbedded
    }

    private string GetFileExtension(EvaluationContext context)
    {
        var format = (ExportFormatPreset)ExportFormat.GetValue(context);

        if (format == ExportFormatPreset.Custom)
        {
            var customFormat = CustomFormat.GetValue(context);
            return GetExtensionFromFormatId(customFormat);
        }

        return format switch
                   {
                       ExportFormatPreset.Obj     => ".obj",
                       ExportFormatPreset.Fbx     => ".fbx",
                       ExportFormatPreset.Gltf    => ".gltf",
                       ExportFormatPreset.Glb     => ".glb",
                       ExportFormatPreset.Stl     => ".stl",
                       ExportFormatPreset.Ply     => ".ply",
                       ExportFormatPreset.Collada => ".dae",
                       ExportFormatPreset.ThreeDs => ".3ds",
                       _                          => ".obj" // Auto defaults to OBJ (most widely supported)
                   };
    }

    private string GetExtensionFromFormatId(string formatId)
    {
        if (string.IsNullOrEmpty(formatId))
            return ".fbx";

        return formatId.ToLowerInvariant() switch
                   {
                       "obj"     => ".obj",
                       "fbx"     => ".fbx",
                       "gltf2"   => ".gltf",
                       "glb2"    => ".glb",
                       "stl"     => ".stl",
                       "ply"     => ".ply",
                       "collada" => ".dae",
                       "3ds"     => ".3ds",
                       _         => ".fbx"
                   };
    }

    private string GetExportFormatId(EvaluationContext context)
    {
        var format = (ExportFormatPreset)ExportFormat.GetValue(context);

        if (format == ExportFormatPreset.Custom)
            return CustomFormat.GetValue(context);

        return format switch
                   {
                       ExportFormatPreset.Obj     => "obj",
                       ExportFormatPreset.Fbx     => "fbx",
                       ExportFormatPreset.Gltf    => "gltf2",
                       ExportFormatPreset.Glb     => "glb2",
                       ExportFormatPreset.Stl     => "stl",
                       ExportFormatPreset.Ply     => "ply",
                       ExportFormatPreset.Collada => "collada",
                       ExportFormatPreset.ThreeDs => "3ds",
                       _                          => "obj" // Auto defaults to OBJ (most widely supported)
                   };
    }

    private void ApplyExportOptions(AssimpContext exporter, string formatId, EvaluationContext context)
    {
        // Note: AssimpNetter doesn't expose format-specific export options directly
        // The GltfOptions enum is kept for future compatibility
    }
    #endregion

    #region Utility Methods
    /// <summary>
    ///     Sanitizes a filename by removing invalid characters and trimming whitespace.
    /// </summary>
    private static string SanitizeFilename(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return "mesh";

        // Remove invalid filename characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", filename.Split(invalidChars));

        // Trim whitespace and limit length
        sanitized = sanitized.Trim();
        if (sanitized.Length > 100)
            sanitized = sanitized.Substring(0, 100);

        return string.IsNullOrWhiteSpace(sanitized) ? "mesh" : sanitized;
    }

    private void SetResult(bool success, string message)
    {
        _lastSuccess = success;
        _lastStatus = message;
        Success.Value = success;
        StatusMessage.Value = message;
        Log.Debug($"AssimpExport: {message}", this);
    }
    #endregion

    #region Fields and Inputs
    private bool _exportMeshTriggered;
    private bool _exportPointsTriggered;
    private int _exportCounter = 1;
    private bool _lastSuccess;
    private string _lastStatus = "Ready";

    [Input(Guid = "47F14471-1D6E-4BAD-A3CC-F372DDA4872C")]
    public readonly InputSlot<MeshBuffers> MeshData = new();

    [Input(Guid = "B0E70377-685C-44B1-8069-2F0C2A9DCBB2")]
    public readonly InputSlot<BufferWithViews> Points = new();

    [Input(Guid = "F784FE22-BA03-499C-BB4E-FE2DED8BF754")]
    public readonly InputSlot<string> FolderPath = new("exports");

    [Input(Guid = "20960269-69A9-4FF1-A3E7-395883521B06")]
    public readonly InputSlot<bool> ExportMesh = new();

    [Input(Guid = "5E6F7A8B-9C1D-2E3F-4A5B-6C7D8E9F0A1B")]
    public readonly InputSlot<bool> ExportPoints = new();

    [Input(Guid = "E8C4F2A7-9B3D-4E1F-A5C6-7D8E9F0A1B2C", MappedType = typeof(ExportFormatPreset))]
    public readonly InputSlot<int> ExportFormat = new(DefaultExportFormat);

    [Input(Guid = "F3D5E8B2-7A4C-4F9E-B2D1-6E7F8A9C0B1D")]
    public readonly InputSlot<string> CustomFormat = new();

    [Input(Guid = "1A2B3C4D-5E6F-7A8B-9C0D-1E2F3A4B5C6D", MappedType = typeof(GltfExportOptions))]
    public readonly InputSlot<int> GltfOptions = new((int)GltfExportOptions.Default);

    [Input(Guid = "2B3C4D5E-6F7A-8B9C-1D2E-3F4A5B6C7D8E")]
    public readonly InputSlot<string> MeshName = new("mesh");

    [Input(Guid = "3C4D5E6F-7A8B-9C1D-2E3F-4A5B6C7D8E9F")]
    public readonly InputSlot<bool> ResetCounter = new();

    [Input(Guid = "4D5E6F7A-8B9C-1D2E-3F4A-6C7D8E9F0A1B")]
    public readonly InputSlot<int> MaxVertexCount = new(0);
    #endregion
}