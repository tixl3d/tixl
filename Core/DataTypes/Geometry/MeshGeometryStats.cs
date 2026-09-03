#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace T3.Core.DataTypes;

/// <summary>
/// Measures a <see cref="MeshGeometry"/>: counts, bounds, signed volume and
/// watertightness. Shared by the stats operator and the editor's geometry output
/// view. Keep one instance per consumer - it reuses its edge table and only
/// remeasures when the geometry's identity or <see cref="MeshGeometry.Version"/> changed.
/// </summary>
public sealed class MeshGeometryStats
{
    public int PointCount { get; private set; }
    public int FaceCount { get; private set; }
    public int TriangleCount { get; private set; }
    public int PartCount { get; private set; }
    public Vector3 BoundsMin { get; private set; }
    public Vector3 BoundsMax { get; private set; }
    public Vector3 Size => BoundsMax - BoundsMin;

    /// <summary>Divergence-theorem volume: positive for outward-facing closed solids, meaningless for open meshes.</summary>
    public float Volume { get; private set; }

    /// <summary>Edges used by exactly one face. Zero means watertight.</summary>
    public int BoundaryEdges { get; private set; }

    /// <summary>Edges shared by more than two faces.</summary>
    public int NonManifoldEdges { get; private set; }

    public readonly record struct PartStats(int FaceCount, int BoundaryEdges, float Volume, Vector3 Pivot, int SeedIndex);

    /// <summary>One entry per <see cref="MeshGeometry.Parts"/> element; empty for part-less geometry.</summary>
    public PartStats[] Parts { get; private set; } = [];

    /// <summary>Remeasures if <paramref name="geometry"/> changed since the last call. Returns true when it did.</summary>
    public bool UpdateIfChanged(MeshGeometry geometry)
    {
        if (ReferenceEquals(geometry, _lastGeometry) && geometry.Version == _lastVersion)
            return false;

        Measure(geometry);
        _lastGeometry = geometry;
        _lastVersion = geometry.Version;
        return true;
    }

    public void Measure(MeshGeometry geometry)
    {
        var positions = geometry.Positions;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in positions)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        if (positions.Length == 0)
            min = max = Vector3.Zero;

        MeasureFaces(geometry, 0, geometry.FaceCount, out var volume, out var boundaryEdges, out var nonManifoldEdges);

        var sourceParts = geometry.Parts;
        if (Parts.Length != sourceParts.Length)
            Parts = new PartStats[sourceParts.Length];

        for (var partIndex = 0; partIndex < sourceParts.Length; partIndex++)
        {
            var part = sourceParts[partIndex];
            var faceEnd = Math.Min(part.FaceStart + part.FaceCount, geometry.FaceCount);
            MeasureFaces(geometry, part.FaceStart, faceEnd, out var partVolume, out var partBoundary, out _);
            Parts[partIndex] = new PartStats(faceEnd - part.FaceStart, partBoundary, partVolume, part.Pivot, part.SeedIndex);
        }

        PointCount = geometry.PointCount;
        FaceCount = geometry.FaceCount;
        TriangleCount = geometry.GetTriangleCount();
        PartCount = geometry.Parts.Length;
        BoundsMin = min;
        BoundsMax = max;
        Volume = volume;
        BoundaryEdges = boundaryEdges;
        NonManifoldEdges = nonManifoldEdges;
    }

    private void MeasureFaces(MeshGeometry geometry, int faceStart, int faceEnd, out float volume, out int boundaryEdges, out int nonManifoldEdges)
    {
        var positions = geometry.Positions;
        var offsets = geometry.FaceCornerOffsets;
        var corners = geometry.CornerPointIndices;

        _edgeUse.Clear();
        var volumeSum = 0.0;
        for (var faceIndex = faceStart; faceIndex < faceEnd; faceIndex++)
        {
            var start = offsets[faceIndex];
            var end = offsets[faceIndex + 1];
            for (var c = start; c < end; c++)
            {
                var next = c + 1 == end ? start : c + 1;
                var a = corners[c];
                var b = corners[next];
                var key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                _edgeUse[key] = _edgeUse.GetValueOrDefault(key) + 1;
            }

            var p0 = positions[corners[start]];
            for (var c = start + 2; c < end; c++)
            {
                volumeSum += Vector3.Dot(p0, Vector3.Cross(positions[corners[c - 1]], positions[corners[c]])) / 6.0;
            }
        }

        boundaryEdges = 0;
        nonManifoldEdges = 0;
        foreach (var use in _edgeUse.Values)
        {
            if (use == 1)
                boundaryEdges++;
            else if (use > 2)
                nonManifoldEdges++;
        }

        volume = (float)volumeSum;
    }

    private readonly Dictionary<long, int> _edgeUse = [];
    private MeshGeometry? _lastGeometry;
    private int _lastVersion;
}
