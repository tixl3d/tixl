#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;

namespace Lib.Utils;

/// <summary>
/// Point-in-mesh test by ray parity along +X. Triangles are bucketed in a 2D grid
/// over the YZ plane, so a query only visits the triangles whose YZ footprint
/// contains the ray instead of the whole mesh - dense scans stay interactive.
/// Build once per mesh version and reuse for all queries.
/// </summary>
internal sealed class MeshInsideTester
{
    public MeshInsideTester(MeshGeometry geometry)
    {
        var positions = geometry.Positions;
        var offsets = geometry.FaceCornerOffsets;
        var corners = geometry.CornerPointIndices;

        Min = new Vector3(float.MaxValue);
        Max = new Vector3(float.MinValue);
        foreach (var p in positions)
        {
            Min = Vector3.Min(Min, p);
            Max = Vector3.Max(Max, p);
        }

        // Fan-triangulate once into flat arrays
        var triangleCount = geometry.GetTriangleCount();
        _a = new Vector3[triangleCount];
        _b = new Vector3[triangleCount];
        _c = new Vector3[triangleCount];
        var t = 0;
        for (var faceIndex = 0; faceIndex < geometry.FaceCount; faceIndex++)
        {
            var start = offsets[faceIndex];
            var end = offsets[faceIndex + 1];
            for (var c = start + 2; c < end; c++)
            {
                _a[t] = positions[corners[start]];
                _b[t] = positions[corners[c - 1]];
                _c[t] = positions[corners[c]];
                t++;
            }
        }

        _resolution = Math.Clamp((int)MathF.Ceiling(MathF.Sqrt(triangleCount / 8f)), 1, 256);
        var extent = Max - Min;
        _cellSizeY = MathF.Max(extent.Y, 1e-6f) / _resolution;
        _cellSizeZ = MathF.Max(extent.Z, 1e-6f) / _resolution;

        var cellLists = new List<int>[_resolution * _resolution];
        for (var i = 0; i < triangleCount; i++)
        {
            var minY = MathF.Min(_a[i].Y, MathF.Min(_b[i].Y, _c[i].Y));
            var maxY = MathF.Max(_a[i].Y, MathF.Max(_b[i].Y, _c[i].Y));
            var minZ = MathF.Min(_a[i].Z, MathF.Min(_b[i].Z, _c[i].Z));
            var maxZ = MathF.Max(_a[i].Z, MathF.Max(_b[i].Z, _c[i].Z));
            var y0 = ToCellY(minY);
            var y1 = ToCellY(maxY);
            var z0 = ToCellZ(minZ);
            var z1 = ToCellZ(maxZ);
            for (var z = z0; z <= z1; z++)
            for (var y = y0; y <= y1; y++)
            {
                (cellLists[z * _resolution + y] ??= []).Add(i);
            }
        }

        _cellOffsets = new int[cellLists.Length + 1];
        for (var i = 0; i < cellLists.Length; i++)
        {
            _cellOffsets[i + 1] = _cellOffsets[i] + (cellLists[i]?.Count ?? 0);
        }

        _cellEntries = new int[_cellOffsets[^1]];
        for (var i = 0; i < cellLists.Length; i++)
        {
            cellLists[i]?.CopyTo(_cellEntries, _cellOffsets[i]);
        }
    }

    public Vector3 Min { get; }
    public Vector3 Max { get; }

    /// <summary>
    /// Thread-safe: reads shared immutable data only. A ray that passes exactly through
    /// an edge shared by two triangles is counted twice and flips the parity - and
    /// symmetric geometry hits such edges systematically - so three slightly offset
    /// rays vote.
    /// </summary>
    public bool IsInside(Vector3 position)
    {
        if (position.Y < Min.Y || position.Y > Max.Y || position.Z < Min.Z || position.Z > Max.Z)
            return false;

        var jitter = MathF.Max(Vector3.Distance(Min, Max), 1e-3f) * 1e-5f;
        var votes = 0;
        if (IsInsideSingleRay(position + new Vector3(0, jitter, jitter * 0.53f))) votes++;
        if (IsInsideSingleRay(position + new Vector3(0, -jitter * 0.71f, jitter))) votes++;
        if (IsInsideSingleRay(position + new Vector3(0, jitter * 0.37f, -jitter * 0.89f))) votes++;
        return votes >= 2;
    }

    private bool IsInsideSingleRay(Vector3 position)
    {
        var cell = ToCellZ(position.Z) * _resolution + ToCellY(position.Y);
        var crossings = 0;
        for (var e = _cellOffsets[cell]; e < _cellOffsets[cell + 1]; e++)
        {
            var i = _cellEntries[e];
            if (RayIntersectsTriangle(position, _a[i], _b[i], _c[i]))
                crossings++;
        }

        return (crossings & 1) == 1;
    }

    /// <summary>Möller-Trumbore for a ray along +X.</summary>
    private static bool RayIntersectsTriangle(Vector3 origin, Vector3 a, Vector3 b, Vector3 c)
    {
        var edge1 = b - a;
        var edge2 = c - a;

        // rayDir = (1,0,0): cross(rayDir, edge2) = (0, -edge2.Z, edge2.Y)
        var py = -edge2.Z;
        var pz = edge2.Y;
        var det = edge1.Y * py + edge1.Z * pz;
        if (MathF.Abs(det) < 1e-10f)
            return false;

        var invDet = 1f / det;
        var t = origin - a;
        var u = (t.Y * py + t.Z * pz) * invDet;
        if (u < 0f || u > 1f)
            return false;

        // q = cross(t, edge1)
        var qx = t.Y * edge1.Z - t.Z * edge1.Y;
        var qy = t.Z * edge1.X - t.X * edge1.Z;
        var qz = t.X * edge1.Y - t.Y * edge1.X;
        var v = qx * invDet; // dot(rayDir, q) with rayDir = (1,0,0)
        if (v < 0f || u + v > 1f)
            return false;

        var distance = (edge2.X * qx + edge2.Y * qy + edge2.Z * qz) * invDet;
        return distance > 1e-8f;
    }

    private int ToCellY(float y) => Math.Clamp((int)((y - Min.Y) / _cellSizeY), 0, _resolution - 1);
    private int ToCellZ(float z) => Math.Clamp((int)((z - Min.Z) / _cellSizeZ), 0, _resolution - 1);

    private readonly Vector3[] _a;
    private readonly Vector3[] _b;
    private readonly Vector3[] _c;
    private readonly int _resolution;
    private readonly float _cellSizeY;
    private readonly float _cellSizeZ;
    private readonly int[] _cellOffsets;
    private readonly int[] _cellEntries;
}
