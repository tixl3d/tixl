#nullable enable
using T3.Core.Output;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Surface-space geometry, shared by the Size (m) fields and the canvas edge handles. A surface's corner-pin
/// quad is the projective image of its rectangle, so resizing means: recover that projection from the current
/// quad, then re-project the new rectangle. The projector's own view never moves — only the surface's
/// footprint changes shape.
/// <para><b>Surface space</b> is metres, Y up, with the origin at the surface's <see cref="Surface.Anchor"/>.
/// Everything a surface owns is stored in it — measuring lines, child regions, the metre raster — so a crop
/// that changes the footprint leaves all of them where they are: only the rectangle's bounds move, never the
/// origin. Quads are handed out in the projector's winding, TL, TR, BR, BL; bounds as (min, max).</para>
/// </summary>
internal static class SurfaceGeometry
{
    /// <summary>Smallest edge length we allow, so a crop can't collapse a surface to nothing.</summary>
    public const float MinSize = 0.01f;

    /// <summary>The surface's own rectangle in its space, as a TL, TR, BR, BL quad — the frame the corner pin maps.</summary>
    public static Vector2[] LocalRect(Surface surface)
    {
        LocalBounds(surface, out var min, out var max);
        return RectFromBounds(min, max);
    }

    /// <summary>The surface's own rectangle in its space: bottom-left and top-right, in metres from the anchor.</summary>
    public static void LocalBounds(Surface surface, out Vector2 min, out Vector2 max)
    {
        min = -surface.AnchorInMeters;
        max = min + surface.SizeInMeters;
    }

    /// <summary>TL, TR, BR, BL from Y-up bounds.</summary>
    public static Vector2[] RectFromBounds(Vector2 min, Vector2 max)
    {
        return [new Vector2(min.X, max.Y), max, new Vector2(max.X, min.Y), min];
    }

    /// <summary>The projection carrying the surface's own space into this mapping's output pixels.</summary>
    public static bool TryGetSurfaceToOutput(Surface surface, Surface.OutputMapping mapping, out Homography surfaceToOutput)
    {
        surfaceToOutput = default;
        var size = surface.SizeInMeters;
        return size.X > 0.0001f && size.Y > 0.0001f && mapping.Quad.Length >= 4
               && Homography.TryComputeQuadToQuad(LocalRect(surface), mapping.Quad, out surfaceToOutput);
    }

    /// <summary>The inverse — output pixels back into the surface's own space.</summary>
    public static bool TryGetOutputToSurface(Surface surface, Surface.OutputMapping mapping, out Homography outputToSurface)
    {
        outputToSurface = default;
        var size = surface.SizeInMeters;
        return size.X > 0.0001f && size.Y > 0.0001f && mapping.Quad.Length >= 4
               && Homography.TryComputeQuadToQuad(mapping.Quad, LocalRect(surface), out outputToSurface);
    }

    /// <summary>
    /// Adopts new bounds — expressed in the surface's *current* space — as the surface's rectangle. Every
    /// mapping's quad is re-projected through its own recovered projection, so what the projector shows stays
    /// put while the footprint changes. The origin does not move: the anchor's normalized position is re-derived
    /// from where the origin now sits inside the new rectangle, and everything stored in surface space
    /// (measuring lines, regions, the raster) keeps its coordinates.
    /// </summary>
    public static void ApplyBounds(Surface surface, Vector2 min, Vector2 max)
    {
        var corners = RectFromBounds(min, max);
        foreach (var mapping in surface.OutputMappings)
        {
            if (!TryGetSurfaceToOutput(surface, mapping, out var surfaceToOutput))
                continue;

            for (var i = 0; i < 4; i++)
                mapping.Quad[i] = surfaceToOutput.TransformPoint(corners[i]);
        }

        var newSize = new Vector2(MathF.Max(max.X - min.X, MinSize), MathF.Max(max.Y - min.Y, MinSize));
        surface.SizeInMeters = newSize;
        surface.Anchor = new Vector2(-2 * min.X / newSize.X - 1, -2 * min.Y / newSize.Y - 1);
    }

    /// <summary>
    /// The surface that actually carries the corner pin for <paramref name="surfaceId"/> on this output —
    /// itself, or the nearest ancestor for a Layout child. A child has no mapping of its own, so anything that
    /// needs the projection (straightening, framing, editing) has to work from its carrier instead.
    /// Null when nothing in the chain is mapped to the output.
    /// </summary>
    public static Surface? FindCarrier(Setup setup, Guid surfaceId, Guid outputId)
    {
        if (surfaceId == Guid.Empty)
            return null;

        var surface = setup.FindSurface(surfaceId);
        for (var guard = 0; surface != null && guard < 16; guard++)
        {
            if (surface.OutputMappings.Exists(m => m.OutputId == outputId))
                return surface;

            if (surface.ParentId == Guid.Empty)
                break;

            var parentId = surface.ParentId;
            surface = setup.FindSurface(parentId);
        }

        return null;
    }

    /// <summary>A Layout child's rectangle in its parent's space — its stored bottom-left plus its size.</summary>
    public static void ChildBounds(Surface child, out Vector2 min, out Vector2 max)
    {
        min = child.LocalPosition;
        max = min + child.SizeInMeters;
    }

    /// <summary>Writes a child's rectangle back from bounds in the parent's space — the inverse of <see cref="ChildBounds"/>.</summary>
    public static void SetChildBounds(Surface child, Vector2 min, Vector2 max)
    {
        child.SizeInMeters = new Vector2(MathF.Max(max.X - min.X, MinSize), MathF.Max(max.Y - min.Y, MinSize));
        child.LocalPosition = min;
    }

    /// <summary>
    /// A descendant's rectangle in its <paramref name="carrier"/>'s space, composed down the whole chain — so
    /// regions can nest arbitrarily deep, not just one level. Every level is metres on the same plane, so each
    /// step is a plain translation: a child's coordinates are measured from its parent's anchor, and that anchor
    /// sits at the parent's bottom-left plus its own anchor offset. <paramref name="parentOrigin"/> is the
    /// immediate parent's origin (anchor) in carrier space — what converts a cursor back into the space edits
    /// live in.
    /// </summary>
    public static bool TryGetDescendantRect(Setup setup, Surface carrier, Surface child,
                                            out Vector2 min, out Vector2 max, out Vector2 parentOrigin)
    {
        min = max = parentOrigin = Vector2.Zero;

        // Walk up to the carrier, then apply the rectangles top-down.
        _chainScratch.Clear();
        var node = child;
        for (var guard = 0; guard < 16; guard++)
        {
            _chainScratch.Add(node);
            if (node.ParentId == carrier.Id)
                break;

            if (node.ParentId == Guid.Empty)
                return false;

            var parentId = node.ParentId;
            node = setup.FindSurface(parentId);
            if (node == null)
                return false;
        }

        if (_chainScratch.Count == 0 || _chainScratch[^1].ParentId != carrier.Id)
            return false;

        var origin = Vector2.Zero; // the carrier's own origin is its anchor, which is (0,0) in carrier space
        for (var i = _chainScratch.Count - 1; i >= 0; i--)
        {
            var current = _chainScratch[i];
            ChildBounds(current, out var localMin, out var localMax);
            parentOrigin = origin;
            min = origin + localMin;
            max = origin + localMax;

            origin = min + current.AnchorInMeters; // this level's own origin, for the level below
        }

        return true;
    }

    /// <param name="quad">Caller-owned buffer of at least 4 entries — this runs per frame, so it doesn't allocate.</param>
    public static bool TryGetChildQuad(Setup setup, Surface carrier, Surface child, Surface.OutputMapping carrierMapping, Vector2[] quad)
    {
        if (quad.Length < 4
            || !TryGetSurfaceToOutput(carrier, carrierMapping, out var surfaceToOutput)
            || !TryGetDescendantRect(setup, carrier, child, out var min, out var max, out _))
            return false;

        quad[0] = surfaceToOutput.TransformPoint(new Vector2(min.X, max.Y));
        quad[1] = surfaceToOutput.TransformPoint(max);
        quad[2] = surfaceToOutput.TransformPoint(new Vector2(max.X, min.Y));
        quad[3] = surfaceToOutput.TransformPoint(min);
        return true;
    }

    /// <summary>
    /// Coordinates worth snapping to, in the parent's space: the parent's own edges and centre, plus every
    /// sibling's. Filled into caller-owned lists, since this runs inside a drag. Snapping in the parent's
    /// space (rather than on screen) means alignments survive the perspective — edges that read as flush stay
    /// flush on the wall.
    /// </summary>
    public static void CollectSnapCandidates(Setup setup, Surface parent, Guid excludeId, List<float> xs, List<float> ys)
    {
        xs.Clear();
        ys.Clear();

        LocalBounds(parent, out var parentMin, out var parentMax);
        AddEdgesAndCentre(xs, ys, parentMin, parentMax);

        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var sibling = setup.Surfaces[i];
            if (sibling.ParentId != parent.Id || sibling.Id == excludeId)
                continue;

            ChildBounds(sibling, out var min, out var max);
            AddEdgesAndCentre(xs, ys, min, max);
        }
    }

    /// <summary>
    /// Nearest candidate to any of <paramref name="anchors"/> (a rectangle offers its two edges and its
    /// centre), returning the offset that lands on it and the coordinate hit, so a guide can be drawn.
    /// </summary>
    public static bool TrySnapOffset(List<float> candidates, ReadOnlySpan<float> anchors, float threshold,
                                     out float offset, out float target)
    {
        offset = 0;
        target = 0;
        var bestDistance = threshold;
        var found = false;

        foreach (var anchor in anchors)
        {
            foreach (var candidate in candidates)
            {
                var delta = candidate - anchor;
                var distance = MathF.Abs(delta);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                offset = delta;
                target = candidate;
                found = true;
            }
        }

        return found;
    }

    /// <summary>Resizes keeping the anchor in place, so editing one dimension extends rather than recentres.</summary>
    public static void ResizeAnchored(Surface surface, Vector2 newSize)
    {
        var oldSize = surface.SizeInMeters;
        newSize = new Vector2(MathF.Max(newSize.X, MinSize), MathF.Max(newSize.Y, MinSize));
        if (oldSize.X <= 0.0001f || oldSize.Y <= 0.0001f)
        {
            surface.SizeInMeters = newSize;
            return;
        }

        // The anchor keeps its normalized place in the rectangle, and it is the origin — so the new bounds are
        // the new size laid out around the same origin.
        var min = -(surface.Anchor + Vector2.One) * 0.5f * newSize;
        ApplyBounds(surface, min, min + newSize);
    }

    /// <summary>
    /// Moves one edge (0 = top, 1 = right, 2 = bottom, 3 = left) to <paramref name="surfacePos"/>, keeping the
    /// opposite edge fixed.
    /// <para>By default this is a <b>crop</b>: the footprint changes and the measured size follows it, so the
    /// raster's cells keep their real spacing and you simply cover more or fewer of them.</para>
    /// <para>With <paramref name="keepDimensions"/> it's a <b>stretch</b>: the same physical rectangle is
    /// mapped onto a different area, so the declared size is untouched and the raster's cells stretch with it —
    /// which is the only visible difference between the two.</para>
    /// </summary>
    public static void DragEdge(Surface surface, int edge, Vector2 surfacePos, bool keepDimensions)
    {
        var size = surface.SizeInMeters;
        if (size.X <= 0.0001f || size.Y <= 0.0001f)
            return;

        var anchor = surface.Anchor;
        LocalBounds(surface, out var min, out var max);
        switch (edge)
        {
            case 0: max.Y = MathF.Max(surfacePos.Y, min.Y + MinSize); break;
            case 1: max.X = MathF.Max(surfacePos.X, min.X + MinSize); break;
            case 2: min.Y = MathF.Min(surfacePos.Y, max.Y - MinSize); break;
            default: min.X = MathF.Min(surfacePos.X, max.X - MinSize); break;
        }

        ApplyBounds(surface, min, max);

        if (!keepDimensions)
            return;

        // A stretch keeps the declared rectangle, so its space has to come back untouched too — only the
        // mapping onto the wall changed.
        surface.SizeInMeters = size;
        surface.Anchor = anchor;
    }

    private static void AddEdgesAndCentre(List<float> xs, List<float> ys, Vector2 min, Vector2 max)
    {
        xs.Add(min.X);
        xs.Add((min.X + max.X) * 0.5f);
        xs.Add(max.X);
        ys.Add(min.Y);
        ys.Add((min.Y + max.Y) * 0.5f);
        ys.Add(max.Y);
    }

    // Ancestor chain scratch — the editor is single-threaded, and this runs inside the per-frame draw.
    private static readonly List<Surface> _chainScratch = [];
}
