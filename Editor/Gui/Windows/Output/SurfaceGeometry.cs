#nullable enable
using T3.Core.Output;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.Output;

/// <summary>
/// Surface-space geometry, shared by the Size (m) fields and the canvas edge handles. A surface's corner-pin
/// quad is the projective image of its rectangle, so resizing means: recover that projection from the current
/// quad, then re-project the new rectangle. The projector's own view never moves — only the surface's
/// footprint changes shape. Surface space has its origin at the surface's top-left with Y growing down, so it
/// matches the quad's TL, TR, BR, BL winding.
/// </summary>
internal static class SurfaceGeometry
{
    /// <summary>Smallest edge length we allow, so a crop can't collapse a surface to nothing.</summary>
    public const float MinSize = 0.01f;

    public static Vector2[] RectForSize(Vector2 size)
    {
        return [Vector2.Zero, new Vector2(size.X, 0), new Vector2(size.X, size.Y), new Vector2(0, size.Y)];
    }

    public static Vector2[] RectFromBounds(Vector2 min, Vector2 max)
    {
        return [min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y)];
    }

    /// <summary>The projection carrying the surface's own space into this mapping's output pixels.</summary>
    public static bool TryGetSurfaceToOutput(Surface surface, Surface.OutputMapping mapping, out Homography surfaceToOutput)
    {
        surfaceToOutput = default;
        var size = surface.SizeInMeters;
        return size.X > 0.0001f && size.Y > 0.0001f && mapping.Quad.Length >= 4
               && Homography.TryComputeQuadToQuad(RectForSize(size), mapping.Quad, out surfaceToOutput);
    }

    /// <summary>The inverse — output pixels back into the surface's own space.</summary>
    public static bool TryGetOutputToSurface(Surface surface, Surface.OutputMapping mapping, out Homography outputToSurface)
    {
        outputToSurface = default;
        var size = surface.SizeInMeters;
        return size.X > 0.0001f && size.Y > 0.0001f && mapping.Quad.Length >= 4
               && Homography.TryComputeQuadToQuad(mapping.Quad, RectForSize(size), out outputToSurface);
    }

    /// <summary>
    /// Adopts <paramref name="newRect"/> — expressed in the surface's *current* space — as the surface's
    /// rectangle. Every mapping's quad is re-projected through its own recovered projection, so what the
    /// projector shows stays put while the surface's footprint changes.
    /// </summary>
    /// <param name="pinAnnotations">When the origin moves (a crop/resize), re-base the measuring lines by the
    /// same offset so they stay on their physical features next to the grid. A stretch keeps the surface's own
    /// space, so it passes false and leaves them where they are.</param>
    public static void ApplyRect(Surface surface, Vector2[] newRect, bool pinAnnotations = true)
    {
        var oldSize = surface.SizeInMeters;
        var pivot = surface.Placement?.Pivot ?? Vector2.Zero;

        // Where the anchor sits in the current frame, so it can be held at the same physical point below.
        var anchor = new Vector2(pivot.X * oldSize.X, oldSize.Y - pivot.Y * oldSize.Y);

        foreach (var mapping in surface.OutputMappings)
        {
            if (!TryGetSurfaceToOutput(surface, mapping, out var surfaceToOutput))
                continue;

            for (var i = 0; i < 4; i++)
                mapping.Quad[i] = surfaceToOutput.TransformPoint(newRect[i]);
        }

        var min = newRect[0];
        var max = newRect[2];
        var newSize = new Vector2(MathF.Max(max.X - min.X, MinSize), MathF.Max(max.Y - min.Y, MinSize));
        surface.SizeInMeters = newSize;

        // The rectangle's own origin just moved, so counter-move the anchor to keep it — and with it the metre
        // raster and everything measured against it — pinned to the same physical point on the wall. A crop
        // must never shift the surface's local content space, only shrink the window onto it.
        var newPivot = new Vector2((anchor.X - min.X) / newSize.X, (max.Y - anchor.Y) / newSize.Y);
        if (MathF.Abs(newPivot.X - pivot.X) > 0.0001f || MathF.Abs(newPivot.Y - pivot.Y) > 0.0001f)
            (surface.Placement ??= new Surface.StagePlacement()).Pivot = newPivot;

        // Measuring lines are stored from the top-left origin, so the same origin shift has to travel to them —
        // otherwise cropping the top/left edge slides them off the features they mark. The grid follows the
        // (counter-moved) pivot, so this is what keeps the two aligned.
        if (pinAnnotations && (min.X != 0 || min.Y != 0))
        {
            foreach (var annotation in surface.Annotations)
            {
                annotation.P1 -= min;
                annotation.P2 -= min;
            }
        }
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

        var surface = setup.Surfaces.Find(s => s.Id == surfaceId);
        for (var guard = 0; surface != null && guard < 16; guard++)
        {
            if (surface.OutputMappings.Exists(m => m.OutputId == outputId))
                return surface;

            if (surface.ParentId == Guid.Empty)
                break;

            var parentId = surface.ParentId;
            surface = setup.Surfaces.Find(s => s.Id == parentId);
        }

        return null;
    }

    /// <summary>The anchor's position in a surface's own space (origin top-left, Y down).</summary>
    public static Vector2 AnchorInSurface(Surface surface)
    {
        var size = surface.SizeInMeters;
        var pivot = surface.Placement?.Pivot ?? Vector2.Zero;
        return new Vector2(pivot.X * size.X, size.Y - pivot.Y * size.Y);
    }

    /// <summary>
    /// A Layout child's rectangle expressed in its parent's space. The child stores its bottom-left in meters
    /// from the parent's anchor (X right, Y up), so this is where that lands with the parent's Y-down frame.
    /// </summary>
    public static Vector2[] ChildRectInParent(Surface parent, Surface child)
    {
        var anchor = AnchorInSurface(parent);
        var bottomLeft = anchor + new Vector2(child.LocalPosition.X, -child.LocalPosition.Y);
        var size = child.SizeInMeters;
        return RectFromBounds(new Vector2(bottomLeft.X, bottomLeft.Y - size.Y),
                              new Vector2(bottomLeft.X + size.X, bottomLeft.Y));
    }

    /// <summary>
    /// A Layout child has no corner pin of its own — it rides its parent's, so its quad is derived by pushing
    /// its rectangle through the parent's projection for that output.
    /// </summary>
    /// <summary>
    /// Writes a child's rectangle back, given bounds in the parent's space — the inverse of
    /// <see cref="ChildRectInParent"/>. Position is stored relative to the parent's anchor, so it survives the
    /// parent being cropped.
    /// </summary>
    public static void SetChildRect(Surface parent, Surface child, Vector2 min, Vector2 max)
    {
        var size = new Vector2(MathF.Max(max.X - min.X, MinSize), MathF.Max(max.Y - min.Y, MinSize));
        var anchor = AnchorInSurface(parent);

        child.SizeInMeters = size;
        child.LocalPosition = new Vector2(min.X - anchor.X, anchor.Y - (min.Y + size.Y));
    }

    /// <summary>
    /// A descendant's rectangle in its <paramref name="carrier"/>'s space, composed down the whole chain — so
    /// regions can nest arbitrarily deep, not just one level. Every level is metres on the same plane, so each
    /// step is a plain translation by the parent's origin. <paramref name="parentOrigin"/> is the immediate
    /// parent's origin in carrier space, which is what converts a cursor back into the space edits live in.
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
            node = setup.Surfaces.Find(s => s.Id == parentId);
            if (node == null)
                return false;
        }

        if (_chainScratch.Count == 0 || _chainScratch[^1].ParentId != carrier.Id)
            return false;

        var offset = Vector2.Zero;
        var parent = carrier;
        for (var i = _chainScratch.Count - 1; i >= 0; i--)
        {
            var current = _chainScratch[i];
            var rect = ChildRectInParent(parent, current);
            parentOrigin = offset;
            min = offset + rect[0];
            max = offset + rect[2];

            offset = min; // this level's space origin, for the level below
            parent = current;
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

        quad[0] = surfaceToOutput.TransformPoint(min);
        quad[1] = surfaceToOutput.TransformPoint(new Vector2(max.X, min.Y));
        quad[2] = surfaceToOutput.TransformPoint(max);
        quad[3] = surfaceToOutput.TransformPoint(new Vector2(min.X, max.Y));
        return true;
    }

    // Ancestor chain scratch — the editor is single-threaded, and this runs inside the per-frame draw.
    private static readonly List<Surface> _chainScratch = [];

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

        var size = parent.SizeInMeters;
        xs.Add(0);
        xs.Add(size.X * 0.5f);
        xs.Add(size.X);
        ys.Add(0);
        ys.Add(size.Y * 0.5f);
        ys.Add(size.Y);

        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var sibling = setup.Surfaces[i];
            if (sibling.ParentId != parent.Id || sibling.Id == excludeId)
                continue;

            var rect = ChildRectInParent(parent, sibling);
            xs.Add(rect[0].X);
            xs.Add((rect[0].X + rect[2].X) * 0.5f);
            xs.Add(rect[2].X);
            ys.Add(rect[0].Y);
            ys.Add((rect[0].Y + rect[2].Y) * 0.5f);
            ys.Add(rect[2].Y);
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

    /// <summary>Resizes keeping the pivot anchored, so editing one dimension extends rather than recentres.</summary>
    public static void ResizeAnchored(Surface surface, Vector2 newSize)
    {
        var oldSize = surface.SizeInMeters;
        newSize = new Vector2(MathF.Max(newSize.X, MinSize), MathF.Max(newSize.Y, MinSize));
        if (oldSize.X <= 0.0001f || oldSize.Y <= 0.0001f)
        {
            surface.SizeInMeters = newSize;
            return;
        }

        // Pivot is normalized from the surface's bottom-left, while surface space runs Y down.
        var pivot = surface.Placement?.Pivot ?? Vector2.Zero;
        var minX = pivot.X * oldSize.X - pivot.X * newSize.X;
        var maxY = oldSize.Y - pivot.Y * oldSize.Y + pivot.Y * newSize.Y;
        ApplyRect(surface, RectFromBounds(new Vector2(minX, maxY - newSize.Y), new Vector2(minX + newSize.X, maxY)));
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

        var pivot = surface.Placement?.Pivot;

        var min = Vector2.Zero;
        var max = size;
        switch (edge)
        {
            case 0: min.Y = MathF.Min(surfacePos.Y, max.Y - MinSize); break;
            case 1: max.X = MathF.Max(surfacePos.X, min.X + MinSize); break;
            case 2: max.Y = MathF.Max(surfacePos.Y, min.Y + MinSize); break;
            default: min.X = MathF.Min(surfacePos.X, max.X - MinSize); break;
        }

        // A stretch restores the surface's own space below, so its annotations must stay put (pinAnnotations:
        // false); a crop re-bases the space, so they ride the origin shift.
        ApplyRect(surface, RectFromBounds(min, max), pinAnnotations: !keepDimensions);

        if (!keepDimensions)
            return;

        // A stretch keeps the declared rectangle, so its normalization has to come back untouched too — only
        // the mapping onto the wall changed, not the surface's own space.
        surface.SizeInMeters = size;
        if (pivot.HasValue && surface.Placement != null)
            surface.Placement.Pivot = pivot.Value;
    }
}
