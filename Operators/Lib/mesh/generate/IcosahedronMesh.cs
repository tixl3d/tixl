using T3.Core.Rendering;
using T3.Core.Utils;

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
            var subdivisions = Subdivisions.GetValue(context).Clamp(0, 5);
            var spherical = Spherical.GetValue(context);
            var strength = Strength.GetValue(context);
            var uvMapMode = TexCoord.GetValue(context);
            var uvMapMode2 = TexCoord2.GetValue(context);
            var uvMapper = GetUvMapper(uvMapMode, subdivisions);
            var uvMapper2 = GetUvMapper(uvMapMode2, subdivisions);
            var shadingMode = Shading.GetValue(context);

            var yaw = rotation.Y.ToRadians();
            var pitch = rotation.X.ToRadians();
            var roll = rotation.Z.ToRadians();

            // Apply the icosahedron tilt adjustment
            var rollOffset = roll - _icosahedronTiltAngle;

            var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, rollOffset);

            // Generate mesh using flat shading structure
            var (vertices, triangles) = GenerateIcosahedron();

            if (subdivisions > 0)
                SubdivideMeshFlat(ref vertices, ref triangles, subdivisions, strength, spherical);

            if (uvMapper is Unwrapped unwrappedUv)
                unwrappedUv.Prepare(vertices, triangles);

            if (uvMapper2 is Unwrapped unwrappedUv2)
                unwrappedUv2.Prepare(vertices, triangles);


            // Calculate normals based on shading mode
            var normals = (shadingMode == (int)ShadingModes.Smoothed)
                ? CalculateSmoothNormals(vertices, triangles)
                : CalculateFlatNormals(vertices, triangles);

            // Debug: Log a few normals to compare
            /* if (vertices.Length >= 3)
             {
                 Log.Debug($"Shading: {(shadingMode == (int)ShadingModes.Smoothed ? "Smooth" : "Flat")}");
                 Log.Debug($"Normal[0]: {normals[0]}");
                 Log.Debug($"Normal[1]: {normals[1]}");
                 Log.Debug($"Normal[2]: {normals[2]}");
             }*/

            // Create buffers
            if (_vertexBufferData.Length != vertices.Length)
                _vertexBufferData = new PbrVertex[vertices.Length];

            if (_indexBufferData.Length != triangles.Length)
                _indexBufferData = new Int3[triangles.Length];

            // Transform vertices
            var centerVec = new Vector3(center.X, center.Y, center.Z);
            var offset = new Vector3(
                stretch.X * scale * pivot.X,
                stretch.Y * scale * pivot.Y,
                stretch.X * scale * pivot.Z
            );

            for (int i = 0; i < vertices.Length; i++)
            {
                var pos = new Vector3(
                    vertices[i].X * scale * stretch.X,
                    vertices[i].Y * scale * stretch.Y,
                    vertices[i].Z * scale * stretch.X
                );

                pos = Vector3.Transform(pos + offset, rotationMatrix) + centerVec;

                var uv = uvMapper.CalculateUV(vertices[i], normals[i], i % 3, i / 3); // Use i / 3 for triangle index
                var uv2 = uvMapper2.CalculateUV(vertices[i], normals[i], i % 3, i / 3);

                _vertexBufferData[i] = new PbrVertex
                {
                    Position = pos,
                    Normal = Vector3.TransformNormal(normals[i], rotationMatrix),
                    Tangent = Vector3.TransformNormal(Vector3.Cross(normals[i], Vector3.UnitY), rotationMatrix),
                    Bitangent = Vector3.TransformNormal(Vector3.Cross(normals[i], Vector3.UnitX), rotationMatrix),
                    Texcoord = uv,
                    Texcoord2 = uv2,
                    Selection = 1,
                };
            }

            // Fill index buffer
            for (var i = 0; i < triangles.Length; i++)
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
            Log.Error("Failed to create icosahedron mesh: " + e.Message);
        }
    }

    private static (Vector3[] vertices, Int3[] triangles) GenerateIcosahedron()
    {
        var baseVertices = new Vector3[12];
        baseVertices[0] = Vector3.Normalize(new Vector3(-1, phi, 0));
        baseVertices[1] = Vector3.Normalize(new Vector3(1, phi, 0));
        baseVertices[2] = Vector3.Normalize(new Vector3(-1, -phi, 0));
        baseVertices[3] = Vector3.Normalize(new Vector3(1, -phi, 0));
        baseVertices[4] = Vector3.Normalize(new Vector3(0, -1, phi));
        baseVertices[5] = Vector3.Normalize(new Vector3(0, 1, phi));
        baseVertices[6] = Vector3.Normalize(new Vector3(0, -1, -phi));
        baseVertices[7] = Vector3.Normalize(new Vector3(0, 1, -phi));
        baseVertices[8] = Vector3.Normalize(new Vector3(phi, 0, -1));
        baseVertices[9] = Vector3.Normalize(new Vector3(phi, 0, 1));
        baseVertices[10] = Vector3.Normalize(new Vector3(-phi, 0, -1));
        baseVertices[11] = Vector3.Normalize(new Vector3(-phi, 0, 1));

        // Original triangles (20 faces)
        var baseTriangles = new Int3[20];
        baseTriangles[0] = new Int3(0, 11, 5);
        baseTriangles[1] = new Int3(0, 5, 1);
        baseTriangles[2] = new Int3(0, 1, 7);
        baseTriangles[3] = new Int3(0, 7, 10);
        baseTriangles[4] = new Int3(0, 10, 11);
        baseTriangles[5] = new Int3(5, 11, 4);
        baseTriangles[6] = new Int3(1, 5, 9);
        baseTriangles[7] = new Int3(7, 1, 8);
        baseTriangles[8] = new Int3(10, 7, 6);
        baseTriangles[9] = new Int3(11, 10, 2);
        baseTriangles[10] = new Int3(3, 9, 4);
        baseTriangles[11] = new Int3(3, 8, 9);
        baseTriangles[12] = new Int3(3, 6, 8);
        baseTriangles[13] = new Int3(3, 2, 6);
        baseTriangles[14] = new Int3(3, 4, 2);
        baseTriangles[15] = new Int3(4, 9, 5);
        baseTriangles[16] = new Int3(9, 8, 1);
        baseTriangles[17] = new Int3(8, 6, 7);
        baseTriangles[18] = new Int3(6, 2, 10);
        baseTriangles[19] = new Int3(2, 4, 11);

        // Split vertices for flat shading (each triangle gets its own vertices)
        var vertices = new List<Vector3>();
        var triangles = new List<Int3>();

        foreach (var tri in baseTriangles)
        {
            var v0 = vertices.Count;
            vertices.Add(baseVertices[tri.X]);
            vertices.Add(baseVertices[tri.Y]);
            vertices.Add(baseVertices[tri.Z]);
            triangles.Add(new Int3(v0, v0 + 1, v0 + 2));
        }

        return (vertices.ToArray(), triangles.ToArray());
    }

    private static Vector3[] CalculateFlatNormals(Vector3[] vertices, Int3[] triangles)
    {
        var normals = new Vector3[vertices.Length];

        for (var i = 0; i < triangles.Length; i++)
        {
            var tri = triangles[i];
            Vector3 v1 = vertices[tri.X];
            Vector3 v2 = vertices[tri.Y];
            Vector3 v3 = vertices[tri.Z];

            Vector3 normal = Vector3.Normalize(Vector3.Cross(v2 - v1, v3 - v1));

            normals[tri.X] = normal;
            normals[tri.Y] = normal;
            normals[tri.Z] = normal;
        }

        return normals;
    }

    private static Vector3[] CalculateSmoothNormals(Vector3[] vertices, Int3[] triangles)
    {
        var normals = new Vector3[vertices.Length];

        // Group vertices by position to identify duplicates
        var positionToIndices = new Dictionary<Vector3, List<int>>(new Vector3EqualityComparer());
        for (var i = 0; i < vertices.Length; i++)
        {
            if (!positionToIndices.ContainsKey(vertices[i]))
                positionToIndices[vertices[i]] = new List<int>();
            positionToIndices[vertices[i]].Add(i);
        }

        // Calculate face normals and accumulate for each vertex position
        var positionNormals = new Dictionary<Vector3, Vector3>(new Vector3EqualityComparer());
        var positionTriangleCount = new Dictionary<Vector3, int>(new Vector3EqualityComparer());

        for (var i = 0; i < triangles.Length; i++)
        {
            var tri = triangles[i];
            var v1 = vertices[tri.X];
            var v2 = vertices[tri.Y];
            var v3 = vertices[tri.Z];

            var normal = Vector3.Normalize(Vector3.Cross(v2 - v1, v3 - v1));

            // Accumulate normal for each vertex position
            foreach (var v in new[] { v1, v2, v3 })
            {
                if (!positionNormals.ContainsKey(v))
                {
                    positionNormals[v] = Vector3.Zero;
                    positionTriangleCount[v] = 0;
                }
                positionNormals[v] += normal;
                positionTriangleCount[v]++;
            }
        }

        // Average normals per position
        foreach (var kvp in positionNormals)
        {
            var pos = kvp.Key;
            var normalSum = kvp.Value;
            var count = positionTriangleCount[pos];
            var averagedNormal = count > 0 ? Vector3.Normalize(normalSum / count) : Vector3.UnitY;

            // Assign averaged normal to all vertices at this position
            foreach (var index in positionToIndices[pos])
            {
                normals[index] = averagedNormal;
            }
        }

        return normals;
    }

    // Helper class for comparing Vector3 positions with a small tolerance
    private class Vector3EqualityComparer : IEqualityComparer<Vector3>
    {
        private const float Epsilon = 0.0001f;

        public bool Equals(Vector3 a, Vector3 b)
        {
            return Math.Abs(a.X - b.X) < Epsilon &&
                   Math.Abs(a.Y - b.Y) < Epsilon &&
                   Math.Abs(a.Z - b.Z) < Epsilon;
        }

        public int GetHashCode(Vector3 obj)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + obj.X.GetHashCode();
                hash = hash * 23 + obj.Y.GetHashCode();
                hash = hash * 23 + obj.Z.GetHashCode();
                return hash;
            }
        }
    }


    // Subdivide mesh for flat shading (each triangle gets its own vertices)
    private static void SubdivideMeshFlat(ref Vector3[] vertices, ref Int3[] triangles, int levels, float strength, bool spherical = true)
    {
        for (var i = 0; i < levels; i++)
        {
            var newTriangles = new List<Int3>(triangles.Length * 4);
            var newVertices = new List<Vector3>();

            for (var t = 0; t < triangles.Length; t++)
            {
                var v1 = vertices[triangles[t].X];
                var v2 = vertices[triangles[t].Y];
                var v3 = vertices[triangles[t].Z];

                // Calculate midpoints (linear interpolation)
                var a = (v1 + v2) * 0.5f;
                var b = (v2 + v3) * 0.5f;
                var c = (v3 + v1) * 0.5f;

                // Add all vertices (optionally normalize)
                var baseIndex = newVertices.Count;
                newVertices.Add(spherical ? MathUtils.Lerp(v1, Vector3.Normalize(v1), strength) : v1);
                newVertices.Add(spherical ? MathUtils.Lerp(a, Vector3.Normalize(a), strength) : a);
                newVertices.Add(spherical ? MathUtils.Lerp(c, Vector3.Normalize(c), strength) : c);

                newVertices.Add(spherical ? MathUtils.Lerp(v2, Vector3.Normalize(v2), strength) : v2);
                newVertices.Add(spherical ? MathUtils.Lerp(b, Vector3.Normalize(b), strength) : b);
                newVertices.Add(spherical ? MathUtils.Lerp(a, Vector3.Normalize(a), strength) : a);

                newVertices.Add(spherical ? MathUtils.Lerp(v3, Vector3.Normalize(v3), strength) : v3);
                newVertices.Add(spherical ? MathUtils.Lerp(c, Vector3.Normalize(c), strength) : c);
                newVertices.Add(spherical ? MathUtils.Lerp(b, Vector3.Normalize(b), strength) : b);

                newVertices.Add(spherical ? MathUtils.Lerp(a, Vector3.Normalize(a), strength) : a);
                newVertices.Add(spherical ? MathUtils.Lerp(b, Vector3.Normalize(b), strength) : b);
                newVertices.Add(spherical ? MathUtils.Lerp(c, Vector3.Normalize(c), strength) : c);

                // Add new triangles (same as before)
                newTriangles.Add(new Int3(baseIndex + 0, baseIndex + 1, baseIndex + 2));
                newTriangles.Add(new Int3(baseIndex + 3, baseIndex + 4, baseIndex + 5));
                newTriangles.Add(new Int3(baseIndex + 6, baseIndex + 7, baseIndex + 8));
                newTriangles.Add(new Int3(baseIndex + 9, baseIndex + 10, baseIndex + 11));
            }

            triangles = newTriangles.ToArray();
            vertices = newVertices.ToArray();
        }
    }



    private IUvMapper GetUvMapper(int uvMapMode, int level)
    {
        return uvMapMode switch
        {
            0 => new Faces(),           // Standard
            1 => new Unwrapped(),        // Unwrapped 
            2 => new Atlas(level),
            3 => new FacesSub(level),
            4 => new GridFacesSub(level),
            _ => new Faces()            // Default fallback
        };
    }
    // Interface for UV mapping strategies
    private interface IUvMapper
    {
        Vector2 CalculateUV(Vector3 vertex, Vector3 normal, int vertexIndex, int triangleIndex);
    }

    private class Faces : IUvMapper
    {
        // UV coordinates that repeat every 3 vertices


        public Vector2 CalculateUV(Vector3 vertex, Vector3 normal, int vertexIndex, int triangleIndex)
        {
            // The pattern repeats every 3 vertices, so we can use modulo
            int uvIndex = vertexIndex % 3;

            if (uvIndex >= 0 && uvIndex < _baseUvs.Length)
            {
                return _baseUvs[uvIndex];
            }

            // Fallback for unexpected cases
            Log.Warning($"Invalid UV index: {uvIndex}. Using fallback UV.");
            return new Vector2(0.5f, 0.5f);
        }
    }

    private class FacesSub(int subdivisionLevel) : IUvMapper
    {
        // Base UV coordinates for a single triangle face
        /* private static readonly Vector2[] _baseUvs = new Vector2[3]
         {
         new Vector2(0.5f, 1.0f),      // vertex 0 (center top)
         new Vector2(0.067f, 0.250f),  // vertex 1 (left bottom)
         new Vector2(0.933f, 0.250f)   // vertex 2 (right bottom)
         };*/

        private int _subdivisionLevel = subdivisionLevel;

        public Vector2 CalculateUV(Vector3 vertex, Vector3 normal, int vertexIndex, int triangleIndex)
        {
            if (_subdivisionLevel == 0)
            {
                // No subdivision - use base UVs
                var _uvIndex = vertexIndex % 3;
                return _baseUvs[_uvIndex];
            }

            // Calculate which original face this triangle belongs to
            var subTrianglesPerFace = (int)Math.Pow(4, _subdivisionLevel);
            var originalFaceIndex = triangleIndex / subTrianglesPerFace;
            originalFaceIndex = originalFaceIndex % 20; // Ensure we don't exceed 20 faces

            // Calculate which sub-triangle within the original face
            var subTriangleIndex = triangleIndex % subTrianglesPerFace;

            // Get the base UV for this vertex position in the triangle
            var uvIndex = vertexIndex % 3;
            var baseUV = _baseUvs[uvIndex];

            // Now we need to map this to the subdivided space
            // We'll use a recursive approach to find the correct sub-triangle position
            // Vector2 tessellatedUV = TessellateUVFace(baseUV, subTriangleIndex, _subdivisionLevel);
            return TessellateUV(baseUV, _baseUvs, subTriangleIndex, _subdivisionLevel);
            //return tessellatedUV;
        }
    }

    private class GridFacesSub(int subdivisionLevel) : IUvMapper // Look ma no llm! tbh I used Blender to get the base UVs for this
    {
        private readonly int _subdivisionLevel = subdivisionLevel.Clamp(0, 5);

        public Vector2 CalculateUV(Vector3 vertex, Vector3 normal, int vertexIndex, int triangleIndex)
        {
            /* if (_subdivisionLevel == 0)
             {
                 return GetNonSubdividedUV(triangleIndex, vertexIndex);
             }*/

            var subTrianglesPerFace = (int)Math.Pow(4, _subdivisionLevel);
            var originalFaceIndex = triangleIndex / subTrianglesPerFace;
            originalFaceIndex = originalFaceIndex % 20;
            var subTriangleIndex = triangleIndex % subTrianglesPerFace;

            var baseTriangleUvs = GetBaseTriangleUvs(originalFaceIndex);
            var uvIndex = vertexIndex % 3;
            var baseUV = baseTriangleUvs[uvIndex];

            //return TessellateUVFace(baseUV, subTriangleIndex, _subdivisionLevel);
            return TessellateUV(baseUV, baseTriangleUvs, subTriangleIndex, _subdivisionLevel);
        }

        private Vector2[] GetBaseTriangleUvs(int originalFaceIndex)
        {
            const float cellH = 1.0f / 5;
            const float cellV = 1.0f / 2;

            var groupIndex = originalFaceIndex / 5;
            var faceInGroup = originalFaceIndex % 5;
            var xOffset = faceInGroup * cellH;
            xOffset += (0.2f - 0.181819f) * 0.5f; // Center the UVs horizontally

            if (originalFaceIndex < 5) // First group (faces 0-4)
            {
                return
                [
                    new Vector2(0.09091f + xOffset, 0.907461f),    // Top center 
                    new Vector2(0.0f + xOffset, 0.75f ),       // Left vertex 
                    new Vector2(0.181819f + xOffset, 0.75f),   // Right vertex
                    
                ];
            }
            else if (originalFaceIndex < 10) // Second group (faces 5-9)
            {
                return
                [
                    new Vector2(0.181819f + xOffset, 0.75f),  // Right vertex
                    new Vector2(0.0f + xOffset, 0.75f),        // Left vertex
                    new Vector2(0.090911f + xOffset, 0.59254f  )    // Bottom center 
                ];
            }
            else if (originalFaceIndex < 15) // Third group (faces 10-14)
            {
                // Apply Y shift downward (-0.157461) and X shift (+cellHWidth/2)
                return
                [
                    new Vector2(0.09091f + xOffset, 0.907461f- cellV),   // Top center
                    new Vector2(0.0f + xOffset, 0.75f - cellV ),       // Left vertex 
                    new Vector2(0.181819f + xOffset, 0.75f- cellV),   // Right vertex
                    
                ];
            }
            else // Fourth group (faces 15-19)
            {
                // Apply Y shift downward (-0.157461) and X shift (+cellHWidth/2)
                return
                [
                    new Vector2(0.181819f + xOffset, 0.75f- cellV),  // Right vertex
                    new Vector2(0.0f + xOffset, 0.75f - cellV),        // Left vertex
                    new Vector2(0.090911f + xOffset, 0.59254f - cellV  )    // Bottom center 
                ];
            }
        }
    }

    private class Unwrapped : IUvMapper
    {
        private Dictionary<(int triangleIndex, int vertexIndex), Vector2> _fixedUvs;
        private HashSet<int> _flippedTriangles;

        // Precomputes UVs with seam fix, must be called before CalculateUV
        public void Prepare(Vector3[] vertices, Int3[] triangles)
        {
            _fixedUvs = new Dictionary<(int, int), Vector2>();
            _flippedTriangles = new HashSet<int>();

            for (var triIndex = 0; triIndex < triangles.Length; triIndex++)
            {
                var tri = triangles[triIndex];
                var indices = new[] { tri.X, tri.Y, tri.Z };
                var uvs = new Vector2[3];

                // Step 1: Compute spherical UVs
                for (var i = 0; i < 3; i++)
                {
                    // Match tilt rotation                     
                    var tilt = Matrix4x4.CreateFromYawPitchRoll(
                        yaw: 0f,
                        pitch: 0f,
                        roll: -_icosahedronTiltAngle
                    );

                    // Rotate vertex into UV-mapping space
                    var v = Vector3.Transform(vertices[indices[i]], tilt);

                    // Then compute spherical UVs from rotated point
                    var u = 0.5f + MathF.Atan2(v.Z, v.X) / (2 * MathF.PI);
                    var vCoord = 0.5f + MathF.Asin(v.Y) / MathF.PI;
                    if (u < 0f) u += 1f;
                    if (u >= 1f) u -= 1f;
                    uvs[i] = new Vector2(u, vCoord);
                }

                // Step 2: Seam fix
                var minU = MathF.Min(uvs[0].X, MathF.Min(uvs[1].X, uvs[2].X));
                var maxU = MathF.Max(uvs[0].X, MathF.Max(uvs[1].X, uvs[2].X));
                var wraps = (maxU - minU) > 0.5f;
                for (var i = 0; i < 3; i++)
                    if (wraps && uvs[i].X < 0.5f)
                        uvs[i].X += 1f;

                // Step 3: Check UV winding and flip if necessary
                var uvA = new Vector3(uvs[0], 0);
                var uvB = new Vector3(uvs[1], 0);
                var uvC = new Vector3(uvs[2], 0);
                var uvNormal = Vector3.Cross(uvB - uvA, uvC - uvA);
                var flipped = uvNormal.Z < 0;

                if (flipped)
                {
                    // Flip UVs
                    (uvs[1], uvs[2]) = (uvs[2], uvs[1]);
                    _flippedTriangles.Add(triIndex);
                }

                // Store fixed UVs with vertex mapping order preserved (adjusted if flipped)
                for (int i = 0; i < 3; i++)
                {
                    int fixedIndex = flipped ? (i == 1 ? 2 : i == 2 ? 1 : 0) : i;
                    _fixedUvs[(triIndex, i)] = uvs[fixedIndex];
                }
            }
        }


        public Vector2 CalculateUV(Vector3 vertex, Vector3 normal, int vertexIndex, int triangleIndex)
        {
            if (_fixedUvs.TryGetValue((triangleIndex, vertexIndex), out var uv))
                return uv * new Vector2(-1f, 1f) + new Vector2(1f, 0f);

            return new Vector2(0.5f, 0.5f); // fallback
        }
    }




    private class Atlas(int subdivisionLevel) : IUvMapper
    {
        private readonly int _subdivisionLevel = subdivisionLevel.Clamp(0, 5);
        private const float currentMaxY = 0.472382f;

        public Vector2 CalculateUV(Vector3 vertex, Vector3 normal, int vertexIndex, int triangleIndex)
        {
            /* if (_subdivisionLevel == 0)
             {
                 return GetNonSubdividedUV(triangleIndex, vertexIndex);
             }*/

            var subTrianglesPerFace = (int)Math.Pow(4, _subdivisionLevel);
            var originalFaceIndex = triangleIndex / subTrianglesPerFace;
            originalFaceIndex = originalFaceIndex % 20;
            var subTriangleIndex = triangleIndex % subTrianglesPerFace;

            var baseTriangleUvs = GetBaseTriangleUvs(originalFaceIndex);
            var uvIndex = vertexIndex % 3;
            var baseUV = baseTriangleUvs[uvIndex];

            //return TessellateUVFace(baseUV, subTriangleIndex, _subdivisionLevel);
            return TessellateUV(baseUV, baseTriangleUvs, subTriangleIndex, _subdivisionLevel);
        }

        private static Vector2[] GetBaseTriangleUvs(int originalFaceIndex)
        {
            const float cellWidth = 0.909091f / 5;
            const float yShift = 0.157461f;
            var groupIndex = originalFaceIndex / 5;
            var faceInGroup = originalFaceIndex % 5;
            var xOffset = faceInGroup * cellWidth;

            if (originalFaceIndex < 5) // First group (faces 0-4)
            {
                return
                [
            new Vector2(0.09091f + xOffset, 1.0f),                     // Top vertex (0.472382 normalized)
            new Vector2(0.0f + xOffset, 0.314921f / currentMaxY),      // Left vertex (~0.6667)
            new Vector2(0.181819f + xOffset, 0.314921f / currentMaxY)  // Right vertex
                ];
            }
            else if (originalFaceIndex < 10) // Second group (faces 5-9)
            {
                return
                [
            new Vector2(0.181819f + xOffset, 0.314921f / currentMaxY),  // Right vertex
            new Vector2(0.0f + xOffset, 0.314921f / currentMaxY),        // Left vertex
            new Vector2(0.090911f + xOffset, 0.157461f / currentMaxY)    // Bottom center (~0.3333)
                ];
            }
            else if (originalFaceIndex < 15) // Third group (faces 10-14)
            {
                // Apply Y shift downward (-0.157461) and X shift (+cellWidth/2)
                return
                [
            new Vector2(0.090911f + xOffset + cellWidth * 0.5f, (0.157461f - yShift) / currentMaxY),  // Bottom center (~0.0)
            new Vector2(0.181819f + xOffset + cellWidth * 0.5f, (0.314921f - yShift) / currentMaxY),  // Right vertex (~0.3333)
            new Vector2(0.0f + xOffset + cellWidth * 0.5f, (0.314921f - yShift) / currentMaxY)        // Left vertex
                ];
            }
            else // Fourth group (faces 15-19)
            {
                // Apply Y shift downward (-0.157461) and X shift (+cellWidth/2)
                return
                [
            new Vector2(0.0f + xOffset + cellWidth * 0.5f, (0.314921f - yShift) / currentMaxY),       // Left vertex (~0.3333)
            new Vector2(0.181819f + xOffset + cellWidth * 0.5f, (0.314921f - yShift) / currentMaxY),   // Right vertex
            new Vector2(0.09091f + xOffset + cellWidth * 0.5f, (0.472382f - yShift) / currentMaxY)    // Top center (~0.6667)
                ];
            }
        }
    }

    private static Vector2 TessellateUV(Vector2 baseUV, Vector2[] baseTriangleUvs, int subTriangleIndex, int subdivisionLevel)
    {
        if (subdivisionLevel == 0)
            return baseUV;

        var currentV0 = baseTriangleUvs[0];
        var currentV1 = baseTriangleUvs[1];
        var currentV2 = baseTriangleUvs[2];

        var currentIndex = subTriangleIndex;
        for (var level = subdivisionLevel; level > 0; level--)
        {
            var trianglesAtThisLevel = (int)Math.Pow(4, level - 1);
            var quadrant = currentIndex / trianglesAtThisLevel;
            currentIndex = currentIndex % trianglesAtThisLevel;

            var mid01 = (currentV0 + currentV1) * 0.5f;
            var mid12 = (currentV1 + currentV2) * 0.5f;
            var mid20 = (currentV2 + currentV0) * 0.5f;

            switch (quadrant)
            {
                case 0:
                    currentV1 = mid01;
                    currentV2 = mid20;
                    break;
                case 1:
                    currentV0 = currentV1;
                    currentV1 = mid12;
                    currentV2 = mid01;
                    break;
                case 2:
                    currentV0 = currentV2;
                    currentV1 = mid20;
                    currentV2 = mid12;
                    break;
                case 3:
                    currentV0 = mid01;
                    currentV1 = mid12;
                    currentV2 = mid20;
                    break;
            }
        }

        // Determine which vertex to return based on the original baseUV
        if (baseUV == baseTriangleUvs[0]) return currentV0;
        if (baseUV == baseTriangleUvs[1]) return currentV1;
        if (baseUV == baseTriangleUvs[2]) return currentV2;

        // For midpoints, return the interpolated value
        return (currentV0 + currentV1 + currentV2) / 3f;
    }

    private Buffer _vertexBuffer;
    private PbrVertex[] _vertexBufferData = new PbrVertex[0];
    private readonly BufferWithViews _vertexBufferWithViews = new();

    private Buffer _indexBuffer;
    private Int3[] _indexBufferData = new Int3[0];
    private readonly BufferWithViews _indexBufferWithViews = new();

    private readonly MeshBuffers _data = new();
    private static readonly float phi = (1f + MathF.Sqrt(5f)) / 2f; // Golden ratio, used in icosahedron generation

    private static readonly float _icosahedronTiltAngle = MathF.Atan(2f / (2f * phi));  // Pre-calculate the tilt angle

    // Base UV coordinates for a single triangle face
    private static readonly Vector2[] _baseUvs =
        [
        new Vector2(0.5f, 1.0f),    // vertex 0 (center top)
        new Vector2(0.067f, 0.250f), // vertex 11 (left bottom)
        new Vector2(0.933f, 0.250f)  // vertex 5 (right bottom)
        ];

    private enum UvModes
    {
        Faces,
        Unwrapped,
        Atlas,
        FacesSub,
        GridFacesSub,
    }

    private enum ShadingModes
    {
        Flat,
        Smoothed,
    }

    [Input(Guid = "2e8c23d8-01ac-4f53-b628-91d9ab094278")]
    public readonly InputSlot<int> Subdivisions = new();

    [Input(Guid = "32a77592-eaa1-43e8-b1ab-74b989ecbccd")]
    public readonly InputSlot<bool> Spherical = new();

    [Input(Guid = "63866397-F64E-486A-8C6D-862FFD3ED42E")]
    public readonly InputSlot<float> Strength = new();

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

    [Input(Guid = "08dd88b7-cd91-4f17-91d9-08de5b260e7a", MappedType = typeof(UvModes))]
    public readonly InputSlot<int> TexCoord2 = new();

    [Input(Guid = "7438A4CA-1FA7-48CF-AD85-0E7067AE54CC", MappedType = typeof(ShadingModes))]
    public readonly InputSlot<int> Shading = new();
}