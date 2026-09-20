#nullable enable
using System;
using System.Collections.Generic;

namespace T3.Core.Output;

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
public static class SurfaceGeometry
{
    /// <summary>Smallest edge length we allow, so a crop can't collapse a surface to nothing.</summary>
    public const float MinSize = 0.01f;

    /// <summary>Smallest slice extent, as a fraction of its source — shared by the canvas handles and the parameter fields.</summary>
    public const float MinSliceSize = 0.01f;

    /// <summary>The surface's own rectangle in its space, as a TL, TR, BR, BL quad — the frame the corner pin maps.</summary>
    /// <param name="corners">Caller-owned, at least 4 entries — this runs per frame, so it doesn't allocate.</param>
    public static void WriteLocalRect(Surface surface, Span<Vector2> corners)
    {
        LocalBounds(surface, out var min, out var max);
        WriteRectCorners(min, max, corners, yUp: true);
    }

    /// <summary>The surface's own rectangle in its space: bottom-left and top-right, in metres from the anchor.</summary>
    public static void LocalBounds(Surface surface, out Vector2 min, out Vector2 max)
    {
        min = -surface.AnchorInMeters;
        max = min + surface.SizeInMeters;
    }

    /// <summary>
    /// The four corners of a rect in the projector's winding, TL, TR, BR, BL. Which corner is "top" depends on
    /// the space: <paramref name="yUp"/> for metres (surface and Board space), false for pixels (canvas, photo).
    /// </summary>
    public static void WriteRectCorners(Vector2 min, Vector2 max, Span<Vector2> corners, bool yUp)
    {
        if (yUp)
        {
            corners[0] = new Vector2(min.X, max.Y);
            corners[1] = max;
            corners[2] = new Vector2(max.X, min.Y);
            corners[3] = min;
        }
        else
        {
            corners[0] = min;
            corners[1] = new Vector2(max.X, min.Y);
            corners[2] = max;
            corners[3] = new Vector2(min.X, max.Y);
        }
    }

    /// <summary>
    /// Moves one edge of a Y-up rect (0 = top, 1 = right, 2 = bottom, 3 = left) to <paramref name="pos"/>, the
    /// opposite edge staying put and the rect never thinner than <paramref name="minSize"/>.
    /// </summary>
    public static void MoveEdge(ref Vector2 min, ref Vector2 max, int edge, Vector2 pos, float minSize)
    {
        switch (edge)
        {
            case 0: max.Y = MathF.Max(pos.Y, min.Y + minSize); break;
            case 1: max.X = MathF.Max(pos.X, min.X + minSize); break;
            case 2: min.Y = MathF.Min(pos.Y, max.Y - minSize); break;
            default: min.X = MathF.Min(pos.X, max.X - minSize); break;
        }
    }

    /// <summary>The pixel size of the canvas a mapping lands on — what its stored 0..1 quad is measured
    /// against wherever the editor works in output pixels.</summary>
    public static Vector2 CanvasSizeOf(Setup setup, Guid outputId)
    {
        var output = setup.FindOutput(outputId);
        return output?.CanvasSize ?? new Vector2(1920, 1080);
    }

    /// <param name="canvasSize">What the mapping's stored 0..1 quad is measured against: the output's pixel
    /// size to land in canvas pixels (the renderer), or <see cref="Vector2.One"/> to stay in the canvas' own
    /// normalized space (the editor's canvases, which are framed in it).</param>
    public static bool TryGetSurfaceToOutput(Surface surface, Surface.OutputMapping mapping, Vector2 canvasSize, out Homography surfaceToOutput)
    {
        surfaceToOutput = default;
        var size = surface.SizeInMeters;
        if (size.X <= 0.0001f || size.Y <= 0.0001f || mapping.Quad.Length < 4)
            return false;

        Span<Vector2> rect = stackalloc Vector2[4];
        WriteLocalRect(surface, rect);
        return Homography.TryComputeQuadToQuad(rect, ScaledQuad(mapping.Quad, canvasSize), out surfaceToOutput);
    }

    /// <summary>The stored 0..1 quad in the given space; the scratch is reused, so consume it before the next call.</summary>
    private static Vector2[] ScaledQuad(Vector2[] quad, Vector2 canvasSize)
    {
        for (var i = 0; i < 4; i++)
            _quadScratch[i] = quad[i] * canvasSize;

        return _quadScratch;
    }

    private static readonly Vector2[] _quadScratch = new Vector2[4];

    /// <summary>The inverse — output pixels back into the surface's own space.</summary>
    public static bool TryGetOutputToSurface(Surface surface, Surface.OutputMapping mapping, Vector2 canvasSize, out Homography outputToSurface)
    {
        outputToSurface = default;
        var size = surface.SizeInMeters;
        if (size.X <= 0.0001f || size.Y <= 0.0001f || mapping.Quad.Length < 4)
            return false;

        Span<Vector2> rect = stackalloc Vector2[4];
        WriteLocalRect(surface, rect);
        return Homography.TryComputeQuadToQuad(ScaledQuad(mapping.Quad, canvasSize), rect, out outputToSurface);
    }

    /// <summary>
    /// Adopts new bounds — expressed in the surface's *current* space — as the surface's rectangle. The origin
    /// does not move: the anchor's normalized position is re-derived from where the origin now sits inside the
    /// new rectangle, and everything stored in surface space (measuring lines, regions, the raster) keeps its
    /// coordinates.
    /// </summary>
    /// <param name="movesMappings">True for a gesture that reshapes the rectangle *on the wall* — a crop takes
    /// the projection with it, so each mapping's quad is re-projected through its own recovered projection and
    /// what the projector shows stays put while the footprint changes. False for a *declaration* of how big the
    /// surface really is, which must leave every projection exactly where it was aimed.</param>
    public static void ApplyBounds(Surface surface, Vector2 min, Vector2 max, bool movesMappings = true)
    {
        Span<Vector2> corners = stackalloc Vector2[4];
        WriteRectCorners(min, max, corners, yUp: true);
        if (movesMappings)
        {
            foreach (var mapping in surface.OutputMappings)
            {
                // A fill is the whole canvas whatever the surface measures — the display shows it, it is not aimed at it.
                if (mapping.IsFilling)
                    continue;

                // Read and write both in the canvas' normalized space, so no output (and no resolution) is needed.
                if (!TryGetSurfaceToOutput(surface, mapping, Vector2.One, out var surfaceToOutput))
                    continue;

                for (var i = 0; i < 4; i++)
                    mapping.Quad[i] = surfaceToOutput.TransformPoint(corners[i]);
            }
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
    public static Surface? FindMappingCarrier(Setup setup, Guid surfaceId, Guid outputId)
    {
        var carrier = setup.FindMappedAncestor(surfaceId);
        return carrier != null && carrier.HasMapping(outputId) ? carrier : null;
    }

    /// <summary>A Layout child's rectangle in its parent's space — its stored bottom-left plus its size.</summary>
    public static void RegionBounds(Surface child, out Vector2 min, out Vector2 max)
    {
        min = child.LocalPosition;
        max = min + child.SizeInMeters;
    }

    /// <summary>Writes a child's rectangle back from bounds in the parent's space — the inverse of <see cref="RegionBounds"/>.</summary>
    public static void SetRegionBounds(Surface child, Vector2 min, Vector2 max)
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
    public static bool TryGetRegionRect(Setup setup, Surface carrier, Surface child,
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
            RegionBounds(current, out var localMin, out var localMax);
            parentOrigin = origin;
            min = origin + localMin;
            max = origin + localMax;

            origin = min + current.AnchorInMeters; // this level's own origin, for the level below
        }

        return true;
    }

    /// <param name="quad">Caller-owned buffer of at least 4 entries — this runs per frame, so it doesn't allocate.</param>
    public static bool TryGetRegionQuad(Setup setup, Surface carrier, Surface child, Surface.OutputMapping carrierMapping,
                                       Vector2 canvasSize, Vector2[] quad)
    {
        if (quad.Length < 4
            || !TryGetSurfaceToOutput(carrier, carrierMapping, canvasSize, out var surfaceToOutput)
            || !TryGetRegionRect(setup, carrier, child, out var min, out var max, out _))
            return false;

        quad[0] = surfaceToOutput.TransformPoint(new Vector2(min.X, max.Y));
        quad[1] = surfaceToOutput.TransformPoint(max);
        quad[2] = surfaceToOutput.TransformPoint(new Vector2(max.X, min.Y));
        quad[3] = surfaceToOutput.TransformPoint(min);
        return true;
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
        MoveEdge(ref min, ref max, edge, surfacePos, MinSize);
        ApplyBounds(surface, min, max);

        if (!keepDimensions)
            return;

        // A stretch keeps the declared rectangle, so its space has to come back untouched too — only the
        // mapping onto the wall changed.
        surface.SizeInMeters = size;
        surface.Anchor = anchor;
    }

    // Ancestor chain scratch — the editor is single-threaded, and this runs inside the per-frame draw.
    private static readonly List<Surface> _chainScratch = [];
}
