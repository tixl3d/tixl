#nullable enable
using System.Threading;

namespace Lib.Utils;

/// <summary>
/// Shrinks a polygon with its straight skeleton: every edge moves inward at unit speed, a corner follows the
/// miter of its two edges, and an edge that shrinks to nothing is removed by merging its two corners. A concave
/// corner that would reach a non-adjacent edge (a split event) stops the inset before it happens, so the
/// result stays a simple polygon. Used per face by [BevelGeometry] and per outline loop by [FillCurves].
/// </summary>
internal sealed class StraightSkeletonInset
{
    /// <summary>A corner of the polygon being shrunk: its position at time t is Origin + Velocity * t.</summary>
    public readonly record struct Vertex(Vector3 Origin, Vector3 Velocity, Vector3 PreviousNormal, Vector3 NextNormal,
                                         int Previous, int Next, bool Alive = true);

    public int Count => _vertices.Count;
    public int AliveCount => _aliveCount;

    public Vertex this[int slot]
    {
        get => _vertices[slot];
        set => _vertices[slot] = value;
    }

    public void Clear()
    {
        _vertices.Clear();
        _aliases.Clear();
        _aliveCount = 0;
    }

    public void Add(Vertex vertex)
    {
        _aliases.Add(_vertices.Count);
        _vertices.Add(vertex);
        _aliveCount++;
    }

    /// <summary>The alive slot a merged corner ended up in; follow merges until a slot points at itself.</summary>
    public int ResolveSlot(int slot)
    {
        var root = slot;
        while (_aliases[root] != root)
            root = _aliases[root];

        while (_aliases[slot] != slot)
        {
            var next = _aliases[slot];
            _aliases[slot] = root;
            slot = next;
        }

        return root;
    }

    /// <summary>
    /// Runs the collapse events of the skeleton until the edges have moved inward by <paramref name="width"/>,
    /// returning the distance actually reached. Vertices are left updated, so a corner's inset position is
    /// Origin + Velocity * result.
    /// </summary>
    public float Limit(float width, Vector3 faceNormal, CancellationToken token)
    {
        var time = 0f;
        var limit = width;
        var guard = 0;
        while (_aliveCount > 2)
        {
            if ((guard++ & 255) == 0)
                token.ThrowIfCancellationRequested();

            var collapseSlot = FindNextEdgeCollapse(time, out var collapseTime);
            var splitTime = FindFirstSplit(faceNormal, time);
            if (splitTime < limit && splitTime <= collapseTime)
            {
                limit = time + (splitTime - time) * SplitSafety;
                break;
            }

            if (collapseSlot < 0 || collapseTime >= limit)
                break;

            MergeWithNext(collapseSlot, collapseTime);
            time = collapseTime;
        }

        if (_aliveCount <= 2)
            limit = time; // the polygon shrank to a ridge or a point - moving on would cross it

        return limit;
    }

    /// <summary>The alive slot whose outgoing edge shrinks to zero first after <paramref name="time"/>.</summary>
    private int FindNextEdgeCollapse(float time, out float collapseTime)
    {
        collapseTime = float.PositiveInfinity;
        var bestSlot = -1;
        for (var slot = 0; slot < _vertices.Count; slot++)
        {
            var vertex = _vertices[slot];
            if (!vertex.Alive)
                continue;

            var next = _vertices[vertex.Next];
            var direction = EdgeDirection(vertex.NextNormal, next.Origin - vertex.Origin);
            var shrinkRate = Vector3.Dot(vertex.Velocity - next.Velocity, direction);
            if (shrinkRate <= 1e-6f)
                continue;

            var t = MathF.Max(Vector3.Dot(next.Origin - vertex.Origin, direction) / shrinkRate, time);
            if (t < collapseTime)
            {
                collapseTime = t;
                bestSlot = slot;
            }
        }

        return bestSlot;
    }

    /// <summary>The time a concave corner first reaches a non-adjacent edge (a split event), or infinity.</summary>
    private float FindFirstSplit(Vector3 faceNormal, float time)
    {
        var splitTime = float.PositiveInfinity;
        for (var slot = 0; slot < _vertices.Count; slot++)
        {
            var vertex = _vertices[slot];
            if (!vertex.Alive || Vector3.Dot(Vector3.Cross(vertex.PreviousNormal, vertex.NextNormal), faceNormal) >= -1e-6f)
                continue; // only concave corners can split a polygon

            for (var edgeSlot = 0; edgeSlot < _vertices.Count; edgeSlot++)
            {
                var edgeStart = _vertices[edgeSlot];
                if (!edgeStart.Alive || edgeSlot == slot || edgeStart.Next == slot)
                    continue;

                // Signed distance of the corner to the edge's line, which moves inward at unit speed
                var approachRate = Vector3.Dot(vertex.Velocity - edgeStart.Velocity, edgeStart.NextNormal);
                if (approachRate >= -1e-6f)
                    continue;

                var t = -Vector3.Dot(vertex.Origin - edgeStart.Origin, edgeStart.NextNormal) / approachRate;
                if (t <= time || t >= splitTime)
                    continue;

                var edgeEnd = _vertices[edgeStart.Next];
                var a = edgeStart.Origin + edgeStart.Velocity * t;
                var b = edgeEnd.Origin + edgeEnd.Velocity * t;
                var hit = vertex.Origin + vertex.Velocity * t;
                var ab = b - a;
                var lengthSq = ab.LengthSquared();
                if (lengthSq < 1e-20f)
                    continue;

                var along = Vector3.Dot(hit - a, ab) / lengthSq;
                if (along >= 0 && along <= 1)
                    splitTime = t;
            }
        }

        return splitTime;
    }

    /// <summary>Removes the edge after <paramref name="slot"/>: both of its corners continue as one.</summary>
    private void MergeWithNext(int slot, float time)
    {
        var vertex = _vertices[slot];
        var nextSlot = vertex.Next;
        var next = _vertices[nextSlot];
        var position = (vertex.Origin + vertex.Velocity * time + next.Origin + next.Velocity * time) * 0.5f;
        var velocity = MiterVelocity(vertex.PreviousNormal, next.NextNormal);

        _vertices[slot] = vertex with
                              {
                                  Origin = position - velocity * time,
                                  Velocity = velocity,
                                  NextNormal = next.NextNormal,
                                  Next = next.Next,
                              };
        _vertices[next.Next] = _vertices[next.Next] with { Previous = slot };
        _vertices[nextSlot] = next with { Alive = false };
        _aliases[nextSlot] = slot;
        _aliveCount--;
    }

    /// <summary>
    /// Velocity of a corner between two edges moving along their inward normals at unit speed. Unlike the
    /// bisector of the edge directions it points into the polygon at concave corners too.
    /// </summary>
    public static Vector3 MiterVelocity(Vector3 inwardA, Vector3 inwardB)
    {
        // A near-180 degree spike would need an unbounded miter; its sum vanishes, so the corner stays put
        return (inwardA + inwardB) / MathF.Max(1f + Vector3.Dot(inwardA, inwardB), MinMiterDenominator);
    }

    /// <summary>Points into a counter-clockwise polygon (seen from its normal), perpendicular to the edge.</summary>
    public static Vector3 EdgeInwardNormal(Vector3 from, Vector3 to, Vector3 faceNormal)
    {
        var inward = Vector3.Cross(faceNormal, to - from);
        var length = inward.Length();
        return length > 1e-12f ? inward / length : Vector3.Zero;
    }

    /// <summary>Recovers the direction of a skeleton edge from its inward normal; falls back to the chord for degenerate edges.</summary>
    private static Vector3 EdgeDirection(Vector3 inwardNormal, Vector3 chord)
    {
        if (inwardNormal.LengthSquared() > 0.5f)
        {
            var faceNormal = Vector3.Cross(chord, inwardNormal);
            if (faceNormal.LengthSquared() > 1e-20f)
                return Vector3.Normalize(Vector3.Cross(inwardNormal, Vector3.Normalize(faceNormal)));
        }

        var length = chord.Length();
        return length > 1e-12f ? chord / length : Vector3.Zero;
    }

    /** Stops a concave polygon this far before a corner would touch the opposite edge. */
    public const float SplitSafety = 0.9f;

    /** 1 + cos of the turn between two edge normals; caps miter length at sharp spikes. */
    public const float MinMiterDenominator = 0.02f;

    private readonly List<Vertex> _vertices = [];
    private readonly List<int> _aliases = [];
    private int _aliveCount;
}
