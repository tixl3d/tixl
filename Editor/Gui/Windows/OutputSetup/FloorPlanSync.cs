#nullable enable
using T3.Core.Output;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Keeps a floor plan's surfaces derived from it: a wall is as wide as its segment and stands on it facing the
/// room, the floor covers the footprint. The plan's vertices are stage metres already (X right, Y away from the
/// viewer, on the ground), so its Board card can sit anywhere. Called after every edit of a plan, so the
/// persisted surface poses are always what the plan says and nothing downstream needs to know plans exist.
/// </summary>
internal static class FloorPlanSync
{
    /// <summary>
    /// Re-derives every surface the plan owns from its current vertices and Board position. A wall whose
    /// segment changed length is cropped from the end that moved — <paramref name="movedVertex"/>, or the
    /// segment's end when unknown — so its pixels and its projection stay where they were.
    /// </summary>
    public static void Apply(Setup setup, FloorPlan plan, int movedVertex = -1, int alsoMovedVertex = -1)
    {
        plan.EnsureWallSlots();

        // The plan's own space is the stage: its card's place on the Board is presentation, like every card's.
        var origin = Vector2.Zero;

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
                var startMoved = movedVertex == segment || alsoMovedVertex == segment;
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
    /// <summary>
    /// Applies the plan's wall height to the walls standing on it. A wall whose height still matches what the
    /// plan asked for before follows along; one that was given a height of its own keeps it — that difference
    /// *is* the override, so nothing extra has to be stored or migrated.
    /// </summary>
    public static void ApplyWallHeight(Setup setup, FloorPlan plan, float previousHeight)
    {
        var height = MathF.Max(plan.WallHeight, SurfaceGeometry.MinSize);
        for (var segment = 0; segment < plan.SegmentCount; segment++)
        {
            var wall = setup.FindSurface(plan.WallOf(segment));
            if (wall == null || MathF.Abs(wall.SizeInMeters.Y - previousHeight) > 0.0005f)
                continue;

            wall.SizeInMeters = new Vector2(wall.SizeInMeters.X, height);
        }

        Apply(setup, plan);
    }

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

    /// <summary>
    /// Gives a segment a typed length: its end corner moves along it, and the change travels on through the run
    /// — a corner that turns lets the next wall lengthen or shorten along its own line, a straight corner passes
    /// the shift on so that wall keeps its length, until a turning corner absorbs it.
    /// </summary>
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

        var delta = direction * (MathF.Max(length, SurfaceGeometry.MinSize) - current);
        var count = plan.Vertices.Count;
        var endVertex = (segment + 1) % count;
        plan.Vertices[endVertex] += delta;

        // Carry the shift forward: each following corner either absorbs it by sliding along its far edge, or
        // passes it on when its edge runs straight. Bounded by the run, so a fully straight run just shifts.
        var moved = endVertex;
        for (var step = 0; step < count - 2; step++)
        {
            var next = moved + 1;
            if (next >= count)
            {
                if (!plan.IsClosed)
                    break;

                next = 0;
            }

            if (next == segment)
                break;

            var edge = plan.Vertices[next] - (plan.Vertices[moved] - delta);
            var cross = edge.X * delta.Y - edge.Y * delta.X;
            if (MathF.Abs(cross) > 0.0001f * MathF.Max(edge.Length(), 0.0001f) * MathF.Max(delta.Length(), 0.0001f))
            {
                // A turning corner: the edge keeps its line, so the next corner slides along the edge after it.
                var after = next + 1;
                if (after >= count)
                {
                    if (!plan.IsClosed)
                        break;

                    after = 0;
                }

                var wanted = plan.Vertices[moved] + edge; // where the edge would end if it kept both length and line
                var far = plan.Vertices[after] - plan.Vertices[next];
                var farCross = edge.X * far.Y - edge.Y * far.X;
                if (MathF.Abs(farCross) > 0.0001f)
                {
                    // wanted + t·edge lies on the line through next along far: solve for t.
                    var toNext = plan.Vertices[next] - wanted;
                    var t = (toNext.X * far.Y - toNext.Y * far.X) / farCross;
                    plan.Vertices[next] = wanted + edge * t;
                }

                break;
            }

            // A straight corner: the next wall shifts as a whole and keeps its length.
            plan.Vertices[next] += delta;
            moved = next;
        }

        Apply(setup, plan, endVertex);
    }

    /// <summary>
    /// Takes a corner out, merging its two segments into one. The first segment's wall carries on across the
    /// merged edge; the second's is taken down (removed when untouched, lowered when in use). A closed plan keeps
    /// at least three corners, an open run two.
    /// </summary>
    public static void RemoveVertex(Setup setup, FloorPlan plan, int index)
    {
        var count = plan.Vertices.Count;
        if (index < 0 || index >= count || count <= (plan.IsClosed ? 3 : 2))
            return;

        plan.EnsureWallSlots();

        // The segment that starts at the corner disappears; on an open run's first corner that is segment 0,
        // on its last corner the segment before it (there is none after).
        var goneSegment = !plan.IsClosed && index == count - 1 ? index - 1 : index;
        if (goneSegment >= 0 && goneSegment < plan.WallSurfaceIds.Count)
        {
            LowerOrRemove(setup, plan, plan.WallSurfaceIds[goneSegment]);
            plan.WallSurfaceIds.RemoveAt(goneSegment);
        }

        plan.Vertices.RemoveAt(index);
        plan.EnsureWallSlots();
        Apply(setup, plan);
    }

    /// <summary>
    /// Splits a segment at <paramref name="point"/>: the new corner joins the run, the first half keeps the wall,
    /// and the second half gets one of its own when there was a wall, so a room stays closed.
    /// </summary>
    public static void InsertVertex(Setup setup, FloorPlan plan, int segment, Vector2 point)
    {
        if (segment < 0 || segment >= plan.SegmentCount)
            return;

        plan.EnsureWallSlots();
        var hadWall = setup.FindSurface(plan.WallOf(segment)) != null;
        plan.Vertices.Insert(segment + 1, point);
        plan.WallSurfaceIds.Insert(segment + 1, Guid.Empty);
        plan.EnsureWallSlots();
        if (hadWall)
            SetWall(setup, plan, segment + 1, true);

        Apply(setup, plan);
    }

    /// <summary>
    /// Slides a segment sideways by <paramref name="offset"/> (plan metres, along its normal), from the corners
    /// it had at <paramref name="startVertices"/>: its two corners travel along their other edges, so the
    /// neighbouring walls lengthen or shorten and every angle stays. A corner with no other edge, or one whose
    /// other edge runs parallel, moves with the segment instead.
    /// </summary>
    public static void MoveSegment(Setup setup, FloorPlan plan, int segment, IReadOnlyList<Vector2> startVertices, Vector2 offset)
    {
        var count = startVertices.Count;
        if (segment < 0 || segment >= plan.SegmentCount || count != plan.Vertices.Count)
            return;

        var a = segment;
        var b = (segment + 1) % count;
        var direction = startVertices[b] - startVertices[a];
        if (direction.LengthSquared() < 0.000001f)
            return;

        var movedA = startVertices[a] + offset;
        var movedB = startVertices[b] + offset;
        plan.Vertices[a] = SlideAlongOtherEdge(startVertices, a, a - 1, movedA, direction, plan.IsClosed);
        plan.Vertices[b] = SlideAlongOtherEdge(startVertices, b, b + 1, movedB, direction, plan.IsClosed);
        Apply(setup, plan, a, b);
    }

    /** Where the moved segment's line meets the corner's other edge; the plain move when there is no such edge or it is parallel. */
    private static Vector2 SlideAlongOtherEdge(IReadOnlyList<Vector2> startVertices, int corner, int neighbour, Vector2 moved, Vector2 direction, bool closed)
    {
        var count = startVertices.Count;
        if (!closed && (neighbour < 0 || neighbour >= count))
            return moved;

        var other = startVertices[(neighbour + count) % count] - startVertices[corner];
        var cross = direction.X * other.Y - direction.Y * other.X;
        if (MathF.Abs(cross) < 0.0001f)
            return moved;

        // Corner + t·other lies on the line through moved along direction: solve for t.
        var toMoved = moved - startVertices[corner];
        var t = (toMoved.X * direction.Y - toMoved.Y * direction.X) / (other.X * direction.Y - other.Y * direction.X);
        return startVertices[corner] + other * t;
    }

    /// <summary>
    /// Closes an open run whose end corner <paramref name="dropped"/> was put onto the other end: the dropped
    /// corner goes, the run closes, and the edge that now joins the ends gets a wall.
    /// </summary>
    public static void CloseByMerging(Setup setup, FloorPlan plan, int dropped)
    {
        var count = plan.Vertices.Count;
        if (plan.IsClosed || count < 3 || (dropped != 0 && dropped != count - 1))
            return;

        plan.EnsureWallSlots();
        var closingWall = Guid.Empty;
        if (dropped == 0)
        {
            // Segment 0 ran from the dropped corner; its wall carries on as the closing edge's wall.
            closingWall = plan.WallSurfaceIds[0];
            plan.WallSurfaceIds.RemoveAt(0);
            plan.Vertices.RemoveAt(0);
        }
        else
        {
            closingWall = plan.WallSurfaceIds[count - 2];
            plan.WallSurfaceIds.RemoveAt(count - 2);
            plan.Vertices.RemoveAt(count - 1);
        }

        plan.IsClosed = true;
        plan.EnsureWallSlots();
        var closingSegment = plan.SegmentCount - 1;
        if (closingWall != Guid.Empty && setup.FindSurface(closingWall) != null)
            plan.WallSurfaceIds[closingSegment] = closingWall;
        else
            SetWall(setup, plan, closingSegment, true);

        Apply(setup, plan);
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
