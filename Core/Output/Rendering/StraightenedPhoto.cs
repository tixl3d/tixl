#nullable enable
using System;
using System.Numerics;
using T3.Core.DataTypes.Vector;
using Texture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Core.Output.Rendering;

/// <summary>
/// A traced surface's reference photo put upright: the photo warped so the traced quad becomes the surface's
/// rectangle. Shared by the editor's views and by operators that show the photo as content.
/// </summary>
public static class StraightenedPhoto
{
    /// <summary>
    /// The rectangle a traced quad straightens to, in photo px: the surface's own aspect at the size the quad
    /// was traced at, on the quad's centre. Only the aspect carries meaning here — how many photo pixels the
    /// wall is drawn across is arbitrary — so the area is what is kept, not one of the sides. That keeps the
    /// two axes symmetric: re-declaring the wall wider widens the frame instead of silently shortening it.
    /// </summary>
    public static void TargetBounds(Surface surface, out Vector2 min, out Vector2 max)
    {
        Bounds(surface.Trace!.Quad, out var quadMin, out var quadMax);
        var traced = new Vector2(MathF.Max(quadMax.X - quadMin.X, 1f), MathF.Max(quadMax.Y - quadMin.Y, 1f));
        var aspect = surface.SizeInMeters.X / MathF.Max(surface.SizeInMeters.Y, 0.0001f);
        var area = traced.X * traced.Y;
        var height = MathF.Sqrt(area / MathF.Max(aspect, 0.0001f));
        var width = MathF.Max(height * aspect, 1f);
        var centre = (quadMin + quadMax) * 0.5f;
        min = centre - new Vector2(width, height) * 0.5f;
        max = centre + new Vector2(width, height) * 0.5f;
    }

    /// <summary>
    /// Warps the photo so the traced <paramref name="quad"/> rectifies to the target rectangle, interpolated by
    /// <paramref name="t"/> (0 = the photo as taken). Out: the warped texture, the extent it covers and the
    /// surface's extent inside it, all in photo pixels.
    /// </summary>
    /// <param name="window">The part of the surface to render, as fractions of its rectangle (xMin, yMin, xMax,
    /// yMax, Y down); the whole surface is (0, 0, 1, 1). A region passes its own place on its traced ancestor.</param>
    /// <param name="surroundOfRegion">Photo kept around the window, as a share of its size, so marks near the edge
    /// still show what surrounds them.</param>
    /// <param name="interpolatedQuad">Receives the traced quad as moved by the warp (4 corners).</param>
    /// <param name="warpedPhotoCorners">Receives where the photo's own corners land (4 corners).</param>
    public static bool TryRender(Int2 imageSize, ReadOnlySpan<Vector2> quad, Vector2 targetMin, Vector2 targetMax, Texture2D texture,
                                 float t, Guid targetKey, float maxDimension, float surroundOfRegion, Vector4 window,
                                 Span<Vector2> interpolatedQuad, Span<Vector2> warpedPhotoCorners,
                                 out Texture2D? warped, out Vector2 bboxMin, out Vector2 bboxMax, out Vector2 regionMin, out Vector2 regionMax)
    {
        warped = null;
        bboxMin = bboxMax = regionMin = regionMax = Vector2.Zero;
        var w = Math.Max(1, imageSize.Width);
        var h = Math.Max(1, imageSize.Height);
        Span<Vector2> targetRect = stackalloc Vector2[4];
        SurfaceGeometry.WriteRectCorners(targetMin, targetMax, targetRect, yUp: false);

        for (var i = 0; i < 4; i++)
            interpolatedQuad[i] = Vector2.Lerp(quad[i], targetRect[i], t);

        if (!Homography.TryComputeQuadToQuad(quad, interpolatedQuad, out var homography))
            return false;

        warpedPhotoCorners[0] = homography.TransformPoint(new Vector2(0, 0));
        warpedPhotoCorners[1] = homography.TransformPoint(new Vector2(w, 0));
        warpedPhotoCorners[2] = homography.TransformPoint(new Vector2(w, h));
        warpedPhotoCorners[3] = homography.TransformPoint(new Vector2(0, h));

        Bounds(interpolatedQuad, out var surfaceMin, out var surfaceMax);
        var surfaceSize = surfaceMax - surfaceMin;
        regionMin = surfaceMin + surfaceSize * new Vector2(window.X, window.Y);
        regionMax = surfaceMin + surfaceSize * new Vector2(window.Z, window.W);

        // Extent = the rectified window plus a margin of surround, so it isn't clipped to the photo rect.
        // Bounding to the window — not the whole warped photo — is essential: a steep rectification sends the
        // far photo corners toward infinity, so sizing to the full warp would blow the target up and leave the
        // surface sub-pixel (it vanishes). The runaway surround is simply cropped to this window.
        var surround = (regionMax - regionMin) * surroundOfRegion;
        bboxMin = regionMin - surround;
        bboxMax = regionMax + surround;

        var bboxSize = bboxMax - bboxMin;
        var maxDim = Math.Max(bboxSize.X, bboxSize.Y);
        var renderScale = maxDim > maxDimension ? maxDimension / maxDim : 1f;
        var rtSize = new Int2(Math.Max(1, (int)(bboxSize.X * renderScale)), Math.Max(1, (int)(bboxSize.Y * renderScale)));

        for (var i = 0; i < 4; i++)
            _warpDestination[i] = (warpedPhotoCorners[i] - bboxMin) * renderScale;

        warped = OutputCompositor.RenderWarpedTexture(texture, _warpDestination, rtSize, targetKey);
        return warped is { IsDisposed: false };
    }

    /// <summary>The surface itself or its nearest ancestor that was traced on a photo; null if none was.</summary>
    public static Surface? FindTraceCarrier(Setup setup, Guid surfaceId)
    {
        var surface = setup.FindSurface(surfaceId);
        for (var guard = 0; surface != null && guard < 16; guard++)
        {
            if (surface.Trace != null)
                return surface;

            if (surface.ParentId == Guid.Empty)
                break;

            var parentId = surface.ParentId;
            surface = setup.FindSurface(parentId);
        }

        return null;
    }

    /// <summary>
    /// Where <paramref name="surface"/> sits on its traced ancestor's rectangle, as the window
    /// <see cref="TryRender"/> takes: (0, 0, 1, 1) for the traced surface itself.
    /// </summary>
    public static bool TryGetWindow(Setup setup, Surface traced, Surface surface, out Vector4 window)
    {
        window = new Vector4(0, 0, 1, 1);
        if (traced == surface)
            return true;

        if (!SurfaceGeometry.TryGetRegionRect(setup, traced, surface, out var regionMin, out var regionMax, out _))
            return false;

        SurfaceGeometry.LocalBounds(traced, out var tracedMin, out var tracedMax);
        var tracedSize = Vector2.Max(tracedMax - tracedMin, new Vector2(0.0001f));
        var min = (regionMin - tracedMin) / tracedSize;
        var max = (regionMax - tracedMin) / tracedSize;

        // Metres run up, the window runs down from the top.
        window = new Vector4(min.X, 1 - max.Y, max.X, 1 - min.Y);
        return true;
    }

    private static void Bounds(ReadOnlySpan<Vector2> points, out Vector2 min, out Vector2 max)
    {
        min = points[0];
        max = points[0];
        for (var i = 1; i < points.Length; i++)
        {
            min = Vector2.Min(min, points[i]);
            max = Vector2.Max(max, points[i]);
        }
    }

    /** Scratch for the warp's destination corners; the warp consumes it before the next call. */
    private static readonly Vector2[] _warpDestination = new Vector2[4];
}
