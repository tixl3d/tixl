using T3.Core.Rendering;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;

namespace Lib.mesh.generate;
[Guid("E0CEAD3C-E19C-4726-8B5C-A9FEFBF96AB9")]
internal sealed class IcosahedronMesh : Instance<IcosahedronMesh>
{
    [Output(Guid = "9c86f704-a28f-4d2a-b7c0-15648f982463")] 
    public readonly Slot<MeshBuffers> Data = new();

    public IcosahedronMesh()
    {
        Data.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        try
        {
            var scale = Scale.GetValue(context);
            var stretch = Stretch.GetValue(context);
            var pivot = Pivot.GetValue(context);
            var rotation = Rotation.GetValue(context);
            var center = Center.GetValue(context);
            var subdivisions = Subdivisions.GetValue(context).Clamp(0, 5); // Limit subdivisions for performance
            var uvMapMode = TexCoord.GetValue(context);
            IUvMapper uvMapper = GetUvMapper(uvMapMode);

            float yaw = rotation.Y.ToRadians();
            float pitch = rotation.X.ToRadians();
            float roll = rotation.Z.ToRadians();
            var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll);

            // Generate base icosahedron vertices
            var (vertices, triangles) = GenerateIcosahedron();

            // Apply subdivisions
            if (subdivisions > 0)
            {
                SubdivideMesh(ref vertices, ref triangles, subdivisions);
            }

            // Calculate normals (after subdivision)
            var normals = CalculateNormals(vertices, triangles);

            // Create buffers
            if (_vertexBufferData.Length != vertices.Length)
                _vertexBufferData = new PbrVertex[vertices.Length];

            if (_indexBufferData.Length != triangles.Length)
                _indexBufferData = new Int3[triangles.Length];

            // Transform vertices
            var centerVec = new Vector3(center.X, center.Y, center.Z);
            var offset = new Vector3(
                 stretch.X * scale * (pivot.X),
                 stretch.Y * scale * (pivot.Y),
                 stretch.X * scale * (pivot.Z) // Use pivot.Z instead of pivot.X
            );

            for (int i = 0; i < vertices.Length; i++)
            {
                // Apply scale and stretch
                var pos = new Vector3(
                    vertices[i].X * scale * stretch.X,
                    vertices[i].Y * scale * stretch.Y,
                    vertices[i].Z * scale * stretch.X // Or create stretch.Z if you want independent Z scaling
                );

                // Apply rotation and offset
                pos = Vector3.Transform(pos + offset, rotationMatrix) + centerVec;
                var uv = uvMapper.CalculateUV(vertices[i], normals[i]);
                _vertexBufferData[i] = new PbrVertex
                {
                    Position = pos,
                    Normal = Vector3.TransformNormal(normals[i], rotationMatrix),
                    Tangent = Vector3.TransformNormal(Vector3.Cross(normals[i], Vector3.UnitY), rotationMatrix),
                    Bitangent = Vector3.TransformNormal(Vector3.Cross(normals[i], Vector3.UnitX), rotationMatrix),
                    Texcoord = uv,
                    Selection = 1,
                };
            }

            // Fill index buffer
            for (int i = 0; i < triangles.Length; i++)
            {
                _indexBufferData[i] = new Int3(
                    triangles[i].X,
                    triangles[i].Y,
                    triangles[i].Z
                );
            }

            // Write Data
            ResourceManager.SetupStructuredBuffer(_vertexBufferData, PbrVertex.Stride * vertices.Length, PbrVertex.Stride, ref _vertexBuffer);
            ResourceManager.CreateStructuredBufferSrv(_vertexBuffer, ref _vertexBufferWithViews.Srv);
            ResourceManager.CreateStructuredBufferUav(_vertexBuffer, UnorderedAccessViewBufferFlags.None, ref _vertexBufferWithViews.Uav);
            _vertexBufferWithViews.Buffer = _vertexBuffer;

            const int stride = 3 * 4;
            ResourceManager.SetupStructuredBuffer(_indexBufferData, stride * triangles.Length, stride, ref _indexBuffer);
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
            Log.Error("Failed to create icosahedron mesh:" + e.Message);
        }
    }


    private static (Vector3[] vertices, Int3[] triangles) GenerateIcosahedron()
    {
        var vertices = new Vector3[12];
        // Create and normalize vertices
        vertices[0] = Vector3.Normalize(new Vector3(-1, t, 0));
        vertices[1] = Vector3.Normalize(new Vector3(1, t, 0));
        vertices[2] = Vector3.Normalize(new Vector3(-1, -t, 0));
        vertices[3] = Vector3.Normalize(new Vector3(1, -t, 0));

        vertices[4] = Vector3.Normalize(new Vector3(0, -1, t));
        vertices[5] = Vector3.Normalize(new Vector3(0, 1, t));
        vertices[6] = Vector3.Normalize(new Vector3(0, -1, -t));
        vertices[7] = Vector3.Normalize(new Vector3(0, 1, -t));

        vertices[8] = Vector3.Normalize(new Vector3(t, 0, -1));
        vertices[9] = Vector3.Normalize(new Vector3(t, 0, 1));
        vertices[10] = Vector3.Normalize(new Vector3(-t, 0, -1));
        vertices[11] = Vector3.Normalize(new Vector3(-t, 0, 1));

        var triangles = new Int3[20];
        // 5 faces around point 0
        triangles[0] = new Int3(0, 11, 5);
        triangles[1] = new Int3(0, 5, 1);
        triangles[2] = new Int3(0, 1, 7);
        triangles[3] = new Int3(0, 7, 10);
        triangles[4] = new Int3(0, 10, 11);

        // 5 adjacent faces
        triangles[5] = new Int3(1, 5, 9);
        triangles[6] = new Int3(5, 11, 4);
        triangles[7] = new Int3(11, 10, 2);
        triangles[8] = new Int3(10, 7, 6);
        triangles[9] = new Int3(7, 1, 8);

        // 5 faces around point 3
        triangles[10] = new Int3(3, 9, 4);
        triangles[11] = new Int3(3, 4, 2);
        triangles[12] = new Int3(3, 2, 6);
        triangles[13] = new Int3(3, 6, 8);
        triangles[14] = new Int3(3, 8, 9);

        // 5 adjacent faces
        triangles[15] = new Int3(4, 9, 5);
        triangles[16] = new Int3(2, 4, 11);
        triangles[17] = new Int3(6, 2, 10);
        triangles[18] = new Int3(8, 6, 7);
        triangles[19] = new Int3(9, 8, 1);

        return (vertices, triangles);
    }

    // Subdivide mesh using loop subdivision
    private static void SubdivideMesh(ref Vector3[] vertices, ref Int3[] triangles, int levels)
    {
        for (int i = 0; i < levels; i++)
        {
            var newTriangles = new List<Int3>(triangles.Length * 4);
            var newVertices = new List<Vector3>(vertices);
            var edgeMap = new Dictionary<long, int>();

            for (int t = 0; t < triangles.Length; t++)
            {
                int v1 = triangles[t].X;
                int v2 = triangles[t].Y;
                int v3 = triangles[t].Z;

                // Get or create edge vertices
                int a = GetEdgePoint(v1, v2, ref vertices, ref newVertices, ref edgeMap);
                int b = GetEdgePoint(v2, v3, ref vertices, ref newVertices, ref edgeMap);
                int c = GetEdgePoint(v3, v1, ref vertices, ref newVertices, ref edgeMap);

                // Add new triangles
                newTriangles.Add(new Int3(v1, a, c));
                newTriangles.Add(new Int3(v2, b, a));
                newTriangles.Add(new Int3(v3, c, b));
                newTriangles.Add(new Int3(a, b, c));
            }

            // Project all vertices to sphere (normalize)
            for (int v = 0; v < newVertices.Count; v++)
            {
                newVertices[v] = Vector3.Normalize(newVertices[v]);
            }

            triangles = newTriangles.ToArray();
            vertices = newVertices.ToArray();
        }
    }



    private static int GetEdgePoint(int a, int b, ref Vector3[] vertices, ref List<Vector3> newVertices, ref Dictionary<long, int> edgeMap)
    {
        // Create a unique key for the edge (order-independent)
        long key = ((long)Math.Min(a, b) << 32) + Math.Max(a, b);

        if (edgeMap.TryGetValue(key, out int index))
        {
            return index;
        }

        // Create new vertex at midpoint
        Vector3 newVertex = (vertices[a] + vertices[b]) * 0.5f;

        int newIndex = newVertices.Count;
        newVertices.Add(newVertex);
        edgeMap[key] = newIndex;

        return newIndex;
    }

   

    private static Vector3[] CalculateNormals(Vector3[] vertices, Int3[] triangles)
    {
        var normals = new Vector3[vertices.Length];

        foreach (var tri in triangles)
        {
            Vector3 v1 = vertices[tri.X];
            Vector3 v2 = vertices[tri.Y];
            Vector3 v3 = vertices[tri.Z];

            Vector3 normal = Vector3.Cross(v2 - v1, v3 - v1);
            normal = Vector3.Normalize(normal); // Normalize triangle normal

            normals[tri.X] += normal;
            normals[tri.Y] += normal;
            normals[tri.Z] += normal;
        }

        // Actually normalize all normals
        for (int i = 0; i < normals.Length; i++)
        {
            if (normals[i].LengthSquared() > 0)
                normals[i] = Vector3.Normalize(normals[i]);
        }

        return normals;
    }

    private IUvMapper GetUvMapper(int uvMapMode)
    {
        return uvMapMode switch
        {
            0 => new Faces(),           // Standard
            1 => new Unwrapped(),        // Unwrapped 
            _ => new Faces()            // Default fallback
        };
    }
    // Interface for UV mapping strategies
    private interface IUvMapper
    {
        Vector2 CalculateUV(Vector3 vertex, Vector3 normal);
    }

    private class Faces : IUvMapper
    {
        public Vector2 CalculateUV(Vector3 vertex, Vector3 normal)
        {
           
            return new Vector2(0.5f, 0.5f);
        }
    }

    private class Unwrapped : IUvMapper
    {
        public Vector2 CalculateUV(Vector3 vertex, Vector3 normal)
        {
            // Blender-style spherical unwrapping
            float u = 0.5f + MathF.Atan2(vertex.Z, vertex.X) / (2 * MathF.PI);
            float v = 0.5f + MathF.Asin(vertex.Y) / MathF.PI;
            return new Vector2(u, v);
        }
    }

    private void GenerateFaceUVMesh(Vector3[] vertices, Int3[] triangles, float scale, Vector2 stretch, Vector3 pivot, Vector3 rotation, Vector3 center)
    {
        var faceUvs = new Vector2[]
        {
        new Vector2(0.5f, 0.0f),   // Top
        new Vector2(1.0f, 1.0f),   // Bottom right
        new Vector2(0.0f, 1.0f),   // Bottom left
        };

        var centerVec = new Vector3(center.X, center.Y, center.Z);
        var offset = new Vector3(
            stretch.X * scale * (pivot.X - 0.5f),
            stretch.Y * scale * (pivot.Y - 0.5f),
            stretch.X * scale * (pivot.Z - 0.5f) // or stretch.Z if you prefer
        );

        float yaw = rotation.Y.ToRadians();
        float pitch = rotation.X.ToRadians();
        float roll = rotation.Z.ToRadians();
        var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll);

        var vertexList = new List<PbrVertex>();
        var indexList = new List<Int3>();

        for (int i = 0; i < triangles.Length; i++)
        {
            var tri = triangles[i];

            for (int j = 0; j < 3; j++)
            {
                int index = (j == 0) ? tri.X : (j == 1) ? tri.Y : tri.Z;
                var pos = Vector3.Normalize(vertices[index]);

                var transformedPos = new Vector3(
                    pos.X * scale * stretch.X,
                    pos.Y * scale * stretch.Y,
                    pos.Z * scale * stretch.X // replace with stretch.Z if needed
                );

                transformedPos = Vector3.Transform(transformedPos + offset, rotationMatrix) + centerVec;

                var normal = Vector3.TransformNormal(pos, rotationMatrix);
                var tangent = Vector3.Cross(normal, Vector3.UnitY);
                var bitangent = Vector3.Cross(normal, Vector3.UnitX);

                vertexList.Add(new PbrVertex
                {
                    Position = transformedPos,
                    Normal = normal,
                    Tangent = tangent,
                    Bitangent = bitangent,
                    Texcoord = faceUvs[j],
                    Selection = 1
                });
            }

            int baseIndex = i * 3;
            indexList.Add(new Int3(baseIndex, baseIndex + 1, baseIndex + 2));
        }

        _vertexBufferData = vertexList.ToArray();
        _indexBufferData = indexList.ToArray();
    }



    private Buffer _vertexBuffer;
    private PbrVertex[] _vertexBufferData = new PbrVertex[0];
    private readonly BufferWithViews _vertexBufferWithViews = new();

    private Buffer _indexBuffer;
    private Int3[] _indexBufferData = new Int3[0];
    private readonly BufferWithViews _indexBufferWithViews = new();

    private readonly MeshBuffers _data = new();
    private static readonly float t = (1f + MathF.Sqrt(5f)) / 2f; // Golden ratio, used in icosahedron generation

    private enum UvModes
    {
        Faces,
        Unwrapped,
    }

    [Input(Guid = "2e8c23d8-01ac-4f53-b628-91d9ab094278")] 
    public readonly InputSlot<int> Subdivisions = new();
    
    [Input(Guid = "e062431e-0741-446d-ace9-e7e91080ed9f")] 
    public readonly InputSlot<Vector2> Stretch = new();
    
    [Input(Guid = "bba90ae7-689f-41d3-8a48-4f1cdb42adab")] 
    public readonly InputSlot<float> Scale = new();
    
    [Input(Guid = "486c1717-20cf-4cf9-951e-cedd51c88262")] 
    public readonly InputSlot<Vector3> Pivot = new();
    
    [Input(Guid = "bbeccca7-9e1c-4702-bbd4-1cf0c9409354")] 
    public readonly InputSlot<Vector3> Center = new();
    
    [Input(Guid = "96D161DA-F459-427C-BE67-E8F1B47D233D")] 
    public readonly InputSlot<Vector3> Rotation = new();
   
    [Input(Guid = "FFD87531-8B82-4F31-9AA9-8459F92A4798", MappedType = typeof(UvModes))]
    public readonly InputSlot<int> TexCoord = new();
}