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
    public static void ApplyRect(Surface surface, Vector2[] newRect)
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

    /// <param name="quad">Caller-owned buffer of at least 4 entries — this runs per frame, so it doesn't allocate.</param>
    public static bool TryGetChildQuad(Surface parent, Surface child, Surface.OutputMapping parentMapping, Vector2[] quad)
    {
        if (quad.Length < 4 || !TryGetSurfaceToOutput(parent, parentMapping, out var surfaceToOutput))
            return false;

        var anchor = AnchorInSurface(parent);
        var bottomLeft = anchor + new Vector2(child.LocalPosition.X, -child.LocalPosition.Y);
        var size = child.SizeInMeters;
        var min = new Vector2(bottomLeft.X, bottomLeft.Y - size.Y);
        var max = new Vector2(bottomLeft.X + size.X, bottomLeft.Y);

        quad[0] = surfaceToOutput.TransformPoint(min);
        quad[1] = surfaceToOutput.TransformPoint(new Vector2(max.X, min.Y));
        quad[2] = surfaceToOutput.TransformPoint(max);
        quad[3] = surfaceToOutput.TransformPoint(new Vector2(min.X, max.Y));
        return true;
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

        ApplyRect(surface, RectFromBounds(min, max));

        if (!keepDimensions)
            return;

        // A stretch keeps the declared rectangle, so its normalization has to come back untouched too — only
        // the mapping onto the wall changed, not the surface's own space.
        surface.SizeInMeters = size;
        if (pivot.HasValue && surface.Placement != null)
            surface.Placement.Pivot = pivot.Value;
    }
}
