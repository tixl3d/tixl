using System;
using System.Collections.Generic;

namespace Lib.geometry;

/// <summary>
/// Fractures a MeshGeometry into Voronoi cells around seed points: one part per
/// cell, built by clipping the mesh against the bisector planes between seeds and
/// capping the cuts. Surface corners keep their interpolated normals; cap faces
/// are flat and marked with Selection = 1 for downstream styling.
/// </summary>
[Guid("70d8f2b5-3a41-4c96-8e2d-b09c6f5e1a73")]
internal sealed class VoronoiFracture : Instance<VoronoiFracture>
{
    [Output(Guid = "48e5a9c1-d637-4b80-92f4-5c1e8b0d7a26")]
    public readonly Slot<MeshGeometry> Result = new();

    public VoronoiFracture()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        if (source == null || source.FaceCount == 0)
        {
            Result.Value = source;
            return;
        }

        // Seed snapshot (separator-aware)
        _seeds.Clear();
        if (Points.GetValue(context) is StructuredList<Point> pointList)
        {
            var elements = pointList.TypedElements;
            for (var i = 0; i < pointList.NumElements; i++)
            {
                if (!Point.IsSeparator(elements[i]))
                    _seeds.Add(elements[i].Position);
            }
        }

        if (_seeds.Count < 2)
        {
            Result.Value = source;
            return;
        }

        Build(source);
        Result.Value = _output;
    }

    private void Build(MeshGeometry source)
    {
        var sourceHasNormals = source.Attributes.TryGet<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, out var sourceNormals);

        // Source faces as clip-ready polygons
        _sourcePolygons.Clear();
        var offsets = source.FaceCornerOffsets;
        var corners = source.CornerPointIndices;
        for (var faceIndex = 0; faceIndex < source.FaceCount; faceIndex++)
        {
            var polygon = new Polygon { IsCap = false };
            for (var c = offsets[faceIndex]; c < offsets[faceIndex + 1]; c++)
            {
                var normal = sourceHasNormals ? sourceNormals!.Values[c] : Vector3.Zero;
                polygon.Vertices.Add(new Vertex(source.Positions[corners[c]], normal));
            }

            _sourcePolygons.Add(polygon);
        }

        _outPositions.Clear();
        _outNormals.Clear();
        _outFaceOffsets.Clear();
        _outCorners.Clear();
        _outFaceIsCap.Clear();
        _outFaceOffsets.Add(0);
        var parts = new List<GeometryPart>();

        for (var seedIndex = 0; seedIndex < _seeds.Count; seedIndex++)
        {
            var faceStart = _outFaceOffsets.Count - 1;
            EmitCell(seedIndex, sourceHasNormals);
            var faceCount = _outFaceOffsets.Count - 1 - faceStart;
            if (faceCount > 0)
                parts.Add(new GeometryPart(faceStart, faceCount, _seeds[seedIndex], seedIndex, seedIndex));
        }

        _output.Positions = _outPositions.ToArray();
        _output.FaceCornerOffsets = _outFaceOffsets.ToArray();
        _output.CornerPointIndices = _outCorners.ToArray();
        _output.Parts = parts.ToArray();
        _output.Attributes.Clear();

        if (sourceHasNormals)
        {
            var normals = _output.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, _outCorners.Count);
            for (var c = 0; c < _outCorners.Count; c++)
            {
                normals.Values[c] = _outNormals[c];
            }
        }

        var selection = _output.Attributes.GetOrCreate<float>(GeometryAttributeNames.Selection, AttributeDomain.Face, _outFaceIsCap.Count);
        for (var faceIndex = 0; faceIndex < _outFaceIsCap.Count; faceIndex++)
        {
            selection.Values[faceIndex] = _outFaceIsCap[faceIndex] ? 1f : 0f;
        }

        _output.InvalidateTopologyCaches();
    }

    private void EmitCell(int seedIndex, bool withNormals)
    {
        // Working copy of the source polygons, clipped plane by plane
        var polygons = new List<Polygon>(_sourcePolygons.Count);
        foreach (var polygon in _sourcePolygons)
        {
            polygons.Add(polygon.Clone());
        }

        var seed = _seeds[seedIndex];
        for (var otherIndex = 0; otherIndex < _seeds.Count; otherIndex++)
        {
            if (otherIndex == seedIndex || polygons.Count == 0)
                continue;

            var toOther = _seeds[otherIndex] - seed;
            var length = toOther.Length();
            if (length < 1e-8f)
                continue;

            var planeNormal = toOther / length;
            var planeOffset = Vector3.Dot(planeNormal, (seed + _seeds[otherIndex]) * 0.5f);
            ClipByPlane(polygons, planeNormal, planeOffset);
        }

        // Emit with per-cell point dedup so each chunk is watertight
        _cellPointLookup.Clear();
        foreach (var polygon in polygons)
        {
            if (polygon.Vertices.Count < 3)
                continue;

            foreach (var vertex in polygon.Vertices)
            {
                _outCorners.Add(GetOrAddCellPoint(vertex.Position));
                _outNormals.Add(withNormals ? vertex.Normal : Vector3.Zero);
            }

            _outFaceOffsets.Add(_outCorners.Count);
            _outFaceIsCap.Add(polygon.IsCap);
        }
    }

    /// <summary>Keeps the half-space dot(n, x) &lt;= offset; cut openings get capped.</summary>
    private void ClipByPlane(List<Polygon> polygons, Vector3 planeNormal, float planeOffset)
    {
        _cutSegments.Clear();
        var anyClipped = false;

        for (var polygonIndex = polygons.Count - 1; polygonIndex >= 0; polygonIndex--)
        {
            var polygon = polygons[polygonIndex];
            var vertices = polygon.Vertices;
            var allInside = true;
            var allOutside = true;
            foreach (var vertex in vertices)
            {
                if (Vector3.Dot(planeNormal, vertex.Position) - planeOffset > ClipEpsilon)
                    allInside = false;
                else
                    allOutside = false;
            }

            if (allInside)
                continue;

            anyClipped = true;
            if (allOutside)
            {
                polygons.RemoveAt(polygonIndex);
                continue;
            }

            _clipScratch.Clear();
            Vertex? firstCut = null;
            for (var i = 0; i < vertices.Count; i++)
            {
                var current = vertices[i];
                var next = vertices[(i + 1) % vertices.Count];
                var currentDistance = Vector3.Dot(planeNormal, current.Position) - planeOffset;
                var nextDistance = Vector3.Dot(planeNormal, next.Position) - planeOffset;
                var currentInside = currentDistance <= ClipEpsilon;
                var nextInside = nextDistance <= ClipEpsilon;

                if (currentInside)
                    _clipScratch.Add(current);

                if (currentInside == nextInside)
                    continue;

                var t = currentDistance / (currentDistance - nextDistance);
                var cut = Vertex.Lerp(current, next, t);
                _clipScratch.Add(cut);
                if (currentInside)
                {
                    firstCut = cut; // leaving the kept side: segment starts here
                }
                else if (firstCut.HasValue)
                {
                    _cutSegments.Add((firstCut.Value, cut));
                    firstCut = null;
                }
                else
                {
                    firstCut = cut; // loop started outside; pair up at the wrap-around exit
                    _pendingEntry = cut;
                    _hasPendingEntry = true;
                }
            }

            // A polygon that started outside pairs its first entry with the last exit
            if (_hasPendingEntry && firstCut.HasValue && !firstCut.Value.Equals(_pendingEntry))
                _cutSegments.Add((firstCut.Value, _pendingEntry));
            _hasPendingEntry = false;

            if (_clipScratch.Count < 3)
            {
                polygons.RemoveAt(polygonIndex);
            }
            else
            {
                vertices.Clear();
                vertices.AddRange(_clipScratch);
            }
        }

        if (!anyClipped || _cutSegments.Count < 3)
            return;

        BuildCaps(polygons, planeNormal);
    }

    /// <summary>Chains the cut segments into loops and emits flat cap polygons.</summary>
    private void BuildCaps(List<Polygon> polygons, Vector3 planeNormal)
    {
        // Segments are oriented exit->entry along the kept surface. Cut points from
        // different faces only match within float error, so chain by nearest endpoint.
        var segmentCount = _cutSegments.Count;
        if (_segmentUsed.Length < segmentCount)
            _segmentUsed = new bool[segmentCount];
        Array.Clear(_segmentUsed, 0, segmentCount);

        for (var startIndex = 0; startIndex < segmentCount; startIndex++)
        {
            if (_segmentUsed[startIndex])
                continue;

            var cap = new Polygon { IsCap = true };
            var currentIndex = startIndex;
            for (var guard = 0; guard <= segmentCount; guard++)
            {
                _segmentUsed[currentIndex] = true;
                var segment = _cutSegments[currentIndex];
                cap.Vertices.Add(new Vertex(segment.From.Position, planeNormal));

                var nextIndex = -1;
                var bestDistanceSq = ChainEpsilonSq;
                for (var candidate = 0; candidate < segmentCount; candidate++)
                {
                    if (_segmentUsed[candidate])
                        continue;

                    var distanceSq = Vector3.DistanceSquared(segment.To.Position, _cutSegments[candidate].From.Position);
                    if (distanceSq < bestDistanceSq)
                    {
                        bestDistanceSq = distanceSq;
                        nextIndex = candidate;
                    }
                }

                if (nextIndex < 0)
                    break; // loop closed back to the start (or broke off)

                currentIndex = nextIndex;
            }

            if (cap.Vertices.Count < 3)
                continue;

            // Orient the cap outward (along the clip plane normal)
            var newell = Vector3.Zero;
            for (var i = 0; i < cap.Vertices.Count; i++)
            {
                var p0 = cap.Vertices[i].Position;
                var p1 = cap.Vertices[(i + 1) % cap.Vertices.Count].Position;
                newell += new Vector3((p0.Y - p1.Y) * (p0.Z + p1.Z),
                                      (p0.Z - p1.Z) * (p0.X + p1.X),
                                      (p0.X - p1.X) * (p0.Y + p1.Y));
            }

            if (Vector3.Dot(newell, planeNormal) < 0)
                cap.Vertices.Reverse();

            polygons.Add(cap);
        }
    }

    private int GetOrAddCellPoint(Vector3 position)
    {
        var key = Quantize(position);
        if (_cellPointLookup.TryGetValue(key, out var pointId))
            return pointId;

        pointId = _outPositions.Count;
        _outPositions.Add(position);
        _cellPointLookup.Add(key, pointId);
        return pointId;
    }

    private static (int, int, int) Quantize(Vector3 position)
    {
        return ((int)MathF.Round(position.X * 100000f),
                (int)MathF.Round(position.Y * 100000f),
                (int)MathF.Round(position.Z * 100000f));
    }

    private readonly record struct Vertex(Vector3 Position, Vector3 Normal)
    {
        public static Vertex Lerp(in Vertex a, in Vertex b, float t)
        {
            var normal = Vector3.Lerp(a.Normal, b.Normal, t);
            if (normal.LengthSquared() > 1e-10f)
                normal = Vector3.Normalize(normal);
            return new Vertex(Vector3.Lerp(a.Position, b.Position, t), normal);
        }
    }

    private sealed class Polygon
    {
        public readonly List<Vertex> Vertices = [];
        public bool IsCap;

        public Polygon Clone()
        {
            var clone = new Polygon { IsCap = IsCap };
            clone.Vertices.AddRange(Vertices);
            return clone;
        }
    }

    private const float ClipEpsilon = 1e-6f;
    private const float ChainEpsilonSq = 1e-4f * 1e-4f;

    private readonly MeshGeometry _output = new();
    private readonly List<Vector3> _seeds = [];
    private readonly List<Polygon> _sourcePolygons = [];
    private readonly List<Vertex> _clipScratch = [];
    private readonly List<(Vertex From, Vertex To)> _cutSegments = [];
    private readonly Dictionary<(int, int, int), int> _cellPointLookup = [];
    private bool[] _segmentUsed = [];
    private readonly List<Vector3> _outPositions = [];
    private readonly List<Vector3> _outNormals = [];
    private readonly List<int> _outFaceOffsets = [];
    private readonly List<int> _outCorners = [];
    private readonly List<bool> _outFaceIsCap = [];
    private Vertex _pendingEntry;
    private bool _hasPendingEntry;

    [Input(Guid = "31c7e9d4-85f2-4a60-b1c8-6d0a5e3f9b27")]
    public readonly InputSlot<MeshGeometry> Geometry = new();

    [Input(Guid = "84a2f6c0-19db-4e75-93a4-c7e1b8d25f06")]
    public readonly InputSlot<StructuredList> Points = new();
}
