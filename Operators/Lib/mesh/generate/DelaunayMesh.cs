using T3.Core.Rendering;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;
using DelaunatorSharp;
using T3.Core.DataTypes;
using System.Linq;
using System.Collections.Generic;


namespace Lib.mesh.generate;

[Guid("bf4daa46-ed0f-4a87-9ba1-93631b2ca29a")]
internal sealed class DelaunayMesh : Instance<DelaunayMesh>
{
    [Output(Guid = "6c85e367-f91c-4f3d-9d3d-e422a521e3a9")]
    public readonly Slot<MeshBuffers> Data = new();

    public DelaunayMesh()
    {
        Data.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        try
        {
            // Get the point list from input
            var pointList = List.GetValue(context);
            if (pointList == null || pointList.NumElements == 0)
            {
                Log.Warning("DelaunayMesh: No points in list");
                return;
            }

            // Cast to StructuredList<Point> to access TypedElements
            var typedPointList = pointList as StructuredList<Point>;
            if (typedPointList == null)
            {
                Log.Error("DelaunayMesh: List is not of type StructuredList<Point>");
                return;
            }

            var pointArray = typedPointList.TypedElements;

            if (pointArray.Length < 3)
            {
                Log.Warning("DelaunayMesh: Need at least 3 points for triangulation");
                return;
            }

            // Get transformation parameters
            var scale = Scale.GetValue(context);
            var stretch = Stretch.GetValue(context);
            var pivot = Pivot.GetValue(context);
            var rotation = Rotation.GetValue(context);
            var center = Center.GetValue(context);

            float yaw = rotation.Y.ToRadians();
            float pitch = rotation.X.ToRadians();
            float roll = rotation.Z.ToRadians();

            var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll);
            var center2 = new Vector3(center.X, center.Y, center.Z);

            // Convert Point array to IPoint array for Delaunator (only x,y coordinates)
            var delaunatorPoints = pointArray.Select(p => new DelaunatorSharp.Point(p.Position.X, p.Position.Y) as IPoint).ToArray();

            // Perform Delaunay triangulation
            var delaunay = new Delaunator(delaunatorPoints);

            // Get vertices and triangles count
            var verticesCount = pointArray.Length;
            var triangleCount = delaunay.Triangles.Length / 3;

            // Get max edge length parameter for alpha shape filtering
            var maxEdgeLength = MaxEdgeLength.GetValue(context);
            var useAlphaShape = maxEdgeLength > 0.0001f; // Only filter if max edge length is set

            // Calculate normals, tangent, bitangent for the mesh
            var normal = Vector3.TransformNormal(VectorT3.ForwardLH, rotationMatrix);
            var tangent = Vector3.TransformNormal(VectorT3.Right, rotationMatrix);
            var binormal = Vector3.TransformNormal(VectorT3.Up, rotationMatrix);

            // Calculate bounds for UV mapping
            var minX = pointArray.Min(p => p.Position.X);
            var maxX = pointArray.Max(p => p.Position.X);
            var minY = pointArray.Min(p => p.Position.Y);
            var maxY = pointArray.Max(p => p.Position.Y);
            var rangeX = maxX - minX;
            var rangeY = maxY - minY;

            // Avoid division by zero for UV calculation
            if (rangeX < 0.0001f) rangeX = 1.0f;
            if (rangeY < 0.0001f) rangeY = 1.0f;

            // Filter triangles if alpha shape is enabled
            var validTriangles = new List<Int3>();

            for (int i = 0; i < triangleCount; i++)
            {
                var idx0 = delaunay.Triangles[i * 3 + 0];
                var idx1 = delaunay.Triangles[i * 3 + 1];
                var idx2 = delaunay.Triangles[i * 3 + 2];

                if (useAlphaShape)
                {
                    // Calculate edge lengths in original point space
                    var p0 = pointArray[idx0].Position;
                    var p1 = pointArray[idx1].Position;
                    var p2 = pointArray[idx2].Position;

                    var edge01Length = Vector2.Distance(new Vector2(p0.X, p0.Y), new Vector2(p1.X, p1.Y));
                    var edge12Length = Vector2.Distance(new Vector2(p1.X, p1.Y), new Vector2(p2.X, p2.Y));
                    var edge20Length = Vector2.Distance(new Vector2(p2.X, p2.Y), new Vector2(p0.X, p0.Y));

                    // Only keep triangle if all edges are within max length
                    if (edge01Length <= maxEdgeLength && edge12Length <= maxEdgeLength && edge20Length <= maxEdgeLength)
                    {
                        validTriangles.Add(new Int3(idx0, idx2, idx1)); // Reversed winding order
                    }
                }
                else
                {
                    validTriangles.Add(new Int3(idx0, idx2, idx1)); // Reversed winding order
                }
            }

            var faceCount = validTriangles.Count;

            // Create buffers with correct sizes
            if (_vertexBufferData.Length != verticesCount)
                _vertexBufferData = new PbrVertex[verticesCount];

            if (_indexBufferData.Length != faceCount)
                _indexBufferData = new Int3[faceCount];

            // Create buffers
            if (_vertexBufferData.Length != verticesCount)
                _vertexBufferData = new PbrVertex[verticesCount];

            if (_indexBufferData.Length != faceCount)
                _indexBufferData = new Int3[faceCount];

            // Create vertices from input points
            for (int i = 0; i < verticesCount; i++)
            {
                var point = pointArray[i];
                var pos = point.Position;

                // Apply scale and stretch
                var scaledPos = new Vector3(
                    pos.X * scale * stretch.X,
                    pos.Y * scale * stretch.Y,
                    pos.Z * scale
                );

                // Apply pivot offset
                var offset = new Vector3(
                    -rangeX * scale * stretch.X * pivot.X,
                    -rangeY * scale * stretch.Y * pivot.Y,
                    0
                );

                // Calculate UV coordinates (normalized 0-1 based on point positions)
                var u = (pos.X - minX) / rangeX;
                var v = (pos.Y - minY) / rangeY;
                var uv = new Vector2(u, v);

                _vertexBufferData[i] = new PbrVertex
                {
                    Position = Vector3.TransformNormal(scaledPos + offset, rotationMatrix) + center2,
                    Normal = normal,
                    Tangent = tangent,
                    Bitangent = binormal,
                    Texcoord = uv,
                    Selection = 1,
                };
            }

            // Copy filtered triangles to index buffer
            for (int i = 0; i < faceCount; i++)
            {
                _indexBufferData[i] = validTriangles[i];
            }

            // Write Data to GPU buffers
            ResourceManager.SetupStructuredBuffer(_vertexBufferData, PbrVertex.Stride * verticesCount, PbrVertex.Stride, ref _vertexBuffer);
            ResourceManager.CreateStructuredBufferSrv(_vertexBuffer, ref _vertexBufferWithViews.Srv);
            ResourceManager.CreateStructuredBufferUav(_vertexBuffer, UnorderedAccessViewBufferFlags.None, ref _vertexBufferWithViews.Uav);
            _vertexBufferWithViews.Buffer = _vertexBuffer;

            const int stride = 3 * 4;
            ResourceManager.SetupStructuredBuffer(_indexBufferData, stride * faceCount, stride, ref _indexBuffer);
            ResourceManager.CreateStructuredBufferSrv(_indexBuffer, ref _indexBufferWithViews.Srv);
            ResourceManager.CreateStructuredBufferUav(_indexBuffer, UnorderedAccessViewBufferFlags.None, ref _indexBufferWithViews.Uav);
            _indexBufferWithViews.Buffer = _indexBuffer;

            _data.VertexBuffer = _vertexBufferWithViews;
            _data.IndicesBuffer = _indexBufferWithViews;
            Data.Value = _data;
            Data.DirtyFlag.Clear();
        }
        catch (Exception e)
        {
            Log.Error("Failed to create Delaunay mesh: " + e.Message);
        }
    }

    private Buffer _vertexBuffer;
    private PbrVertex[] _vertexBufferData = new PbrVertex[0];
    private readonly BufferWithViews _vertexBufferWithViews = new();

    private Buffer _indexBuffer;
    private Int3[] _indexBufferData = new Int3[0];
    private readonly BufferWithViews _indexBufferWithViews = new();

    private readonly MeshBuffers _data = new();

    [Input(Guid = "4784908f-ac12-47a0-9542-d65242acace3")]
    public readonly InputSlot<Vector2> Stretch = new();

    [Input(Guid = "f3c23e04-240c-46b0-8581-db682f49c898")]
    public readonly InputSlot<float> Scale = new();

    [Input(Guid = "58164bef-0da2-4d2f-b086-392b48826f6b")]
    public readonly InputSlot<Vector2> Pivot = new();

    [Input(Guid = "df51a336-a11e-466b-a312-0cecb9db08f1")]
    public readonly InputSlot<Vector3> Center = new();

    [Input(Guid = "50c16e0b-6f5a-408d-b3d1-5f402e4f402e")]
    public readonly InputSlot<Vector3> Rotation = new();

    [Input(Guid = "a5c4c31e-7b3c-4f3e-9d1f-8e2b4d5c6a7b")]
    public readonly InputSlot<float> MaxEdgeLength = new();

    [Input(Guid = "18FDDD63-DB79-4EE6-9A32-B90A5CEFF582")]
    public readonly InputSlot<StructuredList> List = new();

}