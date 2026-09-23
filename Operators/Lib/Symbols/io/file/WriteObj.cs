#nullable enable
using System.Globalization;
using System.IO;
using System.Text;
using SharpDX.Direct3D11;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Rendering;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.Utils;

namespace Lib.io.file;

[Guid("d2a3f0b4-8a17-4c22-9b61-2a4e6d81b0c5")]
internal sealed class WriteObj : Instance<WriteObj>, IStatusProvider
{
    [Output(Guid = "572dd7d6-1c5f-4490-8f72-e14a941eed69")]
    public readonly Slot<MeshBuffers> Data = new();

    [Output(Guid = "4C2E8F1B-9D74-4E89-A0B3-71C6F25AE9D1")]
    public readonly Slot<string> Result = new();

    [Output(Guid = "B7E9A2D4-1F63-4D8A-B5C0-9E8F27C64A31")]
    public readonly Slot<string> OutFilepath = new();

    public WriteObj()
    {
        Result.UpdateAction += Update;
        Data.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var mesh = MeshBuffer.GetValue(context);
        var folder = Folder.GetValue(context) ?? string.Empty;
        var fileName = FileName.GetValue(context) ?? string.Empty;
        //var trigger = MathUtils.WasTriggered(TriggerWrite.GetValue(context), ref _triggerWrite);

        var relativePath = BuildRelativePath(folder, fileName);

        var meshChanged = !ReferenceEquals(mesh, _lastMeshBuffer);
        var pathChanged = relativePath != _lastPath;
        if (mesh == null) {
            return;
        }
        else
        {
            Data.Value = mesh;
        }
        if (MathUtils.WasTriggered(TriggerWrite.GetValue(context), ref _triggerWrite))
        {
            _lastMeshBuffer = mesh;
            _lastPath = relativePath;

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                _statusMessage = "File name is empty";
                Log.Warning(_statusMessage, this);
                Result.Value = string.Empty;
            }
            else if (mesh?.VertexBuffer?.Buffer == null || mesh.IndicesBuffer?.Buffer == null)
            {
                _statusMessage = "No valid mesh buffer connected";
                Log.Error(_statusMessage, this);
                Result.Value = string.Empty;
            }
            else if (TryWrite(mesh, relativePath))
            {
                Result.Value = relativePath;
            }
            else
            {
                Result.Value = string.Empty;
            }
            TriggerWrite.SetTypedInputValue(false);
            _triggerWrite = false;
        }

        OutFilepath.Value = relativePath;
    }

    private static string BuildRelativePath(string folder, string fileName)
    {
        var name = SanitizeObjFileName(fileName);
        if (name.Length == 0)
            return string.Empty;

        var dir = folder.Trim();
        if (dir.Length == 0)
            return name;

        // Normalize slashes for the asset registry.
        dir = dir.Replace('\\', '/').TrimEnd('/');
        return $"{dir}/{name}";
    }

    /// <summary>
    /// Reduces the user-provided <see cref="FileName"/> to a single, safe file-name segment that always
    /// ends in ".obj": invalid characters and path separators are replaced (so a file name can neither
    /// break the write nor redirect it out of <see cref="Folder"/>), and an already given extension is
    /// replaced instead of appended ("mesh.ply" → "mesh.obj", "mesh" → "mesh.obj").
    /// </summary>
    private static string SanitizeObjFileName(string fileName)
    {
        const string objExtension = ".obj";

        var name = (fileName ?? string.Empty).Trim();

        // Keep it one segment: a stray separator must not escape the target folder.
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            name = name.Replace(invalidChar, '_');

        // Windows silently drops trailing dots and spaces, which would swallow the extension.
        name = name.TrimEnd('.', ' ');

        if (name.Length == 0)
            return string.Empty;

        var lastDot = name.LastIndexOf('.');
        if (lastDot > 0)
            name = name[..lastDot];

        return name + objExtension;
    }

    private bool TryWrite(MeshBuffers mesh, string relativePath)
    {
        if (!AssetRegistry.TryResolveAddressForWriting(relativePath, this, out var absolutePath, out var failureReason))
        {
            _statusMessage = failureReason;
            Log.Error(failureReason, this);
            return false;
        }

        if (!TryReadBuffer(mesh.VertexBuffer, out PbrVertex[]? vertices) || vertices == null)
        {
            _statusMessage = "Failed to read vertex buffer from GPU";
            Log.Error(_statusMessage, this);
            return false;
        }

        if (!TryReadBuffer(mesh.IndicesBuffer, out Int3[]? indices) || indices == null)
        {
            _statusMessage = "Failed to read index buffer from GPU";
            Log.Error(_statusMessage, this);
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (var writer = new StreamWriter(absolutePath, false, new UTF8Encoding(false)))
            {
                WriteObjContent(writer, vertices, indices);
            }

            _statusMessage = $"Saved {vertices.Length} vertices / {indices.Length} faces → {relativePath}";
            Log.Info(_statusMessage, this);
            return true;
        }
        catch (Exception e)
        {
            _statusMessage = $"Failed to write OBJ file '{absolutePath}': {e.Message}";
            Log.Error(_statusMessage, this);
            return false;
        }
    }

    private static bool TryReadBuffer<T>(BufferWithViews? source, out T[]? data) where T : struct
    {
        data = null;
        if (source?.Buffer == null || source.Srv == null)
            return false;

        var elementCount = source.Srv.Description.Buffer.ElementCount;
        if (elementCount <= 0)
        {
            data = Array.Empty<T>();
            return true;
        }

        var device = ResourceManager.Device;
        var immediateContext = device.ImmediateContext;

        var sourceDesc = source.Buffer.Description;

        var bufferDesc = new BufferDescription
        {
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
            SizeInBytes = sourceDesc.SizeInBytes,
            OptionFlags = ResourceOptionFlags.BufferStructured,
            StructureByteStride = sourceDesc.StructureByteStride,
            CpuAccessFlags = CpuAccessFlags.Read,
        };

        using var cpuBuffer = new Buffer(device, bufferDesc);

        immediateContext.CopyResource(source.Buffer, cpuBuffer);

        immediateContext.MapSubresource(cpuBuffer, 0, MapMode.Read, MapFlags.None, out var stream);
        try
        {
            data = stream.ReadRange<T>(elementCount);
            return true;
        }
        finally
        {
            stream.Dispose();
            immediateContext.UnmapSubresource(cpuBuffer, 0);
        }
    }

    private static void WriteObjContent(TextWriter writer, PbrVertex[] vertices, Int3[] indices)
    {
        var inv = CultureInfo.InvariantCulture;

        // Heuristic: only emit the extended "v x y z r g b" form if at least
        // one vertex carries non-default color. Keeps files clean when there
        // is no color data, and Blender-friendly when there is.
        var includeColors = false;
        for (var i = 0; i < vertices.Length; i++)
        {
            if (vertices[i].ColorRgb != Vector3.One)
            {
                includeColors = true;
                break;
            }
        }

        writer.WriteLine("# Exported by Tixl3d");
        writer.WriteLine($"# Vertices: {vertices.Length}, Faces: {indices.Length}"
                         + (includeColors ? " (with vertex colors)" : ""));
        writer.WriteLine();

        for (var i = 0; i < vertices.Length; i++)
        {
            var v = vertices[i];
            var p = v.Position;
            writer.Write("v ");
            writer.Write(p.X.ToString("R", inv)); writer.Write(' ');
            writer.Write(p.Y.ToString("R", inv)); writer.Write(' ');
            writer.Write(p.Z.ToString("R", inv));

            if (includeColors)
            {
                var c = v.ColorRgb;
                writer.Write(' ');
                writer.Write(c.X.ToString("R", inv)); writer.Write(' ');
                writer.Write(c.Y.ToString("R", inv)); writer.Write(' ');
                writer.Write(c.Z.ToString("R", inv));
            }
            writer.WriteLine();
        }

        for (var i = 0; i < vertices.Length; i++)
        {
            var uv = vertices[i].Texcoord;
            writer.Write("vt ");
            writer.Write(uv.X.ToString("R", inv)); writer.Write(' ');
            writer.WriteLine(uv.Y.ToString("R", inv));
        }

        for (var i = 0; i < vertices.Length; i++)
        {
            var n = vertices[i].Normal;
            writer.Write("vn ");
            writer.Write(n.X.ToString("R", inv)); writer.Write(' ');
            writer.Write(n.Y.ToString("R", inv)); writer.Write(' ');
            writer.WriteLine(n.Z.ToString("R", inv));
        }

        writer.WriteLine();

        for (var i = 0; i < indices.Length; i++)
        {
            var f = indices[i];
            writer.Write("f ");
            WriteFaceVertex(writer, f.X + 1);
            writer.Write(' ');
            WriteFaceVertex(writer, f.Y + 1);
            writer.Write(' ');
            WriteFaceVertex(writer, f.Z + 1);
            writer.WriteLine();
        }
    }

    private static void WriteFaceVertex(TextWriter writer, int index)
    {
        writer.Write(index);
        writer.Write('/');
        writer.Write(index);
        writer.Write('/');
        writer.Write(index);
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_statusMessage) || _statusMessage.StartsWith("Saved ")
                   ? IStatusProvider.StatusLevel.Success
                   : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage() => _statusMessage;

    private string _statusMessage = string.Empty;
    private bool _triggerWrite;
    private MeshBuffers? _lastMeshBuffer;
    private string? _lastPath;

    [Input(Guid = "6E1F2D9A-3C87-4B15-9E24-5A0B8D7F1C63")]
    public readonly InputSlot<MeshBuffers?> MeshBuffer = new();

    [Input(Guid = "8F4B7E2C-1A6D-4E93-B0C5-2D9A3F17E8B4")]
    public readonly InputSlot<string> Folder = new();

    [Input(Guid = "1C7A6E9F-4B85-4D23-8A07-3E9D5F2B1C64")]
    public readonly InputSlot<string> FileName = new();

    [Input(Guid = "3A9D5C1E-7B42-4F86-A1E9-6C0D2F83A5B7")]
    public readonly InputSlot<bool> TriggerWrite = new();
}