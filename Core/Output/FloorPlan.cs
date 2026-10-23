#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Serialization;

namespace T3.Core.Output;

/// <summary>
/// A venue's footprint seen from above: a run of vertices in metres whose segments may carry wall surfaces, closed
/// into a room or left open. The walls stay derived from it — their width is the segment's length and their stage
/// pose stands them on it — so editing the plan moves the room. Vertices are in the plan's own space, X right and
/// Y away from the viewer; the plan's Board position puts that space into the stage, on the one ground level.
/// </summary>
public sealed class FloorPlan
{
    /// <summary>
    /// A rectangle on the ground in plan metres, turnable: <see cref="RotationDegrees"/> runs counter-clockwise from
    /// the plan's X axis, seen from above. Its width runs along its own X, its depth along its own Y.
    /// </summary>
    public struct FloorRectangle
    {
        public Vector2 Centre;
        public Vector2 Size;
        public float RotationDegrees;

        public FloorRectangle(Vector2 centre, Vector2 size, float rotationDegrees)
        {
            Centre = centre;
            Size = size;
            RotationDegrees = rotationDegrees;
        }

        /// <summary>The unit direction its width runs along.</summary>
        public readonly Vector2 AxisX
        {
            get
            {
                var angle = RotationDegrees * MathF.PI / 180;
                return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            }
        }

        /// <summary>The unit direction its depth runs along, a quarter turn left of <see cref="AxisX"/>.</summary>
        public readonly Vector2 AxisY
        {
            get
            {
                var axisX = AxisX;
                return new Vector2(-axisX.Y, axisX.X);
            }
        }

        /// <summary>The corner where both of its axes start.</summary>
        public readonly Vector2 BottomLeft => Centre - AxisX * (Size.X * 0.5f) - AxisY * (Size.Y * 0.5f);

        /// <summary>A plan point in the rectangle's own fractions: 0..1 inside it, beyond that outside.</summary>
        public readonly Vector2 ToFractions(Vector2 planPoint)
        {
            var offset = planPoint - BottomLeft;
            var size = Vector2.Max(Size, new Vector2(0.0001f));
            return new Vector2(Vector2.Dot(offset, AxisX) / size.X, Vector2.Dot(offset, AxisY) / size.Y);
        }

        /// <summary>A point given in the rectangle's own fractions, in plan metres.</summary>
        public readonly Vector2 FromFractions(Vector2 fractions)
        {
            return BottomLeft + AxisX * (fractions.X * Size.X) + AxisY * (fractions.Y * Size.Y);
        }
    }

    public Guid Id = Guid.NewGuid();
    public string Name = string.Empty;

    /// <summary>The corners in order; consecutive ones bound a segment, and the last joins the first when closed.</summary>
    public List<Vector2> Vertices = [];

    /// <summary>Whether the last vertex joins the first — a room rather than a run of walls.</summary>
    public bool IsClosed;

    /// <summary>The surface lying on the footprint when closed; <see cref="Guid.Empty"/> for none.</summary>
    public Guid FloorSurfaceId;

    /// <summary>
    /// One entry per segment, in vertex order: the wall surface standing on it, or <see cref="Guid.Empty"/> for an
    /// open side. Kept as long as the segment list so every segment has a slot.
    /// </summary>
    public List<Guid> WallSurfaceIds = [];

    /// <summary>
    /// Surfaces taken down but kept: a wall or floor with content on it is not thrown away when its edge is
    /// unticked, it stops following the plan and stays linked, so ticking again raises the same surface.
    /// </summary>
    public HashSet<Guid> LoweredSurfaceIds = [];

    /// <summary>Height a newly raised wall gets; existing walls keep their own.</summary>
    public float WallHeight = 4f;

    /// <summary>
    /// How far a wall's bottom edge is lifted above the ground, keyed by wall surface — for a wall the projection
    /// only covers from some height up. Walls without an entry stand on the ground.
    /// </summary>
    public Dictionary<Guid, float> WallElevations = [];

    /// <summary>Its card's place on the Board; null until the Board seeded one.</summary>
    public BoardPlacement? BoardPlacement;

    /// <summary>
    /// What the floor surface stands for: its size, its pose and the patch showing on it. A concave room's footprint
    /// can reach past it (a stage niche), and those parts get texture coordinates outside 0..1. Null follows the
    /// footprint's bounding box.
    /// </summary>
    public FloorRectangle? FloorRect;

    /// <summary>How many segments the run has: every vertex pair, plus the closing one.</summary>
    public int SegmentCount => Vertices.Count < 2 ? 0 : IsClosed ? Vertices.Count : Vertices.Count - 1;

    /// <summary>Both ends of a segment, in plan space.</summary>
    public void GetSegment(int index, out Vector2 start, out Vector2 end)
    {
        start = Vertices[index];
        end = Vertices[(index + 1) % Vertices.Count];
    }

    /// <summary>The wall standing on a segment, or <see cref="Guid.Empty"/> when there is none or it is lowered; safe for any index.</summary>
    public Guid WallOf(int segment)
    {
        var id = SlotOf(segment);
        return LoweredSurfaceIds.Contains(id) ? Guid.Empty : id;
    }

    /// <summary>The surface linked to a segment, lowered or not; safe for any index.</summary>
    public Guid SlotOf(int segment) => segment >= 0 && segment < WallSurfaceIds.Count ? WallSurfaceIds[segment] : Guid.Empty;

    /// <summary>How far the wall's bottom edge is lifted above the ground; 0 for one standing on it.</summary>
    public float ElevationOf(Guid wallId) => WallElevations.TryGetValue(wallId, out var elevation) ? elevation : 0;

    /// <summary>The floor lying on the footprint, or <see cref="Guid.Empty"/> when there is none or it is lowered.</summary>
    public Guid RaisedFloorId => LoweredSurfaceIds.Contains(FloorSurfaceId) ? Guid.Empty : FloorSurfaceId;

    /// <summary>Grows or trims the wall list to one slot per segment.</summary>
    public void EnsureWallSlots()
    {
        var count = SegmentCount;
        while (WallSurfaceIds.Count < count)
            WallSurfaceIds.Add(Guid.Empty);

        if (WallSurfaceIds.Count > count)
            WallSurfaceIds.RemoveRange(count, WallSurfaceIds.Count - count);
    }

    /// <summary>The vertices' bounding box in plan space; false while there are none.</summary>
    public bool TryGetBounds(out Vector2 min, out Vector2 max)
    {
        min = max = Vector2.Zero;
        if (Vertices.Count == 0)
            return false;

        min = max = Vertices[0];
        for (var i = 1; i < Vertices.Count; i++)
        {
            min = Vector2.Min(min, Vertices[i]);
            max = Vector2.Max(max, Vertices[i]);
        }

        return true;
    }

    /// <summary>The floor's rectangle: <see cref="FloorRect"/> when set, else the bounding box; false while there are no vertices.</summary>
    public bool TryGetFloorRect(out FloorRectangle rect)
    {
        if (FloorRect is { } floorRect)
        {
            rect = floorRect;
            return true;
        }

        rect = default;
        if (!TryGetBounds(out var min, out var max))
            return false;

        rect = new FloorRectangle((min + max) * 0.5f, max - min, 0);
        return true;
    }

    /// <summary>
    /// Triangulates the closed footprint by ear clipping, writing vertex-index triples into <paramref name="triangles"/>
    /// (cleared first), counter-clockwise seen from above whatever way the run was drawn. Handles concave rooms;
    /// a self-intersecting run gets whatever ears remain.
    /// </summary>
    public void Triangulate(List<int> triangles)
    {
        triangles.Clear();
        var count = Vertices.Count;
        if (count < 3)
            return;

        _earScratch.Clear();
        if (SignedAreaTwice() >= 0)
        {
            for (var i = 0; i < count; i++)
                _earScratch.Add(i);
        }
        else
        {
            for (var i = count - 1; i >= 0; i--)
                _earScratch.Add(i);
        }

        var guard = 0;
        while (_earScratch.Count > 3 && guard++ < count * count)
        {
            var clipped = false;
            for (var i = 0; i < _earScratch.Count; i++)
            {
                var previous = _earScratch[(i + _earScratch.Count - 1) % _earScratch.Count];
                var current = _earScratch[i];
                var next = _earScratch[(i + 1) % _earScratch.Count];
                if (!IsEar(previous, current, next))
                    continue;

                triangles.Add(previous);
                triangles.Add(current);
                triangles.Add(next);
                _earScratch.RemoveAt(i);
                clipped = true;
                break;
            }

            if (!clipped)
                break;
        }

        if (_earScratch.Count == 3)
        {
            triangles.Add(_earScratch[0]);
            triangles.Add(_earScratch[1]);
            triangles.Add(_earScratch[2]);
        }
    }

    /** A convex corner whose triangle contains no other remaining vertex. */
    private bool IsEar(int previous, int current, int next)
    {
        var a = Vertices[previous];
        var b = Vertices[current];
        var c = Vertices[next];
        if (Cross(b - a, c - a) <= 0.000001f)
            return false;

        foreach (var index in _earScratch)
        {
            if (index == previous || index == current || index == next)
                continue;

            var p = Vertices[index];
            if (Cross(b - a, p - a) >= 0 && Cross(c - b, p - b) >= 0 && Cross(a - c, p - c) >= 0)
                return false;
        }

        return true;
    }

    private static float Cross(Vector2 u, Vector2 v) => u.X * v.Y - u.Y * v.X;

    private readonly List<int> _earScratch = [];

    /// <summary>Twice the signed area; positive when the vertices run counter-clockwise (seen from above).</summary>
    public float SignedAreaTwice()
    {
        var sum = 0f;
        for (var i = 0; i < Vertices.Count; i++)
        {
            var a = Vertices[i];
            var b = Vertices[(i + 1) % Vertices.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }

        return sum;
    }

    public void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteObject("Id", Id);
        writer.WriteString("Name", Name);
        writer.WriteValue("IsClosed", IsClosed);
        writer.WriteValue("WallHeight", WallHeight);
        if (FloorSurfaceId != Guid.Empty)
            writer.WriteObject("FloorSurface", FloorSurfaceId);

        writer.WritePropertyName("Vertices");
        writer.WriteStartArray();
        foreach (var vertex in Vertices)
        {
            writer.WriteStartArray();
            writer.WriteValue(vertex.X);
            writer.WriteValue(vertex.Y);
            writer.WriteEndArray();
        }

        writer.WriteEndArray();

        writer.WritePropertyName("Walls");
        writer.WriteStartArray();
        foreach (var wall in WallSurfaceIds)
            writer.WriteValue(wall.ToString());

        writer.WriteEndArray();

        if (LoweredSurfaceIds.Count > 0)
        {
            writer.WritePropertyName("Lowered");
            writer.WriteStartArray();
            foreach (var id in LoweredSurfaceIds)
                writer.WriteValue(id.ToString());

            writer.WriteEndArray();
        }

        if (WallElevations.Count > 0)
        {
            writer.WritePropertyName("WallElevations");
            writer.WriteStartObject();
            foreach (var (wallId, elevation) in WallElevations)
                writer.WriteValue(wallId.ToString(), elevation);

            writer.WriteEndObject();
        }

        if (BoardPlacement != null)
        {
            writer.WritePropertyName("BoardPlacement");
            BoardPlacement.WriteToJson(writer);
        }

        if (FloorRect is { } floorRect)
        {
            writer.WritePropertyName("FloorRect");
            writer.WriteStartObject();
            writer.WriteVector2("Centre", floorRect.Centre);
            writer.WriteVector2("Size", floorRect.Size);
            writer.WriteValue("Rotation", floorRect.RotationDegrees);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    public static FloorPlan ReadFromJson(JToken token)
    {
        var plan = new FloorPlan
                       {
                           Id = OutputJson.ReadGuid(token["Id"]),
                           Name = token.ReadValueSafe("Name", string.Empty) ?? string.Empty,
                           IsClosed = token.ReadValueSafe("IsClosed", false),
                           WallHeight = token.ReadValueSafe("WallHeight", 4f),
                           FloorSurfaceId = OutputJson.ReadGuid(token["FloorSurface"]),
                       };

        if (token["Vertices"] is JArray vertices)
        {
            foreach (var vertex in vertices)
                plan.Vertices.Add(OutputJson.ReadVector2(vertex));
        }

        if (token["Walls"] is JArray walls)
        {
            foreach (var wall in walls)
                plan.WallSurfaceIds.Add(OutputJson.ReadGuid(wall));
        }

        if (token["Lowered"] is JArray lowered)
        {
            foreach (var id in lowered)
                plan.LoweredSurfaceIds.Add(OutputJson.ReadGuid(id));
        }

        if (token["WallElevations"] is JObject elevations)
        {
            foreach (var property in elevations.Properties())
            {
                if (Guid.TryParse(property.Name, out var wallId))
                    plan.WallElevations[wallId] = property.Value.Value<float>();
            }
        }

        plan.EnsureWallSlots();
        if (token["BoardPlacement"] is JObject placement)
            plan.BoardPlacement = BoardPlacement.ReadFromJson(placement);

        if (token["FloorRect"] is JObject floorRect)
            plan.FloorRect = new FloorRectangle(OutputJson.ReadVector2(floorRect["Centre"]), OutputJson.ReadVector2(floorRect["Size"]),
                                                floorRect.ReadValueSafe("Rotation", 0f));

        return plan;
    }
}
