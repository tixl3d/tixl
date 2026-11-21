using System;
using System.Numerics;
using T3.Graphics.Compat;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Interfaces;
using T3.Core.Rendering;
using T3.Core.Resource;
using T3.Core.Utils;
using static Lib.Utils.LaserProjection;

namespace Lib.laser.convert;

/// <summary>
/// Converts a mesh into laser points. Wireframe mode emits each unique triangle edge
/// as an isolated lit run (blanked approach point, then straight-line samples at
/// MaxEdgeStep) - the encoding LaserOptimizer already splits into shape segments.
/// Vertices mode emits each vertex as an isolated dwell dot, identical to
/// LaserCamera's Points mode, so point clouds never get connected by lit strokes.
/// </summary>
[Guid("65770451-B526-4D5A-B1A5-262CF9549C41")]
internal sealed class MeshToLaserPoints : Instance<MeshToLaserPoints>
{
    [Output(Guid = "469832F7-F83B-47EB-ADB8-2BC39B5F6D99")]
    public readonly Slot<StructuredList> LaserPoints = new();

    [Output(Guid = "D54E2936-E57A-4E3C-B31E-394F14491D62", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> PointCount = new();

    public MeshToLaserPoints()
    {
        // Both outputs must run the update: a consumer wired to PointCount alone would otherwise
        // read the initial 0 forever, and with both pulled in one frame the value lags a frame.
        LaserPoints.UpdateAction += Update;
        PointCount.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        // TiXL terminates the process on an unhandled operator exception. GPU readback
        // and malformed meshes must degrade to an empty frame, never crash the editor.
        try
        {
            UpdateInternal(context);
        }
        catch (Exception e)
        {
            Log.Error($"MeshToLaserPoints: update failed, outputting empty frame - {e.Message}", this);
            SetEmptyOutput();
        }
    }

    private void UpdateInternal(EvaluationContext context)
    {
        var mesh = Mesh.GetValue(context);
        if (mesh?.VertexBuffer?.Buffer == null || mesh.IndicesBuffer?.Buffer == null
                                                || mesh.VertexBuffer.Srv == null || mesh.IndicesBuffer.Srv == null)
        {
            SetEmptyOutput();
            return;
        }

        if (Camera.GetValue(context) is not ICamera camera)
        {
            SetEmptyOutput();
            return;
        }

        // The projection depends on the camera pose as much as on the mesh: a static mesh
        // viewed by a moving camera must re-project even when nothing else changed.
        var worldToCamera = camera.WorldToCamera;
        var cameraToClipSpace = camera.CameraToClipSpace;

        var wasTriggered = MathUtils.WasTriggered(TriggerUpdate.GetValue(context), ref _triggerUpdate);
        if (wasTriggered)
        {
            TriggerUpdate.SetTypedInputValue(false);
        }

        var updateContinuously = UpdateContinuously.GetValue(context);
        var mode = (PrimitiveMode)Mode.GetValue(context).Clamp(0, 1);
        var maxEdgeStep = MaxEdgeStep.GetValue(context);
        maxEdgeStep = float.IsFinite(maxEdgeStep) ? MathF.Max(1f, maxEdgeStep) : 4000f;
        var dotDwell = DotDwellPoints.GetValue(context).Clamp(1, MaxDotDwellPoints);
        var brightness = Math.Clamp(Brightness.GetValue(context), 0f, 1f);

        var sourceVertexBytes = mesh.VertexBuffer.Buffer.Description.SizeInBytes;
        var sourceIndexBytes = mesh.IndicesBuffer.Buffer.Description.SizeInBytes;
        var geometryChanged = !ReferenceEquals(mesh.VertexBuffer.Buffer, _lastVertexBuffer)
                              || !ReferenceEquals(mesh.IndicesBuffer.Buffer, _lastIndexBuffer)
                              || sourceVertexBytes != _lastVertexBytes
                              || sourceIndexBytes != _lastIndexBytes;

        // Keep the last frame unless something actually changed - GPU readback stalls
        // the pipeline, so it must not run every evaluation for a static mesh. Comparing the
        // mesh version (not the staging buffer size) means an animated Brightness or
        // MaxEdgeStep rebuilds from the cached CPU copy instead of reading the GPU again.
        if (!wasTriggered && !updateContinuously && _hasResult
            && _lastMode == mode
            && _lastMaxEdgeStep.Equals(maxEdgeStep)
            && _lastDotDwell == dotDwell
            && _lastBrightness.Equals(brightness)
            && !geometryChanged
            && _lastWorldToCamera.Equals(worldToCamera)
            && _lastCameraToClipSpace.Equals(cameraToClipSpace)
            && _cachedVertices != null
            && _cachedFaces != null)
        {
            LaserPoints.DirtyFlag.Trigger = updateContinuously ? DirtyFlagTrigger.Animated : DirtyFlagTrigger.None;
            return;
        }

        var vertexCount = Math.Min(mesh.VertexBuffer.Srv.Description.Buffer.ElementCount, MaxMeshVertices);
        var faceCount = Math.Min(mesh.IndicesBuffer.Srv.Description.Buffer.ElementCount, MaxMeshFaces);

        PbrVertex[]? vertices;
        Int3[]? faces;
        if (!geometryChanged && _cachedVertices != null && _cachedFaces != null)
        {
            vertices = _cachedVertices;
            faces = _cachedFaces;
        }
        else
        {
            vertices = ReadBuffer<PbrVertex>(mesh.VertexBuffer.Buffer, sourceVertexBytes, PbrVertex.Stride, ref _vertexCpuAccess, vertexCount);
            faces = ReadBuffer<Int3>(mesh.IndicesBuffer.Buffer, sourceIndexBytes, 3 * 4, ref _indexCpuAccess, faceCount);

            if (vertices != null && faces != null)
            {
                _cachedVertices = vertices;
                _cachedFaces = faces;
                _lastVertexBuffer = mesh.VertexBuffer.Buffer;
                _lastIndexBuffer = mesh.IndicesBuffer.Buffer;
                _lastVertexBytes = sourceVertexBytes;
                _lastIndexBytes = sourceIndexBytes;
            }
        }

        if (vertices == null || faces == null || vertices.Length == 0)
        {
            SetEmptyOutput();
            return;
        }

        var result = mode == PrimitiveMode.Wireframe
                         ? BuildWireframe(vertices, faces, camera, maxEdgeStep, brightness)
                         : BuildVertexDots(vertices, camera, dotDwell, brightness);

        LaserPoints.Value = result;
        _hasResult = true;
        _lastMode = mode;
        _lastMaxEdgeStep = maxEdgeStep;
        _lastDotDwell = dotDwell;
        _lastBrightness = brightness;
        _lastWorldToCamera = worldToCamera;
        _lastCameraToClipSpace = cameraToClipSpace;

        LaserPoints.DirtyFlag.Trigger = updateContinuously ? DirtyFlagTrigger.Animated : DirtyFlagTrigger.None;
    }

    private StructuredList BuildVertexDots(PbrVertex[] vertices, ICamera camera, int dotDwell, float brightness)
    {
        var combined = camera.WorldToCamera * camera.CameraToClipSpace;

        EnsureScratch(Math.Min(vertices.Length * (1 + dotDwell), MaxOutputPoints));

        var count = 0;
        var haveLast = false;
        var lastX = 0;
        var lastY = 0;

        for (var i = 0; i < vertices.Length && count < _scratch.Length; i++)
        {
            if (!TryProject(vertices[i].Position, combined, out var x, out var y))
                continue;

            // Isolated dot encoding: blanked approach, then a dwell run with the beam
            // on. Without the blank the scanner would draw a line from the previous
            // dot straight to this one.
            if (!haveLast || lastX != x || lastY != y)
            {
                _scratch[count++] = LaserPoint.CreateBlanked(x, y);
                lastX = x;
                lastY = y;
                haveLast = true;
            }

            var vertexColor = vertices[i].ColorRgb;
            ToColor(vertexColor.X, vertexColor.Y, vertexColor.Z, brightness, out var r, out var g, out var b);
            for (var d = 0; d < dotDwell && count < _scratch.Length; d++)
            {
                _scratch[count++] = new LaserPoint(x, y, r, g, b);
            }
        }

        return Finish(count);
    }

    private StructuredList BuildWireframe(PbrVertex[] vertices, Int3[] faces, ICamera camera, float maxEdgeStep, float brightness)
    {
        var combined = camera.WorldToCamera * camera.CameraToClipSpace;
        _edgeKeys.Clear();

        var count = 0;
        var capped = false;

        for (var f = 0; f < faces.Length; f++)
        {
            var face = faces[f];

            for (var e = 0; e < 3; e++)
            {
                var i0 = e == 0 ? face.X : e == 1 ? face.Y : face.Z;
                var i1 = e == 0 ? face.Y : e == 1 ? face.Z : face.X;

                if (i0 < 0 || i0 >= vertices.Length || i1 < 0 || i1 >= vertices.Length)
                    continue;

                // Dedupe shared edges so interior wireframe lines don't scan twice
                var a = Math.Min(i0, i1);
                var b = Math.Max(i0, i1);
                if (!_edgeKeys.Add(((long)a << 32) | (uint)b))
                    continue;

                if (_edgeKeys.Count > MaxEdges)
                {
                    capped = true;
                    break;
                }

                if (!TryProjectSegment(vertices[i0].Position, vertices[i1].Position, combined,
                                       out var x0, out var y0, out var x1, out var y1))
                    continue;

                var dx = x1 - x0;
                var dy = y1 - y0;
                var distance = MathF.Sqrt(dx * dx + dy * dy);
                var steps = Math.Max(1, (int)MathF.Ceiling(distance / maxEdgeStep));
                steps = Math.Min(steps, MaxSamplesPerEdge);

                // One isolated lit run per edge: blanked approach, then straight-line
                // samples from v0 to v1 with the beam on
                var needed = 2 + steps;
                if (count + needed > _scratch.Length && _scratch.Length < MaxOutputPoints)
                {
                    var newSize = Math.Min(Math.Max(_scratch.Length * 2, count + needed), MaxOutputPoints);
                    Array.Resize(ref _scratch, newSize);
                }

                if (count >= _scratch.Length)
                {
                    capped = true;
                    break;
                }

                _scratch[count++] = LaserPoint.CreateBlanked((int)MathF.Round(x0), (int)MathF.Round(y0));

                var color = vertices[i0].ColorRgb;
                ToColor(color.X, color.Y, color.Z, brightness, out var r, out var g, out var bl);
                for (var s = 0; s <= steps && count < _scratch.Length; s++)
                {
                    var t = s / (float)steps;
                    var x = (int)MathF.Round(x0 + dx * t);
                    var y = (int)MathF.Round(y0 + dy * t);
                    _scratch[count++] = new LaserPoint(x, y, r, g, bl);
                }
            }

            if (capped)
                break;
        }

        if (capped)
            Log.Debug($"MeshToLaserPoints: output capped ({MaxEdges} edges / {MaxOutputPoints} points)", this);

        return Finish(count);
    }

    private StructuredList Finish(int count)
    {
        count = Math.Min(count, _scratch.Length);
        var resultArray = new LaserPoint[count];
        Array.Copy(_scratch, 0, resultArray, 0, count);
        PointCount.Value = count;
        PointCount.DirtyFlag.Invalidate();
        return new StructuredList<LaserPoint>(resultArray);
    }

    private void EnsureScratch(int required)
    {
        if (_scratch.Length >= required)
            return;

        var newSize = Math.Min(Math.Max(_scratch.Length * 2, required), MaxOutputPoints);
        Array.Resize(ref _scratch, newSize);
    }

    /// <summary>
    /// Copies a GPU structured buffer to a CPU-readable twin and reads it back.
    /// Mirrors LaserCamera's sync path: create an empty structured buffer, then copy -
    /// never CreateBuffer-with-initial-data, whose native copy has crashed the process
    /// with an uncatchable access violation.
    /// </summary>
    private static T[] ReadBuffer<T>(Buffer source, int sourceBytes, int stride, ref BufferWithViews cpuAccess, int elementCount) where T : struct
    {
        try
        {
            if (cpuAccess.Buffer == null || cpuAccess.Buffer.Description.SizeInBytes != sourceBytes)
            {
                cpuAccess.Buffer?.Dispose();
                cpuAccess.Buffer = null;

                var desc = new BufferDescription
                {
                    // Staging is what makes this buffer mappable for CPU read: the compatibility
                    // layer derives D3D11 usage/CPU access from ResourceUsage only, so "Default +
                    // CpuAccessFlags.Read" becomes a plain device-local buffer that MapSubresource
                    // rejects (and that Vulkan never maps) - the readback failed on every backend
                    // and the operator always emitted an empty frame.
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    SizeInBytes = sourceBytes,
                    OptionFlags = ResourceOptionFlags.BufferStructured,
                    StructureByteStride = stride,
                    CpuAccessFlags = CpuAccessFlags.Read
                };
                cpuAccess.Buffer = new Buffer(ResourceManager.Device, desc);
            }

            DataStream stream = null;
            var mapped = false;
            try
            {
                ResourceManager.Device.ImmediateContext.CopyResource(source, cpuAccess.Buffer);
                ResourceManager.Device.ImmediateContext.MapSubresource(cpuAccess.Buffer, 0, MapMode.Read, MapFlags.None, out stream);
                mapped = true;
                return elementCount > 0 ? stream.ReadRange<T>(elementCount) : Array.Empty<T>();
            }
            finally
            {
                stream?.Dispose();
                if (mapped)
                    ResourceManager.Device.ImmediateContext.UnmapSubresource(cpuAccess.Buffer, 0);
            }
        }
        catch (Exception e)
        {
            Log.Error($"MeshToLaserPoints: GPU readback of {typeof(T).Name} failed - {e.Message}");
            return null;
        }
    }

    private void SetEmptyOutput()
    {
        LaserPoints.Value = s_emptyList;
        // Animated so a transient failure self-heals on the next frame instead of
        // leaving the laser chain stuck on an empty frame forever
        LaserPoints.DirtyFlag.Trigger = DirtyFlagTrigger.Animated;
        PointCount.Value = 0;
        PointCount.DirtyFlag.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _vertexCpuAccess.Buffer?.Dispose();
            _vertexCpuAccess.Buffer = null;
            _indexCpuAccess.Buffer?.Dispose();
            _indexCpuAccess.Buffer = null;
        }

        base.Dispose(disposing);
    }

    private static readonly StructuredList s_emptyList = new StructuredList<LaserPoint>(Array.Empty<LaserPoint>());

    private LaserPoint[] _scratch = new LaserPoint[1024];
    private readonly HashSet<long> _edgeKeys = new();
    private bool _triggerUpdate;
    private bool _hasResult;
    private PrimitiveMode _lastMode;
    private float _lastMaxEdgeStep;
    private int _lastDotDwell;
    private float _lastBrightness;
    private Matrix4x4 _lastWorldToCamera;
    private Matrix4x4 _lastCameraToClipSpace;
    private BufferWithViews _vertexCpuAccess = new();
    private BufferWithViews _indexCpuAccess = new();
    private PbrVertex[]? _cachedVertices;
    private Int3[]? _cachedFaces;
    private Buffer? _lastVertexBuffer;
    private Buffer? _lastIndexBuffer;
    private int _lastVertexBytes = -1;
    private int _lastIndexBytes = -1;

    private enum PrimitiveMode
    {
        Wireframe,
        Vertices,
    }

    /// <summary>Hard cap on generated points (1M points = 24 MB scratch), matching the
    /// other laser operators.</summary>
    private const int MaxOutputPoints = 1 << 20;

    /// <summary>Upper bound on unique wireframe edges per frame - keeps huge meshes from
    /// ballooning the laser frame.</summary>
    private const int MaxEdges = 65536;

    /// <summary>Upper bound on lit samples per single edge; bounds the cost of a tiny
    /// MaxEdgeStep against a full-field edge.</summary>
    private const int MaxSamplesPerEdge = 256;

    /// <summary>Readback size caps - bigger meshes are truncated, not skipped.</summary>
    private const int MaxMeshVertices = 131072;
    private const int MaxMeshFaces = 131072;

    /// <summary>Upper dwell limit per dot. Kept at or below LaserSafety's default
    /// MaxStaticPoints (64) so dots survive the static-dwell limiter unblanked.</summary>
    private const int MaxDotDwellPoints = 64;

    [Input(Guid = "A0C2C2C8-F247-4B9D-8993-730D502B6ACA")]
    public readonly InputSlot<MeshBuffers> Mesh = new();

    [Input(Guid = "AC065C9C-EAC6-4F6C-A2B2-F9F1515A2353")]
    public readonly InputSlot<Object> Camera = new();

    [Input(Guid = "C883666D-4BDA-40A6-9289-0C8508E32EA0", MappedType = typeof(PrimitiveMode))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "4F55D55B-B51D-4A29-8788-E1AE21A80960")]
    public readonly InputSlot<float> MaxEdgeStep = new(4000f);

    [Input(Guid = "FCB3B079-830A-403E-A549-A3E3F1F95C56")]
    public readonly InputSlot<int> DotDwellPoints = new(8);

    [Input(Guid = "C1BE157F-9E5E-45B9-91AB-C133CE4BAE65")]
    public readonly InputSlot<float> Brightness = new(1f);

    [Input(Guid = "B8B1385D-BE4D-4218-9FF2-27E8B853CA73")]
    public readonly InputSlot<bool> UpdateContinuously = new();

    [Input(Guid = "944B4428-2852-4474-A344-D24D3FB30BB6")]
    public readonly InputSlot<bool> TriggerUpdate = new();
}
