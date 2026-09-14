#nullable enable
using T3.Core.Output;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Keeps a floor plan's surfaces derived from it: a wall is as wide as its segment and stands on it facing the
/// room, the floor covers the footprint. Called after every edit of a plan, so the persisted surface poses are
/// always what the plan says and nothing downstream needs to know plans exist.
/// </summary>
internal static class FloorPlanSync
{
    /// <summary>
    /// Re-derives every surface the plan owns from its current vertices and Board position. A wall whose
    /// segment changed length is cropped from the end that moved — <paramref name="movedVertex"/>, or the
    /// segment's end when unknown — so its pixels and its projection stay where they were.
    /// </summary>
    public static void Apply(Setup setup, FloorPlan plan, int movedVertex = -1)
    {
        plan.EnsureWallSlots();
        var origin = plan.BoardPlacement?.Position ?? Vector2.Zero;

        // Walls face the inside of a room; on an open run they face left of the direction the run is drawn in.
        var facesLeft = !plan.IsClosed || plan.SignedAreaTwice() >= 0;

        for (var segment = 0; segment < plan.SegmentCount; segment++)
        {
            if (setup.FindSurface(plan.WallSurfaceIds[segment]) == null)
                plan.WallSurfaceIds[segment] = Guid.Empty;

            // A lowered wall keeps its slot but is free: nothing is derived for it.
            var wall = setup.FindSurface(plan.WallOf(segment));
            if (wall == null)
                continue;

            plan.GetSegment(segment, out var start, out var end);
            var direction = end - start;
            var length = direction.Length();
            if (length < SurfaceGeometry.MinSize)
                continue;

            direction /= length;
            var normal = facesLeft ? new Vector2(-direction.Y, direction.X) : new Vector2(direction.Y, -direction.X);
            if (MathF.Abs(wall.SizeInMeters.X - length) > 0.0005f)
            {
                // Seen from inside, a wall facing left of its segment runs right-to-left along it: the segment's
                // start is the wall's right end. So the start moving crops the right side, the end the left.
                var startMoved = movedVertex == segment;
                var cropLeft = facesLeft ? !startMoved : startMoved;
                CropWall(setup, wall, length, cropLeft);
            }

            StagePlacing.PoseWall(wall, PlanToStage(origin, (start + end) * 0.5f), new Vector3(normal.X, 0, -normal.Y));
        }

        if (setup.FindSurface(plan.FloorSurfaceId) == null)
            plan.FloorSurfaceId = Guid.Empty;

        var floor = plan.IsClosed ? setup.FindSurface(plan.RaisedFloorId) : null;
        if (floor == null)
            return;

        // The floor surface is a rectangle, so it covers the footprint's bounding box.
        if (!plan.TryGetBounds(out var min, out var max))
            return;

        var size = Vector2.Max(max - min, new Vector2(SurfaceGeometry.MinSize));
        if ((floor.SizeInMeters - size).Length() > 0.0005f)
            SurfaceMetrics.RemeterSurface(setup, floor, size);

        StagePlacing.PoseFloor(floor, PlanToStage(origin, min));
    }

    /// <summary>A closed rectangle of <paramref name="size"/>, counter-clockwise from its near-left corner.</summary>
    public static FloorPlan CreateRectangle(Setup setup, Vector2 size, bool withFloor)
    {
        var plan = new FloorPlan
                       {
                           Name = $"Floor Plan {setup.FloorPlans.Count + 1}",
                           Vertices = [Vector2.Zero, new Vector2(size.X, 0), size, new Vector2(0, size.Y)],
                           IsClosed = true,
                       };
        plan.EnsureWallSlots();
        setup.FloorPlans.Add(plan);
        if (withFloor)
            SetFloor(setup, plan, true);

        return plan;
    }

    /// <summary>
    /// Starts an open plan from a wall that already exists: its bottom edge becomes the first segment, so the
    /// plan is drawn on from a measured or traced surface. The card lands under the surface's.
    /// </summary>
    public static FloorPlan StartFromWall(Setup setup, Surface wall)
    {
        var plan = new FloorPlan
                       {
                           Name = $"Floor Plan {setup.FloorPlans.Count + 1}",
                           Vertices = [Vector2.Zero, new Vector2(MathF.Max(wall.SizeInMeters.X, SurfaceGeometry.MinSize), 0)],
                           WallHeight = wall.SizeInMeters.Y,
                           BoardPlacement = PlacementUnder(wall, 0),
                       };
        plan.EnsureWallSlots();
        plan.WallSurfaceIds[0] = wall.Id;
        setup.FloorPlans.Add(plan);
        Apply(setup, plan);
        return plan;
    }

    /// <summary>A closed rectangle the size of <paramref name="floor"/>, with that surface lying on it.</summary>
    public static FloorPlan StartFromFloor(Setup setup, Surface floor)
    {
        var plan = CreateRectangle(setup, Vector2.Max(floor.SizeInMeters, new Vector2(SurfaceGeometry.MinSize)), withFloor: false);
        plan.BoardPlacement = PlacementUnder(floor, floor.SizeInMeters.Y);
        plan.FloorSurfaceId = floor.Id;
        Apply(setup, plan);
        return plan;
    }

    /// <summary>
    /// Raises a wall on a segment, or takes it down: a wall nothing was done to is removed, one that carries
    /// content, a projection, a trace or regions is lowered — it stays linked to the edge but stops following
    /// the plan, and ticking the edge again raises that same surface.
    /// </summary>
    public static void SetWall(Setup setup, FloorPlan plan, int segment, bool on)
    {
        plan.EnsureWallSlots();
        if (segment < 0 || segment >= plan.SegmentCount)
            return;

        if (!on)
        {
            if (!LowerOrRemove(setup, plan, plan.WallSurfaceIds[segment]))
                plan.WallSurfaceIds[segment] = Guid.Empty;

            return;
        }

        var linked = setup.FindSurface(plan.WallSurfaceIds[segment]);
        if (linked != null)
        {
            plan.LoweredSurfaceIds.Remove(linked.Id);
            Apply(setup, plan);
            return;
        }

        plan.GetSegment(segment, out var start, out var end);
        var wall = new Surface
                       {
                           Name = $"Wall {segment + 1}",
                           SizeInMeters = new Vector2(MathF.Max((end - start).Length(), SurfaceGeometry.MinSize), MathF.Max(plan.WallHeight, SurfaceGeometry.MinSize)),
                       };
        setup.Surfaces.Add(wall);
        plan.WallSurfaceIds[segment] = wall.Id;
        Apply(setup, plan);
    }

    /// <summary>Lays a floor on a closed plan, or takes it up — removed when untouched, lowered when in use (see <see cref="SetWall"/>).</summary>
    public static void SetFloor(Setup setup, FloorPlan plan, bool on)
    {
        if (!on)
        {
            if (!LowerOrRemove(setup, plan, plan.FloorSurfaceId))
                plan.FloorSurfaceId = Guid.Empty;

            return;
        }

        if (!plan.IsClosed)
            return;

        var linked = setup.FindSurface(plan.FloorSurfaceId);
        if (linked != null)
        {
            plan.LoweredSurfaceIds.Remove(linked.Id);
            Apply(setup, plan);
            return;
        }

        if (!plan.TryGetBounds(out var min, out var max))
            return;

        var floor = new Surface
                        {
                            Name = "Floor",
                            SizeInMeters = Vector2.Max(max - min, new Vector2(SurfaceGeometry.MinSize)),
                        };
        setup.Surfaces.Add(floor);
        plan.FloorSurfaceId = floor.Id;
        Apply(setup, plan);
    }

    /// <summary>Gives a segment a typed length by moving its end vertex along it — the wall's width, written back to the plan.</summary>
    public static void SetSegmentLength(Setup setup, FloorPlan plan, int segment, float length)
    {
        if (segment < 0 || segment >= plan.SegmentCount)
            return;

        plan.GetSegment(segment, out var start, out var end);
        var direction = end - start;
        var current = direction.Length();
        if (current < 0.0001f)
            direction = Vector2.UnitX;
        else
            direction /= current;

        var endVertex = (segment + 1) % plan.Vertices.Count;
        plan.Vertices[endVertex] = start + direction * MathF.Max(length, SurfaceGeometry.MinSize);
        Apply(setup, plan, endVertex);
    }

    /// <summary>Forgets deleted surfaces: their slots open up, the plan itself stays.</summary>
    public static void ReleaseSurfaces(Setup setup, HashSet<Guid> surfaceIds)
    {
        foreach (var plan in setup.FloorPlans)
        {
            plan.LoweredSurfaceIds.RemoveWhere(surfaceIds.Contains);
            if (surfaceIds.Contains(plan.FloorSurfaceId))
                plan.FloorSurfaceId = Guid.Empty;

            for (var i = 0; i < plan.WallSurfaceIds.Count; i++)
            {
                if (surfaceIds.Contains(plan.WallSurfaceIds[i]))
                    plan.WallSurfaceIds[i] = Guid.Empty;
            }
        }
    }

    /// <summary>
    /// Sets a wall's width by cropping one side, like an edge drag on the Board: the other side and the pixels
    /// it shows stay put, the projection stays aligned. Not a re-metering, which would stretch the content.
    /// </summary>
    private static void CropWall(Setup setup, Surface wall, float width, bool fromLeft)
    {
        SurfaceGeometry.LocalBounds(wall, out var oldMin, out var oldMax);
        var edgeX = fromLeft ? oldMax.X - width : oldMin.X + width;
        SurfaceGeometry.DragEdge(wall, fromLeft ? 3 : 1, new Vector2(edgeX, 0), keepDimensions: false);

        SurfaceGeometry.LocalBounds(wall, out var newMin, out var newMax);
        if (SliceUvAnchoring.TryBegin(setup, wall, out var sliceId, out var uvStart))
            SliceUvAnchoring.ApplyCrop(setup, sliceId, uvStart, oldMin, oldMax, newMin, newMax);
    }

    /// <summary>Whether anything was done to the surface beyond the plan raising it — what makes it worth keeping.</summary>
    public static bool IsInUse(Setup setup, Surface surface)
    {
        if (surface.SliceId != Guid.Empty || surface.OutputMappings.Count > 0 || surface.Trace != null || surface.Annotations.Count > 0)
            return true;

        foreach (var other in setup.Surfaces)
        {
            if (other.ParentId == surface.Id)
                return true;
        }

        return false;
    }

    /** Takes a surface down: removed when untouched, lowered (kept and linked) when in use. True when it stays linked. */
    private static bool LowerOrRemove(Setup setup, FloorPlan plan, Guid surfaceId)
    {
        var surface = setup.FindSurface(surfaceId);
        if (surface == null)
            return false;

        if (IsInUse(setup, surface))
        {
            plan.LoweredSurfaceIds.Add(surface.Id);
            return true;
        }

        setup.Surfaces.Remove(surface);
        return false;
    }

    /// <summary>Plan metres to stage metres: the plan's up runs away from the viewer, on the ground.</summary>
    public static Vector3 PlanToStage(Vector2 origin, Vector2 point)
    {
        return new Vector3(origin.X + point.X, 0, -(origin.Y + point.Y));
    }

    /** Beneath the surface's card, so the plan starts next to what it was made from; the plan's depth keeps it clear. */
    private static BoardPlacement? PlacementUnder(Surface surface, float planDepth)
    {
        if (surface.BoardPlacement == null)
            return null;

        var bottomLeft = surface.BoardPlacement.Position - surface.AnchorInMeters;
        return new BoardPlacement { Position = new Vector2(bottomLeft.X, bottomLeft.Y - 1.5f - planDepth) };
    }
}
