using SharpDX.Direct3D11;
using T3.Core.Rendering;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.mesh.generate;

[Guid("4E5AC2B9-5905-4F24-B0FC-0980357927D4")]
internal sealed class ClosedLoopMesh : Instance<ClosedLoopMesh>
{
    [Output(Guid = "87125559-AF11-409E-B61A-D4DEA84202BA")]
    public readonly Slot<MeshBuffers> Mesh = new();

    public ClosedLoopMesh()
    {
        Mesh.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        try
        {
            var pointBuffer = Points.GetValue(context);
            var pointsPerShape = Math.Max(0, PointsPerShape.GetValue(context));

            if (pointBuffer == null || pointBuffer.Buffer == null || pointBuffer.Buffer.Description.SizeInBytes == 0)
            {
                Log.Warning("ClosedLoopMesh: No points connected");
                return;
            }

            if (pointBuffer.Buffer.Description.StructureByteStride != Point.Stride)
            {
                Log.Warning("ClosedLoopMesh: Buffer is not a Point-buffer (expected stride 64 bytes)");
                return;
            }

            var points = ReadBackPoints(pointBuffer);
            if (points.Length == 0)
            {
                Log.Warning("ClosedLoopMesh: No points could be read back");
                return;
            }

            var loops = new List<Point[]>();
            if (pointsPerShape > 0)
            {
                for (var start = 0; start < points.Length; start += pointsPerShape)
                    CollectLoops(points, start, Math.Min(start + pointsPerShape, points.Length), loops);
            }
            else
            {
                CollectLoops(points, 0, points.Length, loops);
            }

            var vertices = new List<PbrVertex>();
            var triangles = new List<Int3>();

            foreach (var loop in loops)
            {
                if (loop.Length < 3)
                    continue;

                var positions = new Vector3[loop.Length];
                for (var i = 0; i < loop.Length; i++)
                    positions[i] = loop[i].Position;

                var loopNormal = ComputeNewellNormal(positions);
                if (loopNormal.LengthSquared() < 0.0001f)
                {
                    Log.Warning("ClosedLoopMesh: Degenerate loop - points are collinear");
                    continue;
                }

                // Right-handed orthonormal basis (u x v = normal)
                var uAxis = Vector3.Normalize(Vector3.Cross(PickMostOrthogonalAxis(loopNormal), loopNormal));
                var vAxis = Vector3.Cross(loopNormal, uAxis);

                var origin = positions[0];
                var plane = new Vector2[loop.Length];
                var minU = float.MaxValue;
                var maxU = float.MinValue;
                var minV = float.MaxValue;
                var maxV = float.MinValue;
                for (var i = 0; i < loop.Length; i++)
                {
                    var delta = positions[i] - origin;
                    var u = Vector3.Dot(delta, uAxis);
                    var v = Vector3.Dot(delta, vAxis);
                    plane[i] = new Vector2(u, v);
                    minU = MathF.Min(minU, u);
                    maxU = MathF.Max(maxU, u);
                    minV = MathF.Min(minV, v);
                    maxV = MathF.Max(maxV, v);
                }

                if (!TryEarClip(plane, out var clipped))
                {
                    Log.Warning("ClosedLoopMesh: Loop triangulation failed (degenerate or self-intersecting polygon)");
                    continue;
                }

                var rangeU = MathF.Max(maxU - minU, 0.0001f);
                var rangeV = MathF.Max(maxV - minV, 0.0001f);

                var firstVertex = vertices.Count;
                foreach (var tri in clipped)
                {
                    Vector3 Pa = positions[tri.X];
                    Vector3 Pb = positions[tri.Y];
                    Vector3 Pc = positions[tri.Z];

                    var faceNormal = Vector3.Normalize(Vector3.Cross(Pb - Pa, Pc - Pa));
                    if (faceNormal.Z < 0)
                        faceNormal = -faceNormal;

                    AddPlanarVertex(vertices, Pa, loop[tri.X], faceNormal, uAxis, vAxis, plane[tri.X], minU, rangeU, minV, rangeV);
                    AddPlanarVertex(vertices, Pb, loop[tri.Y], faceNormal, uAxis, vAxis, plane[tri.Y], minU, rangeU, minV, rangeV);
                    AddPlanarVertex(vertices, Pc, loop[tri.Z], faceNormal, uAxis, vAxis, plane[tri.Z], minU, rangeU, minV, rangeV);

                    // Flat fills face the default camera at (0, 0, +DefaultCameraDistance):
                    // normal attribute and winding agree and both point toward +Z
                    triangles.Add(new Int3(firstVertex, firstVertex + 1, firstVertex + 2));
                    firstVertex += 3;
                }
            }

            if (triangles.Count == 0 || vertices.Count == 0)
            {
                Log.Warning("ClosedLoopMesh: No valid loops to triangulate");
                return;
            }

            UploadBuffers(vertices, triangles);
        }
        catch (Exception e)
        {
            Log.Error("ClosedLoopMesh: " + e.Message, this);
        }
    }

    private static void AddPlanarVertex(List<PbrVertex> vertices, Vector3 position, Point point, Vector3 normal, Vector3 uAxis, Vector3 vAxis, Vector2 plane, float minU, float rangeU, float minV, float rangeV)
    {
        vertices.Add(new PbrVertex
                         {
                             Position = position,
                             Normal = normal,
                             Tangent = uAxis,
                             Bitangent = vAxis,
                             Texcoord = new Vector2((plane.X - minU) / rangeU, (plane.Y - minV) / rangeV),
                             Selection = point.F1,
                             ColorRgb = new Vector3(point.Color.X, point.Color.Y, point.Color.Z),
                         });
    }

    private void UploadBuffers(List<PbrVertex> vertices, List<Int3> triangles)
    {
        var vertexCount = vertices.Count;
        if (_vertexBufferData.Length != vertexCount)
            _vertexBufferData = new PbrVertex[vertexCount];
        vertices.CopyTo(_vertexBufferData);

        var faceCount = triangles.Count;
        if (_indexBufferData.Length != faceCount)
            _indexBufferData = new Int3[faceCount];
        triangles.CopyTo(_indexBufferData);

        var stride = PbrVertex.Stride;
        ResourceManager.SetupStructuredBuffer(_vertexBufferData, stride * vertexCount, stride, ref _vertexBuffer);
        ResourceManager.CreateStructuredBufferSrv(_vertexBuffer, ref _vertexBufferWithViews.Srv);
        ResourceManager.CreateStructuredBufferUav(_vertexBuffer, UnorderedAccessViewBufferFlags.None, ref _vertexBufferWithViews.Uav);
        _vertexBufferWithViews.Buffer = _vertexBuffer;

        stride = 3 * sizeof(int);
        ResourceManager.SetupStructuredBuffer(_indexBufferData, stride * faceCount, stride, ref _indexBuffer);
        ResourceManager.CreateStructuredBufferSrv(_indexBuffer, ref _indexBufferWithViews.Srv);
        ResourceManager.CreateStructuredBufferUav(_indexBuffer, UnorderedAccessViewBufferFlags.None, ref _indexBufferWithViews.Uav);
        _indexBufferWithViews.Buffer = _indexBuffer;

        _data.VertexBuffer = _vertexBufferWithViews;
        _data.IndicesBuffer = _indexBufferWithViews;
        Mesh.Value = _data;
        Mesh.DirtyFlag.Clear();
    }

    private Point[] ReadBackPoints(BufferWithViews pointBuffer)
    {
        var d3DDevice = ResourceManager.Device;
        var immediateContext = d3DDevice.ImmediateContext;
        var sourceBuffer = pointBuffer.Buffer;

        if (_stagingBuffer == null
            || _stagingBuffer.Buffer == null
            || _stagingBuffer.Buffer.Description.SizeInBytes != sourceBuffer.Description.SizeInBytes
            || _stagingBuffer.Buffer.Description.StructureByteStride != sourceBuffer.Description.StructureByteStride)
        {
            if (_stagingBuffer?.Buffer != null)
                Utilities.Dispose(ref _stagingBuffer.Buffer);

            _stagingBuffer ??= new BufferWithViews();

            if (_stagingBuffer.Buffer == null
                || _stagingBuffer.Buffer.Description.SizeInBytes != sourceBuffer.Description.SizeInBytes)
            {
                if (_stagingBuffer.Buffer != null)
                    _stagingBuffer.Buffer.Dispose();

                var bufferDesc = new BufferDescription
                                     {
                                         Usage = ResourceUsage.Default,
                                         BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                                         SizeInBytes = sourceBuffer.Description.SizeInBytes,
                                         OptionFlags = ResourceOptionFlags.BufferStructured,
                                         StructureByteStride = sourceBuffer.Description.StructureByteStride,
                                         CpuAccessFlags = CpuAccessFlags.Read,
                                     };
                _stagingBuffer.Buffer = new Buffer(ResourceManager.Device, bufferDesc);
            }
        }

        immediateContext.CopyResource(sourceBuffer, _stagingBuffer.Buffer);
        immediateContext.MapSubresource(_stagingBuffer.Buffer, 0, MapMode.Read, MapFlags.None, out var sourceStream);

        Point[] points;
        using (sourceStream)
        {
            var elementCount = sourceBuffer.Description.SizeInBytes / sourceBuffer.Description.StructureByteStride;
            points = elementCount > 0 ? sourceStream.ReadRange<Point>(elementCount) : Array.Empty<Point>();
        }

        immediateContext.UnmapSubresource(_stagingBuffer.Buffer, 0);
        return points;
    }

    private static void CollectLoops(Point[] points, int start, int end, List<Point[]> loops)
    {
        var current = new List<Point>();
        for (var i = start; i < end; i++)
        {
            if (Point.IsSeparator(in points[i]))
            {
                if (current.Count > 0)
                {
                    loops.Add(current.ToArray());
                    current = new List<Point>();
                }
            }
            else
            {
                current.Add(points[i]);
            }
        }

        if (current.Count > 0)
            loops.Add(current.ToArray());
    }

    private static Vector3 PickMostOrthogonalAxis(Vector3 normal)
    {
        var absX = MathF.Abs(normal.X);
        var absY = MathF.Abs(normal.Y);
        var absZ = MathF.Abs(normal.Z);
        return absX <= absY && absX <= absZ ? Vector3.UnitX
            : absY <= absZ ? Vector3.UnitY
            : Vector3.UnitZ;
    }

    private static Vector3 ComputeNewellNormal(Vector3[] positions)
    {
        var normal = Vector3.Zero;
        for (var i = 0; i < positions.Length; i++)
        {
            var a = positions[i];
            var b = positions[(i + 1) % positions.Length];
            normal.X += (a.Y - b.Y) * (a.Z + b.Z);
            normal.Y += (a.Z - b.Z) * (a.X + b.X);
            normal.Z += (a.X - b.X) * (a.Y + b.Y);
        }

        var length = normal.Length();
        return length > 0.0001f ? normal / length : Vector3.Zero;
    }

    private static bool TryEarClip(Vector2[] polygon, out List<Int3> triangles)
    {
        triangles = new List<Int3>();
        if (polygon.Length < 3)
            return false;

        // Signed area: positive winding uses positive cross product for ears
        var area = 0.0;
        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];
            area += a.X * (double)b.Y - b.X * (double)a.Y;
        }
        var counterClockwise = area > 0;

        var remaining = new List<int>(polygon.Length);
        for (var i = 0; i < polygon.Length; i++)
            remaining.Add(i);

        while (remaining.Count > 2)
        {
            var earFound = false;
            var n = remaining.Count;
            for (var i = 0; i < n; i++)
            {
                var aIndex = remaining[(i + n - 1) % n];
                var bIndex = remaining[i];
                var cIndex = remaining[(i + 1) % n];

                var cross = Cross(polygon[aIndex], polygon[bIndex], polygon[cIndex]);
                if (counterClockwise ? cross <= 0 : cross >= 0)
                    continue;

                var hasPointInside = false;
                for (var j = 0; j < n; j++)
                {
                    var index = remaining[j];
                    if (index == aIndex || index == bIndex || index == cIndex)
                        continue;

                    if (PointInTriangle(polygon[index], polygon[aIndex], polygon[bIndex], polygon[cIndex]))
                    {
                        hasPointInside = true;
                        break;
                    }
                }

                if (hasPointInside)
                    continue;

                triangles.Add(new Int3(aIndex, bIndex, cIndex));
                remaining.RemoveAt(i);
                earFound = true;
                break;
            }

            if (!earFound)
                return false;
        }

        return triangles.Count > 0;
    }

    private static float Cross(Vector2 a, Vector2 b, Vector2 c)
    {
        return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        var d1 = Sign(p, a, b);
        var d2 = Sign(p, b, c);
        var d3 = Sign(p, c, a);
        var hasNegative = d1 < 0 || d2 < 0 || d3 < 0;
        var hasPositive = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNegative && hasPositive);
    }

    private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);
    }

    private PbrVertex[] _vertexBufferData = Array.Empty<PbrVertex>();
    private Int3[] _indexBufferData = Array.Empty<Int3>();
    private Buffer _vertexBuffer;
    private Buffer _indexBuffer;
    private readonly BufferWithViews _vertexBufferWithViews = new();
    private readonly BufferWithViews _indexBufferWithViews = new();
    private readonly MeshBuffers _data = new();
    private BufferWithViews _stagingBuffer;

    [Input(Guid = "CCCAC1C9-926A-466A-B252-0D0E279102AE")]
    public readonly InputSlot<BufferWithViews> Points = new();

    [Input(Guid = "0F0D10D0-3EB0-4E12-8C06-E9DB4C000917")]
    public readonly InputSlot<int> PointsPerShape = new();
}