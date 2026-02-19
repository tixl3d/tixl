#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using T3.Core.Logging;
// ReSharper disable InconsistentNaming

// ReSharper disable RedundantNameQualifier

namespace T3.Core.Rendering;

public sealed class ObjMesh
{
    public readonly List<Vector3> Positions = [];
    public readonly List<Vector4> Colors = [];

    public readonly List<Vector3> Normals = [];
    public readonly List<Vector2> TexCoords = [];
    public readonly List<Face> Faces = [];
    public readonly List<Line> Lines = [];

    public static bool TryLoadFromFile(string? objFilePath, [NotNullWhen(true)]out ObjMesh? mesh)
    {
        mesh = null;
        try
        {
            if (string.IsNullOrEmpty(objFilePath) || !new FileInfo(objFilePath).Exists)
                return false;
        }
        catch (Exception e)
        {
            Log.Warning("Failed to load object path:" + objFilePath + "\\n" + e);
            return false;
        }

        mesh = new ObjMesh();
            
        var line = "";
        try
        {
            using var stream = new StreamReader(objFilePath);
            while ((line = stream.ReadLine()) != null)
            {
                var lineEntries = line.Split(' ');
                switch (lineEntries[0])
                {
                    case "v":
                    {
                        var x = float.Parse(lineEntries[1], CultureInfo.InvariantCulture);
                        var y = float.Parse(lineEntries[2], CultureInfo.InvariantCulture);
                        var z = float.Parse(lineEntries[3], CultureInfo.InvariantCulture);
                        mesh.Positions.Add(new Vector3(x, y, z));
                        var vertexIncludesColor = lineEntries.Length == 7;
                        if (vertexIncludesColor)
                        {
                            var r = float.Parse(lineEntries[4], CultureInfo.InvariantCulture);
                            var g = float.Parse(lineEntries[5], CultureInfo.InvariantCulture);
                            var b = float.Parse(lineEntries[6], CultureInfo.InvariantCulture);
                            mesh.Colors.Add(new Vector4(r, g, b, 1));
                        }

                        break;
                    }
                    case "vt":
                    {
                        var u = float.Parse(lineEntries[1], CultureInfo.InvariantCulture);
                        var v = float.Parse(lineEntries[2], CultureInfo.InvariantCulture);
                        mesh.TexCoords.Add(new Vector2(u, v));
                        break;
                    }
                    case "vn":
                    {
                        var x = float.Parse(lineEntries[1], CultureInfo.InvariantCulture);
                        var y = float.Parse(lineEntries[2], CultureInfo.InvariantCulture);
                        var z = float.Parse(lineEntries[3], CultureInfo.InvariantCulture);
                        mesh.Normals.Add(new Vector3(x, y, z));
                        break;
                    }
                    case "f":
                    {
                        SplitFaceIndices(lineEntries[1], out var v0V, out var v0T, out var v0N);
                        SplitFaceIndices(lineEntries[2], out var v1V, out var v1T, out var v1N);
                        SplitFaceIndices(lineEntries[3], out var v2V, out var v2T, out var v2N);
                        mesh.Faces.Add(new Face(
                                                v0V, v0N, v0T,
                                                v1V, v1N, v1T,
                                                v2V, v2N, v2T));

                        if (lineEntries.Length > 4)
                        {
                            SplitFaceIndices(lineEntries[4], out var v3V, out var v3T, out var v3N);
                            mesh.Faces.Add(new Face(
                                                    v0V, v0N, v0T,
                                                    v2V, v2N, v2T,
                                                    v3V, v3N, v3T
                                                   ));
                        }

                        break;
                    }
                    case "l":
                    {
                        mesh.Lines.Add(new Line(int.Parse(lineEntries[1], CultureInfo.InvariantCulture) - 1,
                                                int.Parse(lineEntries[2], CultureInfo.InvariantCulture) - 1
                                               ));
                        break;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load obj cloud:{e.Message} '{line}'");
            return false;
        }

        if (mesh.Colors.Count > 0 && mesh.Colors.Count != mesh.Positions.Count)
        {
            Log.Warning("Optional OBJ color information not defined for all vertices");
        }

        return mesh.DistinctDistinctVertices.Count != 0;
    }

    private static void SplitFaceIndices(string v0, out int positionIndex, out int textureIndex, out int normalIndex)
    {
        var v0Entries = v0.Split('/');
        positionIndex = int.Parse(v0Entries[0], CultureInfo.InvariantCulture) - 1;
        if (string.IsNullOrEmpty(v0Entries[1]))
        {
            textureIndex = 0;
            normalIndex = int.Parse(v0Entries[2], CultureInfo.InvariantCulture) - 1;
        }
        else
        {
            textureIndex = int.Parse(v0Entries[1], CultureInfo.InvariantCulture) - 1;
            normalIndex = int.Parse(v0Entries[2], CultureInfo.InvariantCulture) - 1;
        }
    }

    public struct Face
    {
        internal Face(int v0, int v0N, int v0T, int v1, int v1N, int v1T, int v2, int v2N, int v2T)
        {
            V0 = v0;
            V0n = v0N;
            V0t = v0T;

            V1 = v1;
            V1n = v1N;
            V1t = v1T;

            V2 = v2;
            V2n = v2N;
            V2t = v2T;
        }

        public readonly int V0;
        public readonly int V0n;
        public  int V0t;
        public readonly int V1;
        public readonly int V1n;
        public  int V1t;
        public readonly int V2;
        public readonly int V2n;
        public  int V2t;
    }

    public struct Line
    {
        internal Line(int v0, int v2)
        {
            V0 = v0;
            V2 = v2;
        }

        public readonly int V0;
        public readonly int V2;
    }

    #region joining vertices
    public List<Vertex> DistinctDistinctVertices
    {
        get
        {
            _distinctVertices ??= InitializeVertices();
            return _distinctVertices;  
        }
    }

    public int GetVertexIndex(int positionIndex, int normalIndex, int textureCoordsIndex)
    {
        var hash = Vertex.GetHashForIndices(positionIndex, normalIndex, textureCoordsIndex);

        if (_vertexIndicesByHash.TryGetValue(hash, out var index))
        {
            return index;
        }

        return -1;
    }

    public readonly struct Vertex
    {
        public readonly int PositionIndex;
        public readonly int NormalIndex;
        public readonly int TextureCoordsIndex;

        internal Vertex(int positionIndex, int normalIndex, int textureCoordsIndex)
        {
            PositionIndex = positionIndex;
            NormalIndex = normalIndex;
            TextureCoordsIndex = textureCoordsIndex;
        }

        /***
         * The hash is done by bit-shifting. This results in a maximum
         * vertex count of  64/3 bit =  2^21 = 2,097,152 vertices (!)
         */
        internal static long GetHashForIndices(int pos, int normal, int textureCoords)
        {
            return (long)pos << 42 | (long)normal << 21 | (long)textureCoords;
        }
    }

    /// <summary>
    /// Tooll's mesh format uses vertex indices that combine all required information.
    /// This means that we have to split the OBJ-vertices if they use different normals or UVs.
    /// We do this by iterating over all face vertices, and generating a hash for face-, normal- and uv-index.
    /// If the hash already exists, we reuse and thus "merge the vertex" I.e. use it for different faces.
    /// </summary>
    private List<Vertex> InitializeVertices()
    {
        if (TexCoords.Count == 0)
        {
            TexCoords.Add(Vector2.Zero);
        }

        // compute fallback UVs as basis for TBN calculation...
        for (var index = 0; index < Faces.Count; index++)
        {
            var face = Faces[index];
            var uv0 = TexCoords[face.V0t];
            var uv1 = TexCoords[face.V1t];
            var uv2 = TexCoords[face.V2t];
            var needToGenerateUv = uv0 == Vector2.Zero && uv1 == Vector2.Zero && uv2 == Vector2.Zero;
            if (!needToGenerateUv)
                continue;
                
            var n0 = Normals[face.V0n];
            var n1 = Normals[face.V1n];
            var n2 = Normals[face.V2n];
                
            var p0 = Positions[face.V0];
            var p1 = Positions[face.V1];
            var p2 = Positions[face.V2];                
                
            uv0 = GetUvFromPositionAndNormal(p0, n0);
            face.V0t = TexCoords.Count;
            TexCoords.Add(uv0);
                
            uv1 = GetUvFromPositionAndNormal(p1, n1);
            face.V1t = TexCoords.Count;
            TexCoords.Add(uv1);
                
            uv2 = GetUvFromPositionAndNormal(p2, n2);
            face.V2t = TexCoords.Count;
            TexCoords.Add(uv2);
                
            Faces[index] = face;
        }

        _distinctVertices = [];
        for (var faceIndex = 0; faceIndex < Faces.Count; faceIndex++)
        {
            if (faceIndex >= Faces.Count)
            {
                Log.Warning($"Skipping out of range {faceIndex} >= {Faces.Count} face index");
                faceIndex = 0;
            }
                    
            var face = Faces[faceIndex];

            SortInMergedVertex(face.V0, face.V0n, face.V0t, faceIndex);
            SortInMergedVertex(face.V1, face.V1n, face.V1t, faceIndex);
            SortInMergedVertex(face.V2, face.V2n, face.V2t, faceIndex);
        }

        return _distinctVertices;
    }

    private int SortInMergedVertex(int posIndex, int normalIndex, int texCoordIndex, int faceIndex)
    {
        if(_distinctVertices == null)
            return 0;
        
        var face = Faces[faceIndex];
        var vertHash = Vertex.GetHashForIndices(posIndex, normalIndex, texCoordIndex);

        if (_vertexIndicesByHash.TryGetValue(vertHash, out var index))
        {
            return index;
        }
            
        var p0 = Positions[face.V0];
        var p1 = Positions[face.V1];
        var p2 = Positions[face.V2];

        var uv0 = TexCoords[face.V0t];
        var uv1 = TexCoords[face.V1t];
        var uv2 = TexCoords[face.V2t];


        MeshUtils.CalcTBNSpace(p0: p0, uv0: uv0,
                               p1: p1, uv1: uv1,
                               p2: p2, uv2: uv2,
                               normal: Normals[normalIndex],
                               tangent: out var tangent,
                               bitangent: out var bitangent);

        var newIndex = _distinctVertices.Count;
        var vert = new Vertex(posIndex, normalIndex, texCoordIndex);

        _vertexIndicesByHash[vertHash] = newIndex;
        VertexBinormals.Add(bitangent);
        VertexTangents.Add(tangent);
        _distinctVertices.Add(vert);
        return newIndex;
    }

    private static Vector2 GetUvFromPositionAndNormal(Vector3 pos, Vector3 normal)
    {
        var ax = MathF.Abs(normal.X);
        var ay = MathF.Abs(normal.Y);
        var az = MathF.Abs(normal.Z);

        if (ax > ay)
        {
            return ax > az
                       ? new Vector2(pos.Y, pos.Z)
                       : new Vector2(pos.X, pos.Y);
        }

        return ay > az
                   ? new Vector2(pos.X, pos.Z)
                   : new Vector2(pos.X, pos.Y);
    }

    /// <summary>
    /// Vertex sorting is implement through an index look-up table
    /// </summary>
    public void UpdateVertexSorting(SortDirections sortDirection)
    {
        if (_distinctVertices == null || _distinctVertices.Count == 0)
        {
            SortedVertexIndices = [];
            return;
        }
            
        SortedVertexIndices = Enumerable.Range(0, _distinctVertices.Count).ToList();
        
        switch (sortDirection)
        {
            case SortDirections.XForward:
                SortedVertexIndices
                   .Sort((v1, v2) => Positions[_distinctVertices[v1].PositionIndex].X.CompareTo(Positions[_distinctVertices[v2].PositionIndex].X));
                break;
            case SortDirections.XBackwards:
                SortedVertexIndices.Sort((v1, v2) => Positions[_distinctVertices[v2].PositionIndex].X
                                                                                                   .CompareTo(Positions[_distinctVertices[v1].PositionIndex]
                                                                                                       .X));
                break;
            case SortDirections.YForward:
                SortedVertexIndices.Sort((v1, v2) => Positions[_distinctVertices[v1].PositionIndex].Y
                                                                                                   .CompareTo(Positions[_distinctVertices[v2].PositionIndex]
                                                                                                       .Y));
                break;
            case SortDirections.YBackwards:
                SortedVertexIndices.Sort((v1, v2) => Positions[_distinctVertices[v2].PositionIndex].Y
                                                                                                   .CompareTo(Positions[_distinctVertices[v1].PositionIndex]
                                                                                                       .Y));
                break;
            case SortDirections.ZForward:
                SortedVertexIndices.Sort((v1, v2) => Positions[_distinctVertices[v1].PositionIndex].Z
                                                                                                   .CompareTo(Positions[_distinctVertices[v2].PositionIndex]
                                                                                                       .Z));
                break;
            case SortDirections.ZBackwards:
                SortedVertexIndices.Sort((v1, v2) => Positions[_distinctVertices[v2].PositionIndex].Z
                                                                                                   .CompareTo(Positions[_distinctVertices[v1].PositionIndex]
                                                                                                       .Z));
                break;
            case SortDirections.Ignore:
                break;
        }
    }

    public enum SortDirections
    {
        XForward,
        XBackwards,
        YForward,
        YBackwards,
        ZForward,
        ZBackwards,
        Ignore,
    }

    private List<Vertex>? _distinctVertices;
    public readonly List<Vector3> VertexTangents = [];
    public readonly List<Vector3> VertexBinormals = [];
    public List<int> SortedVertexIndices=[];
    private readonly Dictionary<long, int> _vertexIndicesByHash = new();
    #endregion
}