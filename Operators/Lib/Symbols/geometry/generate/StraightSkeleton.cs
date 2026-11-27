#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Lib.Utils;
using Lib.Utils.StraightSkeleton;
using Lib.Utils.StraightSkeleton.Primitives;
using T3.Core.Utils;

namespace Lib.geometry.generate;

/// <summary>
/// Computes the straight skeleton of the closed curves in a Geometry: every contour edge moves inward at the
/// same speed, corners follow their angle bisectors, and the traces of those corners become the skeleton.
/// Nested contours become holes, and every contour can contain holes of its own.
/// The event-based sweep is the vendored StraightSkeletonNet implementation; see
/// <c>Lib/Utils/StraightSkeleton/NOTICE.txt</c> for its origin and licence.
/// </summary>
[Guid("cde60bf0-9ed7-4505-9338-3a397befacb0")]
internal sealed class StraightSkeleton : Instance<StraightSkeleton>, IStatusProvider, IProgressProvider
{
    private enum OutputModes
    {
        Skeleton,
        Roof,
        Both,
    }

    [Output(Guid = "4fa8de00-a7f4-4487-a725-f8b4170d3b63")]
    public readonly Slot<Geometry> Result = new();

    public StraightSkeleton()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Curves.GetValue(context);
        var height = Height.GetValue(context);
        var tolerance = MathF.Max(Tolerance.GetValue(context), 1e-6f);
        var mode = (OutputModes)Math.Clamp(Output.GetValue(context), 0, (int)OutputModes.Both);
        _statusMessage = string.Empty;

        if (source == null || source.PrimitiveCount == 0)
        {
            _asyncComputation.WaitForPending(Result);
            Result.Value = null!;
            _statusMessage = "No closed curves or polygons.";
            return;
        }

        var inputsVersion = HashCode.Combine(source.Version, source.GetHashCode(), height, tolerance, mode);
        if (Async.GetValue(context))
        {
            var result = _asyncComputation.Update(context, Result, inputsVersion,
                                                  token => Build(source, height, tolerance, mode, token));
            if (result != null)
            {
                Result.Value = result;
                _statusMessage = _buildStatus;
            }

            return;
        }

        _asyncComputation.WaitForPending(Result);
        Result.Value = Build(source, height, tolerance, mode, CancellationToken.None);
        _statusMessage = _buildStatus;
    }

    private Geometry Build(Geometry source, float height, float tolerance, OutputModes mode, CancellationToken token)
    {
        _buildStatus = string.Empty;
        var warnings = new List<string>();
        var loops = CollectLoops(source, tolerance, warnings);
        _asyncComputation.ReportProgress(0.1f);
        if (loops.Count == 0)
        {
            _buildStatus = warnings.Count > 0 ? string.Join(" ", warnings) : "No closed curves or polygons to skeletonize.";
            return new Geometry();
        }

        AssignNesting(loops);
        token.ThrowIfCancellationRequested();

        var positions = new List<Vector3>();
        var pointTimes = new List<float>();
        var vertexPoints = new List<int>();
        var offsets = new List<int> { 0 };
        var kinds = new List<PrimitiveKinds>();
        var closed = new List<bool>();
        var parts = new List<PrimitivePart>();
        var skeletonLoops = 0;

        for (var outerIndex = 0; outerIndex < loops.Count; outerIndex++)
        {
            token.ThrowIfCancellationRequested();
            _asyncComputation.ReportProgress(0.1f + 0.8f * outerIndex / loops.Count);
            var outer = loops[outerIndex];
            if (outer.Depth % 2 != 0)
                continue;

            var holeLoops = new List<Loop>();
            for (var loopIndex = 0; loopIndex < loops.Count; loopIndex++)
            {
                var candidate = loops[loopIndex];
                if (candidate.Depth % 2 == 1 && candidate.Parent == outerIndex)
                    holeLoops.Add(candidate);
            }

            var islandPointIds = new Dictionary<(long X, long Y), int>();
            var islandArcs = new List<(int A, int B)>();
            var islandArcsSeen = new HashSet<(int A, int B)>();
            var primitiveStart = kinds.Count;
            var baseZ = outer.Z;

            int GetPoint(Vector2d point, double time)
            {
                var key = (Quantize(point.X), Quantize(point.Y));
                if (islandPointIds.TryGetValue(key, out var existing))
                    return existing;

                var id = positions.Count;
                islandPointIds[key] = id;
                positions.Add(new Vector3((float)point.X, (float)point.Y, baseZ + (float)time * height));
                pointTimes.Add((float)time);
                return id;
            }

            var outerPoints = ToSkeletonPoints(outer.Points);
            var holes = new List<List<Vector2d>>(holeLoops.Count);
            foreach (var hole in holeLoops)
                holes.Add(ToSkeletonPoints(hole.Points));

            var skeleton = TryBuildSkeleton(outerPoints, holes, outer.Points, holeLoops, tolerance, outerIndex, out var failure);
            if (skeleton == null)
            {
                warnings.Add($"Skeleton failed on contour {outerIndex + 1}: {failure?.Message ?? "unknown error"}");
                Log.Warning($"StraightSkeleton contour {outerIndex + 1} failed: {failure}", this);
                continue;
            }

            if (skeleton.Edges.Count == 0)
            {
                warnings.Add($"Contour {outerIndex + 1} produced no skeleton faces.");
                continue;
            }

            skeletonLoops++;
            var exactTimes = skeleton.Distances;
            var timeByPoint = BuildTimeLookup(exactTimes);

            double GetTime(Vector2d point)
            {
                if (exactTimes.TryGetValue(point, out var time))
                    return time;

                return timeByPoint.TryGetValue((Quantize(point.X), Quantize(point.Y)), out time) ? time : 0;
            }

            foreach (var face in skeleton.Edges)
            {
                token.ThrowIfCancellationRequested();
                var faceIds = new List<int>(face.Polygon.Count);
                foreach (var point in face.Polygon)
                {
                    var id = GetPoint(point, GetTime(point));
                    if (faceIds.Count == 0 || faceIds[^1] != id)
                        faceIds.Add(id);
                }

                while (faceIds.Count > 1 && faceIds[^1] == faceIds[0])
                    faceIds.RemoveAt(faceIds.Count - 1);

                if (faceIds.Count < 3)
                    continue;

                if (SignedArea(faceIds, positions) < 0)
                    faceIds.Reverse();

                if (mode is OutputModes.Roof or OutputModes.Both)
                    AddPrimitive(faceIds, PrimitiveKinds.Polygon, true);

                if (mode is not (OutputModes.Skeleton or OutputModes.Both))
                    continue;

                for (var corner = 0; corner < faceIds.Count; corner++)
                {
                    var a = faceIds[corner];
                    var b = faceIds[(corner + 1) % faceIds.Count];
                    if (pointTimes[a] <= TimeEpsilon && pointTimes[b] <= TimeEpsilon)
                        continue; // the input boundary, not a skeleton arc

                    var key = a < b ? (a, b) : (b, a);
                    if (islandArcsSeen.Add(key))
                        islandArcs.Add(key);
                }
            }

            if (mode is OutputModes.Skeleton or OutputModes.Both)
                AddSkeletonCurves(islandArcs, positions.Count, token);

            var partCount = kinds.Count - primitiveStart;
            if (partCount > 0)
                parts.Add(new PrimitivePart(primitiveStart, partCount, GetPivot(outer.Points, baseZ), parts.Count, outerIndex));
        }

        if (skeletonLoops == 0)
        {
            _buildStatus = warnings.Count > 0 ? string.Join(" ", warnings) : "No skeleton could be built.";
            return new Geometry();
        }

        if (positions.Count == 0 || kinds.Count == 0)
        {
            _buildStatus = warnings.Count > 0 ? string.Join(" ", warnings) : "The skeleton is empty.";
            return new Geometry();
        }

        var target = new Geometry();
        target.Resize(positions.Count, kinds.Count, vertexPoints.Count);
        positions.CopyTo(target.Positions);
        vertexPoints.CopyTo(target.VertexPoints);
        offsets.CopyTo(target.PrimitiveVertexOffsets);
        kinds.CopyTo(target.PrimitiveKind);
        closed.CopyTo(target.PrimitiveClosed);
        target.Parts = parts.Count > 0 ? parts.ToArray() : [];

        var times = target.Attributes.GetOrCreate<float>(SkeletonTimeAttribute, AttributeDomain.Point, positions.Count);
        pointTimes.CopyTo(times.Values);

        target.InvalidateTopologyCaches();
        _asyncComputation.ReportProgress(1f);
        _buildStatus = warnings.Count > 0 ? string.Join(" ", warnings) : string.Empty;
        return target;

        void AddPrimitive(List<int> ids, PrimitiveKinds kind, bool isClosed)
        {
            if (ids.Count == 0)
                return;

            vertexPoints.AddRange(ids);
            offsets.Add(vertexPoints.Count);
            kinds.Add(kind);
            closed.Add(isClosed);
        }

        void AddSkeletonCurves(List<(int A, int B)> arcs, int pointCount, CancellationToken token)
        {
            if (arcs.Count == 0)
                return;

            var adjacency = new List<int>[pointCount];
            for (var arcIndex = 0; arcIndex < arcs.Count; arcIndex++)
            {
                var (a, b) = arcs[arcIndex];
                (adjacency[a] ??= []).Add(arcIndex);
                (adjacency[b] ??= []).Add(arcIndex);
            }

            var used = new bool[arcs.Count];
            for (var seed = 0; seed < arcs.Count; seed++)
            {
                token.ThrowIfCancellationRequested();
                if (used[seed])
                    continue;

                used[seed] = true;
                var (start, end) = arcs[seed];
                var chain = new List<int>(4) { start, end };
                var isClosed = false;

                while (adjacency[end]?.Count == 2)
                {
                    var next = FindUnusedArc(end, adjacency, arcs, used);
                    if (next < 0)
                        break;

                    used[next] = true;
                    var other = Other(arcs[next], end);
                    if (other == chain[0])
                    {
                        isClosed = true;
                        break;
                    }

                    chain.Add(other);
                    end = other;
                }

                if (!isClosed)
                {
                    while (adjacency[chain[0]]?.Count == 2)
                    {
                        var next = FindUnusedArc(chain[0], adjacency, arcs, used);
                        if (next < 0)
                            break;

                        used[next] = true;
                        var other = Other(arcs[next], chain[0]);
                        if (other == chain[^1])
                        {
                            isClosed = true;
                            break;
                        }

                        chain.Insert(0, other);
                    }
                }

                AddPrimitive(chain, PrimitiveKinds.Curve, isClosed);
            }
        }
    }

    /// <summary>One input contour, flattened into the XY plane.</summary>
    private sealed class Loop
    {
        public List<Vector2> Points = [];
        public float Z;
        public float Area;
        public int Depth;
        public int Parent = -1;
    }

    /// <summary>
    /// Builds one island and retries with a progressively simplified outline when the sweep hits a degenerate event.
    /// </summary>
    private Lib.Utils.StraightSkeleton.Skeleton? TryBuildSkeleton(List<Vector2d> outerPoints, List<List<Vector2d>> holes,
                                                                  List<Vector2> outerLoop, List<Loop> holeLoops,
                                                                  float tolerance, int outerIndex,
                                                                  out Exception? failure)
    {
        failure = null;
        try
        {
            return SkeletonBuilder.Build(outerPoints, holes.Count > 0 ? holes : null);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        var simplify = MathF.Max(tolerance * 0.25f, MinRetryCollinearTolerance);
        var limit = MathF.Max(simplify * 16f, MaxRetryCollinearTolerance);
        for (; simplify <= limit; simplify *= 2f)
        {
            var simpleOuter = SimplifyContour(outerLoop, simplify);
            if (simpleOuter.Count < 3)
                break;

            var simpleHoles = new List<List<Vector2d>>(holeLoops.Count);
            var valid = true;
            foreach (var hole in holeLoops)
            {
                var simpleHole = SimplifyContour(hole.Points, simplify);
                if (simpleHole.Count < 3)
                {
                    valid = false;
                    break;
                }

                simpleHoles.Add(ToSkeletonPoints(simpleHole));
            }

            if (!valid)
                continue;

            try
            {
                var skeleton = SkeletonBuilder.Build(ToSkeletonPoints(simpleOuter), simpleHoles.Count > 0 ? simpleHoles : null);
                Log.Debug($"StraightSkeleton contour {outerIndex + 1} simplified to {simplify:R} for a stable skeleton.", this);
                return skeleton;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        return null;
    }

    private static List<Vector2> SimplifyContour(List<Vector2> points, float tolerance)
    {
        var copy = new List<Vector2>(points);
        RemoveCollinearPoints(copy, tolerance);
        return copy;
    }

    private List<Loop> CollectLoops(Geometry source, float tolerance, List<string> warnings)
    {
        var loops = new List<Loop>();
        var scratch = new List<Vector3>();
        for (var primitive = 0; primitive < source.PrimitiveCount; primitive++)
        {
            var kind = source.PrimitiveKind[primitive];
            if (kind == PrimitiveKinds.Curve)
            {
                if (!source.PrimitiveClosed[primitive])
                    continue;

                scratch.Clear();
                source.Curves[primitive].Flatten(tolerance, scratch);
                var loop = new Loop();
                AddPoints(loop, scratch);
                AddLoop(loops, loop, primitive, warnings);
            }
            else if (kind == PrimitiveKinds.Polygon)
            {
                var loop = new Loop();
                var start = source.PrimitiveVertexOffsets[primitive];
                var end = source.PrimitiveVertexOffsets[primitive + 1];
                var zSum = 0f;
                for (var vertex = start; vertex < end; vertex++)
                {
                    var point = source.Positions[source.VertexPoints[vertex]];
                    if (loop.Points.Count == 0 || Vector2.DistanceSquared(loop.Points[^1], new Vector2(point.X, point.Y)) > 1e-12f)
                        loop.Points.Add(new Vector2(point.X, point.Y));

                    zSum += point.Z;
                }

                loop.Z = end > start ? zSum / (end - start) : 0;
                AddLoop(loops, loop, primitive, warnings);
            }
        }

        return loops;
    }

    private static void AddPoints(Loop loop, List<Vector3> points)
    {
        var zSum = 0f;
        foreach (var point in points)
        {
            var xy = new Vector2(point.X, point.Y);
            if (loop.Points.Count == 0 || Vector2.DistanceSquared(loop.Points[^1], xy) > 1e-12f)
                loop.Points.Add(xy);

            zSum += point.Z;
        }

        loop.Z = points.Count > 0 ? zSum / points.Count : 0;
    }

    private static void AddLoop(List<Loop> loops, Loop loop, int primitive, List<string> warnings)
    {
        while (loop.Points.Count > 1 && Vector2.DistanceSquared(loop.Points[0], loop.Points[^1]) <= 1e-12f)
            loop.Points.RemoveAt(loop.Points.Count - 1);

        RemoveCollinearPoints(loop.Points, CollinearTolerance);
        if (loop.Points.Count < 3)
        {
            warnings.Add($"Primitive {primitive} has fewer than three points and was skipped.");
            return;
        }

        loop.Area = SignedArea(loop.Points);
        if (MathF.Abs(loop.Area) < 1e-9f)
        {
            warnings.Add($"Primitive {primitive} has no area and was skipped.");
            return;
        }

        loops.Add(loop);
    }

    private static void RemoveCollinearPoints(List<Vector2> points, float tolerance)
    {
        var changed = true;
        while (changed && points.Count > 3)
        {
            changed = false;
            for (var index = 0; index < points.Count; index++)
            {
                var previous = points[(index + points.Count - 1) % points.Count];
                var current = points[index];
                var next = points[(index + 1) % points.Count];
                var a = current - previous;
                var b = next - current;
                var cross = MathF.Abs(a.X * b.Y - a.Y * b.X);
                var length = a.Length() + b.Length();
                if (cross > length * tolerance || Vector2.Dot(a, b) <= 0)
                    continue;

                points.RemoveAt(index);
                changed = true;
                break;
            }
        }
    }

    private static void AssignNesting(List<Loop> loops)
    {
        for (var loopIndex = 0; loopIndex < loops.Count; loopIndex++)
        {
            var depth = 0;
            var point = loops[loopIndex].Points[0];
            for (var otherIndex = 0; otherIndex < loops.Count; otherIndex++)
            {
                if (otherIndex != loopIndex && Contains(loops[otherIndex].Points, point))
                    depth++;
            }

            loops[loopIndex].Depth = depth;
        }

        for (var loopIndex = 0; loopIndex < loops.Count; loopIndex++)
        {
            var loop = loops[loopIndex];
            if (loop.Depth % 2 == 0)
                continue;

            var point = loop.Points[0];
            var smallestArea = float.MaxValue;
            for (var otherIndex = 0; otherIndex < loops.Count; otherIndex++)
            {
                var candidate = loops[otherIndex];
                if (candidate.Depth != loop.Depth - 1 || !Contains(candidate.Points, point))
                    continue;

                var area = MathF.Abs(candidate.Area);
                if (area >= smallestArea)
                    continue;

                smallestArea = area;
                loop.Parent = otherIndex;
            }
        }
    }

    private static bool Contains(List<Vector2> polygon, Vector2 point)
    {
        var inside = false;
        for (int current = 0, previous = polygon.Count - 1; current < polygon.Count; previous = current++)
        {
            var a = polygon[current];
            var b = polygon[previous];
            if ((a.Y > point.Y) == (b.Y > point.Y))
                continue;

            var x = (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X;
            if (point.X < x)
                inside = !inside;
        }

        return inside;
    }

    private static float SignedArea(List<Vector2> points)
    {
        var area = 0f;
        for (var index = 0; index < points.Count; index++)
        {
            var a = points[index];
            var b = points[(index + 1) % points.Count];
            area += a.X * b.Y - b.X * a.Y;
        }

        return area * 0.5f;
    }

    private static float SignedArea(List<int> ids, List<Vector3> positions)
    {
        var area = 0f;
        for (var index = 0; index < ids.Count; index++)
        {
            var a = positions[ids[index]];
            var b = positions[ids[(index + 1) % ids.Count]];
            area += a.X * b.Y - b.X * a.Y;
        }

        return area * 0.5f;
    }

    private static Vector3 GetPivot(List<Vector2> points, float z)
    {
        double area = 0;
        double x = 0;
        double y = 0;
        for (var index = 0; index < points.Count; index++)
        {
            var a = points[index];
            var b = points[(index + 1) % points.Count];
            var cross = (double)a.X * b.Y - (double)b.X * a.Y;
            area += cross;
            x += ((double)a.X + b.X) * cross;
            y += ((double)a.Y + b.Y) * cross;
        }

        area *= 0.5;
        if (Math.Abs(area) < 1e-12)
        {
            var sum = Vector2.Zero;
            foreach (var point in points)
                sum += point;

            var center = sum / points.Count;
            return new Vector3(center, z);
        }

        return new Vector3((float)(x / (6 * area)), (float)(y / (6 * area)), z);
    }

    private static List<Vector2d> ToSkeletonPoints(List<Vector2> points)
    {
        var result = new List<Vector2d>(points.Count);
        foreach (var point in points)
            result.Add(new Vector2d(point.X, point.Y));

        return result;
    }

    private static Dictionary<(long X, long Y), double> BuildTimeLookup(Dictionary<Vector2d, double> distances)
    {
        var quantized = new Dictionary<(long X, long Y), double>(distances.Count);
        foreach (var pair in distances)
            quantized[(Quantize(pair.Key.X), Quantize(pair.Key.Y))] = pair.Value;

        return quantized;
    }

    private static int FindUnusedArc(int point, List<int>[] adjacency, List<(int A, int B)> arcs, bool[] used)
    {
        var incidents = adjacency[point];
        if (incidents == null)
            return -1;

        foreach (var arcIndex in incidents)
        {
            if (!used[arcIndex])
                return arcIndex;
        }

        return -1;
    }

    private static int Other((int A, int B) arc, int point) => arc.A == point ? arc.B : arc.A;

    private static long Quantize(double value) => (long)Math.Round(value * 1e5);

    public bool TryGetProgress(out float progress) => _asyncComputation.TryGetUiProgress(out progress);

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_statusMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage() => _statusMessage;

    private const string SkeletonTimeAttribute = "SkeletonTime";
    private const float TimeEpsilon = 1e-6f;
    private const float CollinearTolerance = 1e-5f;
    private const float MinRetryCollinearTolerance = 5e-4f;
    private const float MaxRetryCollinearTolerance = 8e-3f;

    private readonly AsyncComputation<Geometry> _asyncComputation = new();
    private volatile string _buildStatus = string.Empty;
    private string _statusMessage = string.Empty;

    [Input(Guid = "76653262-91d0-41e4-b807-efcd34dd86f9")]
    public readonly InputSlot<Geometry> Curves = new();

    [Input(Guid = "309ed1ce-5fff-46e4-9e2b-9bb9d51195e5")]
    public readonly InputSlot<float> Height = new();

    [Input(Guid = "348a2532-139d-4bc1-80b7-c6051a6353cd")]
    public readonly InputSlot<float> Tolerance = new();

    [Input(Guid = "52990565-f0ab-45cf-923b-b1a7cd5bdb07", MappedType = typeof(OutputModes))]
    public readonly InputSlot<int> Output = new();

    [Input(Guid = "13ffec39-19ec-4d3d-8425-2f82412651be")]
    public readonly InputSlot<bool> Async = new();
}
