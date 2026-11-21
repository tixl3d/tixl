#nullable enable
using System.Threading;
using Lib.Utils.AssimpIo;
using T3.Graphics.Compat;
using T3.Core.Rendering;
using T3.Core.Resource.Assets;
using T3.Core.Utils;

namespace Lib.io.file;

/// <summary>
/// Writes a mesh, a polygon geometry or a point cloud to a 3D file through Assimp.
/// </summary>
/// <remarks>
/// The GPU readback happens on the render thread when an export is triggered (like [WriteObj]);
/// building the Assimp scene and writing the file run on a worker thread, in a separate process when
/// possible so a native crash can't take the editor down. Files are numbered
/// MeshName_001.ext, MeshName_002.ext, ... and existing files are never overwritten.
/// </remarks>
[Guid("F66C3CD9-B648-4E19-A564-D87AA48D6D88")]
internal sealed class AssimpExport : Instance<AssimpExport>, IStatusProvider
{
    [Output(Guid = "7FDE5B8D-B541-43D3-8EC4-67037F47BF31")]
    public readonly Slot<bool> Success = new();

    [Output(Guid = "369771FD-4C3F-4877-B44E-D42B28702282")]
    public readonly Slot<string> StatusMessage = new();

    [Output(Guid = "6C0E8B53-F2A7-4D19-9B64-AE31D85C07F2")]
    public readonly Slot<string> LastFilePath = new();

    [Output(Guid = "A1B2C3D4-E5F6-7890-ABCD-EF1234567890")]
    public readonly Slot<string> SupportedFormats = new();

    public AssimpExport()
    {
        Success.UpdateAction += Update;
        StatusMessage.UpdateAction += Update;
        LastFilePath.UpdateAction += Update;
        SupportedFormats.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        try
        {
            UpdateExport(context);
        }
        catch (Exception e)
        {
            SetResult(false, $"Unexpected export failure: {e.Message}");
            Log.Error(_statusMessage, this);
        }

        Success.Value = _lastSuccess;
        StatusMessage.Value = _statusMessage;
        LastFilePath.Value = _lastFilePath;
        SupportedFormats.Value = _supportedFormatsText;

        var trigger = _pendingExport != null || _formatsQuery is { IsCompleted: false }
                          ? DirtyFlagTrigger.Animated
                          : DirtyFlagTrigger.None;
        Success.DirtyFlag.Trigger = trigger;
        StatusMessage.DirtyFlag.Trigger = trigger;
        LastFilePath.DirtyFlag.Trigger = trigger;
        SupportedFormats.DirtyFlag.Trigger = trigger;
    }

    private void UpdateExport(EvaluationContext context)
    {
        UpdateSupportedFormats();
        TryCompleteExport();

        if (MathUtils.WasTriggered(ResetCounter.GetValue(context), ref _resetCounterTriggered))
        {
            ResetCounter.SetTypedInputValue(false);
            _resetCounterTriggered = false;
            _exportCounter = 1;
            SetResult(true, "Export counter reset to 1");
        }

        var exportMesh = MathUtils.WasTriggered(ExportMesh.GetValue(context), ref _exportMeshTriggered);
        var exportPoints = MathUtils.WasTriggered(ExportPoints.GetValue(context), ref _exportPointsTriggered);

        // Read the data inputs every frame so the graph above stays evaluated like for any other consumer
        var mesh = MeshData.GetValue(context);
        var geometry = Geometry.GetValue(context);
        var points = Points.GetValue(context);
        var folder = FolderPath.GetValue(context) ?? string.Empty;
        var meshName = MeshName.GetValue(context) ?? string.Empty;
        var formatPreset = ExportFormat.GetEnumValue<ExportFormats>(context);
        var customFormat = CustomFormat.GetValue(context) ?? string.Empty;

        if (exportMesh)
        {
            ExportMesh.SetTypedInputValue(false);
            _exportMeshTriggered = false;
        }

        if (exportPoints)
        {
            ExportPoints.SetTypedInputValue(false);
            _exportPointsTriggered = false;
        }

        if (!exportMesh && !exportPoints)
            return;

        if (_pendingExport != null)
        {
            SetResult(false, "Previous export is still running");
            return;
        }

        if (!TryResolveFormat(formatPreset, customFormat, meshName, out var formatId, out var extension, out var formatError))
        {
            SetResult(false, formatError);
            return;
        }

        if (!TryCreateSource(exportMesh, mesh, geometry, points, out var source, out var sourceError))
        {
            SetResult(false, sourceError);
            return;
        }

        var baseName = SanitizeFileName(meshName);
        if (!TryReserveFilePath(folder, baseName, extension, out var absolutePath, out var pathError))
        {
            SetResult(false, pathError);
            return;
        }

        StartExport(source, absolutePath, formatId, baseName, IsolateExport.GetValue(context));
    }

    #region export job
    private void StartExport(AssimpModelData source, string absolutePath, string formatId, string meshName, bool isolate)
    {
        var request = new ExportRequest(absolutePath);
        _pendingExport = request;
        SetResult(false, $"Exporting {Path.GetFileName(absolutePath)}...");

        Task.Run(() =>
                 {
                     try
                     {
                         // Probed on the worker: the first probe can start a process and take a moment
                         request.WasIsolated = isolate && AssimpIsolatedImport.IsAvailable;
                         if (request.WasIsolated)
                         {
                             AssimpIsolatedImport.Export(source, absolutePath, formatId, meshName, CancellationToken.None);
                         }
                         else
                         {
                             AssimpModelExporter.Export(source, absolutePath, formatId, meshName);
                         }
                     }
                     catch (Exception e)
                     {
                         request.ErrorMessage = e.Message;
                     }
                     finally
                     {
                         Volatile.Write(ref request.IsCompleted, true);
                     }
                 });
    }

    private void TryCompleteExport()
    {
        var request = _pendingExport;
        if (request == null || !Volatile.Read(ref request.IsCompleted))
            return;

        _pendingExport = null;
        if (request.ErrorMessage != null)
        {
            SetResult(false, $"Export failed: {request.ErrorMessage}");
            Log.Warning(_statusMessage, this);
            return;
        }

        _exportCounter++;
        _lastFilePath = request.AbsolutePath;
        var suffix = request.WasIsolated ? " (separate process)" : " (in-process)";
        SetResult(true, $"Exported {Path.GetFileName(request.AbsolutePath)}{suffix}");
        Log.Info($"Exported {request.AbsolutePath}", this);
    }

    /// <summary>Copies the data to export off the GPU / out of the graph, so the worker owns its own snapshot.</summary>
    private static bool TryCreateSource(bool exportMesh,
                                        MeshBuffers? mesh,
                                        MeshGeometry? geometry,
                                        BufferWithViews? points,
                                        [NotNullWhen(true)] out AssimpModelData? source,
                                        [NotNullWhen(false)] out string? errorMessage)
    {
        source = null;
        if (exportMesh)
        {
            if (geometry is { FaceCount: > 0 })
            {
                source = new AssimpModelData { Geometry = SnapshotGeometry(geometry) };
                errorMessage = null;
                return true;
            }

            if (mesh?.VertexBuffer?.Buffer == null || mesh.IndicesBuffer?.Buffer == null)
            {
                errorMessage = "Connect MeshData or Geometry to export a mesh";
                return false;
            }

            if (!TryReadBuffer(mesh.VertexBuffer, PbrVertex.Stride, out PbrVertex[]? vertices)
                || !TryReadBuffer(mesh.IndicesBuffer, 3 * sizeof(int), out Int3[]? triangles))
            {
                errorMessage = "Failed to read the mesh from the GPU";
                return false;
            }

            source = new AssimpModelData { Vertices = vertices, Triangles = triangles };
            errorMessage = null;
            return true;
        }

        if (points?.Buffer == null)
        {
            errorMessage = "Connect Points to export a point cloud";
            return false;
        }

        if (!TryReadBuffer(points, Point.Stride, out Point[]? pointData))
        {
            errorMessage = "Failed to read the points from the GPU";
            return false;
        }

        source = new AssimpModelData { Points = pointData };
        errorMessage = null;
        return true;
    }

    /// <summary>
    /// Copies the arrays of a live <see cref="MeshGeometry"/>. Geometry operators rebuild their output
    /// instance in place on later frames, so a worker thread must never read the live instance.
    /// </summary>
    private static MeshGeometry SnapshotGeometry(MeshGeometry geometry)
    {
        var snapshot = new MeshGeometry
                           {
                               Positions = (Vector3[])geometry.Positions.Clone(),
                               FaceCornerOffsets = (int[])geometry.FaceCornerOffsets.Clone(),
                               CornerPointIndices = (int[])geometry.CornerPointIndices.Clone(),
                               Parts = (GeometryPart[])geometry.Parts.Clone(),
                           };

        CopyAttribute<Vector3>(geometry, snapshot, GeometryAttributeNames.Normal);
        CopyAttribute<Vector2>(geometry, snapshot, GeometryAttributeNames.TexCoord);
        CopyAttribute<Vector4>(geometry, snapshot, GeometryAttributeNames.Color);
        snapshot.InvalidateTopologyCaches();
        return snapshot;
    }

    private static void CopyAttribute<T>(MeshGeometry source, MeshGeometry target, string name) where T : unmanaged
    {
        if (!source.Attributes.TryGet<T>(name, AttributeDomain.Corner, out var attribute))
            return;

        target.Attributes.Add(new GeometryAttribute<T>(name, AttributeDomain.Corner, attribute.Values.Length)
                                  {
                                      Values = (T[])attribute.Values.Clone(),
                                  });
    }

    /// <summary>Synchronous staging readback; only called for a user-triggered export.</summary>
    private static bool TryReadBuffer<T>(BufferWithViews? source, int expectedStride, [NotNullWhen(true)] out T[]? data) where T : struct
    {
        data = null;
        if (source?.Buffer == null || source.Buffer.IsDisposed || source.Srv == null || source.Srv.IsDisposed)
            return false;

        if (source.ElementStride != expectedStride)
            return false;

        var elementCount = source.Srv.Description.Buffer.ElementCount;
        if (elementCount <= 0)
            return false;

        var device = ResourceManager.Device;
        var immediateContext = device.ImmediateContext;

        var description = source.Buffer.Description;
        description.Usage = ResourceUsage.Staging;
        description.BindFlags = BindFlags.None;
        description.CpuAccessFlags = CpuAccessFlags.Read;
        description.OptionFlags = ResourceOptionFlags.None;

        using var stagingBuffer = new Buffer(device, description);
        immediateContext.CopyResource(source.Buffer, stagingBuffer);

        var dataBox = immediateContext.MapSubresource(stagingBuffer, 0, MapMode.Read, MapFlags.None);
        try
        {
            data = new T[elementCount];
            GraphicsUtilities.Read(dataBox.DataPointer, data, 0, elementCount);
            return true;
        }
        finally
        {
            immediateContext.UnmapSubresource(stagingBuffer, 0);
        }
    }
    #endregion

    #region paths and formats
    /// <summary>
    /// Picks the next free MeshName_###.ext. Relative folders resolve into the project like other
    /// file outputs (e.g. "exports" or "MyProject:exports"); absolute folders are used as they are.
    /// </summary>
    private bool TryReserveFilePath(string folder, string baseName, string extension,
                                    out string absolutePath, [NotNullWhen(false)] out string? errorMessage)
    {
        absolutePath = string.Empty;
        folder = folder.Trim().Replace('\\', '/').TrimEnd('/');

        string folderPath;
        if (folder.Length > 0 && Path.IsPathRooted(folder))
        {
            folderPath = folder;
        }
        else
        {
            var probeAddress = (folder.Length > 0 ? folder + "/" : string.Empty) + baseName + extension;
            // Without a package prefix, write into the project that contains this operator
            if (!probeAddress.Contains(':'))
            {
                var package = Parent?.Symbol.SymbolPackage ?? Symbol.SymbolPackage;
                probeAddress = $"{package.Name}:{probeAddress}";
            }

            if (!AssetRegistry.TryResolveAddressForWriting(probeAddress, this, out var probePath, out var failureReason))
            {
                errorMessage = failureReason;
                return false;
            }

            folderPath = Path.GetDirectoryName(probePath) ?? string.Empty;
        }

        const int maxCounter = 99_999;
        for (var counter = Math.Max(1, _exportCounter); counter <= maxCounter; counter++)
        {
            var candidate = Path.Combine(folderPath, $"{baseName}_{counter:D3}{extension}");
            if (File.Exists(candidate))
                continue;

            _exportCounter = counter;
            absolutePath = candidate;
            errorMessage = null;
            return true;
        }

        errorMessage = $"No free file name for {baseName} in {folderPath}";
        return false;
    }

    private bool TryResolveFormat(ExportFormats preset, string customFormat, string meshName,
                                  out string formatId, out string extension, [NotNullWhen(false)] out string? errorMessage)
    {
        formatId = preset switch
                       {
                           ExportFormats.Obj        => "obj",
                           ExportFormats.Fbx        => "fbx",
                           ExportFormats.Gltf       => "gltf2",
                           ExportFormats.Glb        => "glb2",
                           ExportFormats.Stl        => "stlb",
                           ExportFormats.Ply        => "ply",
                           ExportFormats.Collada    => "collada",
                           ExportFormats.ThreeDs    => "3ds",
                           ExportFormats.Custom     => customFormat.Trim().ToLowerInvariant(),
                           ExportFormats.ThreeMf    => "3mf",
                           ExportFormats.X3d        => "x3d",
                           ExportFormats.StlAscii   => "stl",
                           ExportFormats.PlyBinary  => "plyb",
                           _                             => GetFormatIdForExtension(Path.GetExtension(meshName)) ?? "obj",
                       };

        extension = string.Empty;
        if (_formatsQuery is not { IsCompletedSuccessfully: true })
        {
            errorMessage = _formatsQuery is { IsFaulted: true }
                               ? _formatsQuery.Exception!.GetBaseException().Message
                               : "Assimp is still starting up, try again in a moment";
            return false;
        }

        foreach (var (id, formatExtension, _) in _formatsQuery.Result)
        {
            if (!string.Equals(id, formatId, StringComparison.OrdinalIgnoreCase))
                continue;

            extension = formatExtension;
            errorMessage = null;
            return true;
        }

        errorMessage = string.IsNullOrEmpty(formatId)
                           ? "Set CustomFormat to one of the SupportedFormats"
                           : $"Format '{formatId}' is not supported. Available: {_supportedFormatsText}";
        return false;
    }

    private static string? GetFormatIdForExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
                   {
                       ".obj"  => "obj",
                       ".fbx"  => "fbx",
                       ".gltf" => "gltf2",
                       ".glb"  => "glb2",
                       ".stl"  => "stlb",
                       ".ply"  => "ply",
                       ".dae"  => "collada",
                       ".3ds"  => "3ds",
                       ".3mf"  => "3mf",
                       ".x3d"  => "x3d",
                       _       => null,
                   };
    }

    /// <summary>One safe file name segment without extension, so the name can't escape the target folder.</summary>
    private static string SanitizeFileName(string meshName)
    {
        var name = Path.GetFileNameWithoutExtension(meshName.Trim());
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidChar, '_');
        }

        // Also replace characters invalid on Windows, so files written on Linux stay portable
        foreach (var invalidChar in "<>:\"/\\|?*")
        {
            name = name.Replace(invalidChar, '_');
        }

        name = name.Trim().TrimEnd('.');
        if (name.Length > 100)
            name = name[..100];

        return name.Length == 0 ? "mesh" : name;
    }

    /// <summary>Queried once per editor session in the background; loads the native library on first use.</summary>
    private void UpdateSupportedFormats()
    {
        _formatsQuery ??= _sharedFormatsQuery.Value;
        if (_supportedFormatsText.Length > 0 || !_formatsQuery.IsCompleted)
            return;

        if (_formatsQuery.IsCompletedSuccessfully)
        {
            var ids = new List<string>();
            foreach (var format in _formatsQuery.Result)
            {
                ids.Add(format.FormatId);
            }

            ids.Sort(StringComparer.Ordinal);
            _supportedFormatsText = string.Join(", ", ids);
        }
        else
        {
            SetResult(false, _formatsQuery.Exception?.GetBaseException().Message ?? "Assimp is unavailable");
        }
    }

    private static readonly Lazy<Task<IReadOnlyList<(string FormatId, string Extension, string Description)>>> _sharedFormatsQuery =
        new(() => Task.Run(AssimpModelExporter.GetExportFormats), LazyThreadSafetyMode.ExecutionAndPublication);
    #endregion

    #region status provider
    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        if (_pendingExport != null)
            return IStatusProvider.StatusLevel.Notice;

        return _lastSuccess || _statusMessage.Length == 0 ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage() => _statusMessage;

    private void SetResult(bool success, string message)
    {
        _lastSuccess = success;
        _statusMessage = message;
    }
    #endregion

    private enum ExportFormats
    {
        Auto,
        Obj,
        Fbx,
        Gltf,
        Glb,
        Stl,
        Ply,
        Collada,
        ThreeDs,
        Custom,
        ThreeMf,
        X3d,
        StlAscii,
        PlyBinary,
    }

    private sealed class ExportRequest(string absolutePath)
    {
        public readonly string AbsolutePath = absolutePath;

        /** Set by the worker before it marks the request completed. */
        public bool WasIsolated;

        public string? ErrorMessage;
        public bool IsCompleted;
    }

    private Task<IReadOnlyList<(string FormatId, string Extension, string Description)>>? _formatsQuery;
    private string _supportedFormatsText = string.Empty;
    private ExportRequest? _pendingExport;

    private bool _exportMeshTriggered;
    private bool _exportPointsTriggered;
    private bool _resetCounterTriggered;
    private int _exportCounter = 1;
    private bool _lastSuccess;
    private string _statusMessage = string.Empty;
    private string _lastFilePath = string.Empty;

    [Input(Guid = "47F14471-1D6E-4BAD-A3CC-F372DDA4872C")]
    public readonly InputSlot<MeshBuffers> MeshData = new();

    [Input(Guid = "0B8E46D2-7F3A-4C91-A5D8-E2964F1C3B70")]
    public readonly InputSlot<MeshGeometry> Geometry = new();

    [Input(Guid = "B0E70377-685C-44B1-8069-2F0C2A9DCBB2")]
    public readonly InputSlot<BufferWithViews> Points = new();

    [Input(Guid = "F784FE22-BA03-499C-BB4E-FE2DED8BF754")]
    public readonly InputSlot<string> FolderPath = new("exports");

    [Input(Guid = "2B3C4D5E-6F7A-8B9C-1D2E-3F4A5B6C7D8E")]
    public readonly InputSlot<string> MeshName = new("mesh");

    [Input(Guid = "E8C4F2A7-9B3D-4E1F-A5C6-7D8E9F0A1B2C", MappedType = typeof(ExportFormats))]
    public readonly InputSlot<int> ExportFormat = new((int)ExportFormats.Obj);

    [Input(Guid = "F3D5E8B2-7A4C-4F9E-B2D1-6E7F8A9C0B1D")]
    public readonly InputSlot<string> CustomFormat = new();

    [Input(Guid = "20960269-69A9-4FF1-A3E7-395883521B06")]
    public readonly InputSlot<bool> ExportMesh = new();

    [Input(Guid = "5E6F7A8B-9C1D-2E3F-4A5B-6C7D8E9F0A1B")]
    public readonly InputSlot<bool> ExportPoints = new();

    [Input(Guid = "3C4D5E6F-7A8B-9C1D-2E3F-4A5B6C7D8E9F")]
    public readonly InputSlot<bool> ResetCounter = new();

    [Input(Guid = "FFEB7639-9911-4099-A69F-73BBD1DA9757")]
    public readonly InputSlot<bool> IsolateExport = new(true);
}
