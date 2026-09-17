#nullable enable
using T3.Core.Output;
using T3.Core.Output.Rendering;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// What a surface measures: its metres, the measuring lines and reference points among its annotations, and
/// the sizes seeded for new ones. Re-metering scales everything stored in surface space about the anchor and
/// leaves the projection and the trace alone — the wall is where it was, only the numbers describing it change.
/// </summary>
internal static class SurfaceMetrics
{
    /// <summary>
    /// Re-meters a surface: everything stored in its metres — the size, the measuring lines, the regions and
    /// their descendants — scales by <paramref name="scale"/> about the anchor.
    /// </summary>
    public static void ScaleSurfaceMetric(Setup setup, Surface surface, Vector2 scale)
    {
        surface.SizeInMeters *= scale;
        foreach (var annotation in surface.Annotations)
        {
            annotation.P1 *= scale;
            annotation.P2 *= scale;
        }

        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var child = setup.Surfaces[i];
            if (child.ParentId != surface.Id)
                continue;

            child.LocalPosition *= scale;
            ScaleSurfaceMetric(setup, child, scale);
        }
    }

    /// <summary>Declares a surface's real size — a re-metering (see <see cref="ScaleSurfaceMetric"/>), so lines and regions keep their place on the wall.</summary>
    public static void RemeterSurface(Setup setup, Surface surface, Vector2 newSize)
    {
        var target = new Vector2(MathF.Max(newSize.X, SurfaceGeometry.MinSize), MathF.Max(newSize.Y, SurfaceGeometry.MinSize));
        var old = surface.SizeInMeters;
        if (old.X <= 0.0001f || old.Y <= 0.0001f)
        {
            surface.SizeInMeters = target;
            return;
        }

        ScaleSurfaceMetric(setup, surface, target / old);
    }

    /// <summary>
    /// Reshapes the surface so its real-world proportions match the pixels of the slice it shows — the inverse
    /// of the slice's "Match target aspect", for when the wall is what should give. Keeps the width and solves
    /// the height, so it reads as a nudge rather than a jump.
    /// </summary>
    public static void MatchSurfaceToSliceAspect(Setup setup, Surface surface)
    {
        var slice = setup.FindSlice(surface.SliceId);
        if (slice == null || !TryGetSliceAspect(setup, slice, out var aspect))
            return;

        var width = MathF.Max(surface.SizeInMeters.X, SurfaceGeometry.MinSize);
        SetupUndo.RunUndoable("Resize surface", setup,
                              () => SurfaceGeometry.ResizeAnchored(surface, new Vector2(width, width / MathF.Max(aspect, 0.0001f))));
    }

    /// <summary>
    /// Metres for a traced quad: its bounding box's aspect, at the photo's pixels-per-metre averaged over the
    /// surfaces already traced on it — or, with none to learn from, a typical wall height.
    /// </summary>
    public static Vector2 EstimateTracedSize(Setup setup, ReferenceImage image, Vector2[] quad)
    {
        QuadBounds(quad, out var min, out var max);
        var width = MathF.Max(max.X - min.X, 1f);
        var height = MathF.Max(max.Y - min.Y, 1f);

        var pixelsPerMetre = 0f;
        var samples = 0;
        foreach (var other in setup.Surfaces)
        {
            if (other.Trace == null || other.Trace.ImageId != image.Id || other.Trace.Quad.Length < 4 || other.SizeInMeters.X <= 0.01f)
                continue;

            QuadBounds(other.Trace.Quad, out var otherMin, out var otherMax);
            pixelsPerMetre += MathF.Max(otherMax.X - otherMin.X, 1f) / other.SizeInMeters.X;
            samples++;
        }

        if (samples > 0)
        {
            pixelsPerMetre /= samples;
            return new Vector2(width / pixelsPerMetre, height / pixelsPerMetre);
        }

        const float typicalWallHeight = 2.5f;
        return new Vector2(typicalWallHeight * width / height, typicalWallHeight);
    }

    /// <summary>The x right of every placed surface card, plus a gap — where the next one stands on the floor.</summary>
    public static float NextFreeBoardX(Setup setup)
    {
        const float gap = 0.5f;
        var right = float.NegativeInfinity;
        foreach (var surface in setup.Surfaces)
        {
            if (surface.ParentId != Guid.Empty || surface.BoardPlacement == null)
                continue;

            right = MathF.Max(right, surface.BoardPlacement.Position.X - surface.AnchorInMeters.X + surface.SizeInMeters.X);
        }

        return float.IsNegativeInfinity(right) ? 0f : right + gap;
    }

    /// <summary>The measuring lines among a surface's annotations (points don't count toward a straighten).</summary>
    public static int CountLines(Surface surface)
    {
        var count = 0;
        foreach (var annotation in surface.Annotations)
        {
            if (!annotation.IsPoint)
                count++;
        }

        return count;
    }

    /// <summary>Whether any line carries a real length — what "Apply lengths" needs.</summary>
    public static bool HasMeasuredLine(Surface surface)
    {
        foreach (var annotation in surface.Annotations)
        {
            if (!annotation.IsPoint && annotation.LengthInMeters > 0)
                return true;
        }

        return false;
    }

    /// <summary>The reference points among a surface's annotations.</summary>
    public static int CountPoints(Surface surface)
    {
        var count = 0;
        foreach (var annotation in surface.Annotations)
        {
            if (annotation.IsPoint)
                count++;
        }

        return count;
    }

    /// <summary>Aspect of a slice's pixels — its uv extent against the source's resolution.</summary>
    private static bool TryGetSliceAspect(Setup setup, Slice slice, out float aspect)
    {
        aspect = 1f;
        var source = setup.FindSource(slice.SourceId);
        if (source == null || !OutputContentResolver.TryGetSourceContent(source.SymbolChildId, out _, out var content)
            || content is not { IsDisposed: false })
            return false;

        var width = content.Description.Width * MathF.Max(slice.UvRect.Z - slice.UvRect.X, 0.0001f);
        var height = content.Description.Height * MathF.Max(slice.UvRect.W - slice.UvRect.Y, 0.0001f);
        if (width <= 0 || height <= 0)
            return false;

        aspect = width / height;
        return true;
    }

    private static void QuadBounds(Vector2[] quad, out Vector2 min, out Vector2 max)
    {
        min = max = quad[0];
        for (var i = 1; i < quad.Length; i++)
        {
            min = Vector2.Min(min, quad[i]);
            max = Vector2.Max(max, quad[i]);
        }
    }
}
