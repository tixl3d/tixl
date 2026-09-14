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

    /// <summary>Its card's place on the Board; null until the Board seeded one.</summary>
    public BoardPlacement? BoardPlacement;

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

        if (BoardPlacement != null)
        {
            writer.WritePropertyName("BoardPlacement");
            BoardPlacement.WriteToJson(writer);
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

        plan.EnsureWallSlots();
        if (token["BoardPlacement"] is JObject placement)
            plan.BoardPlacement = BoardPlacement.ReadFromJson(placement);

        return plan;
    }
}
