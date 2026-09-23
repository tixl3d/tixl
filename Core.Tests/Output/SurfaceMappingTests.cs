using System.Numerics;
using T3.Core.Output;
using Xunit;

namespace Core.Tests.Output;

public class SurfaceMappingTests
{
    [Fact]
    public void Fill_SurvivesAResize()
    {
        var surface = MappedSurface(MappingModes.Fill);

        SurfaceGeometry.ResizeAnchored(surface, new Vector2(4, 1));

        Assert.Equal(new Vector2(4, 1), surface.SizeInMeters);
        AssertQuad(surface, new Vector2(0, 0), new Vector2(1, 1));
    }

    [Fact]
    public void CornerPin_FollowsAReshapeOnTheWall()
    {
        var surface = PinnedSurface();

        // Twice as wide, same height, anchored at the bottom-left corner of the old rectangle.
        SurfaceGeometry.ApplyBounds(surface, Vector2.Zero, new Vector2(4, 1));

        AssertQuad(surface, new Vector2(0.2f, 0.2f), new Vector2(1.4f, 0.8f));
    }

    [Fact]
    public void CornerPin_StaysPutWhenTheSizeIsOnlyDeclared()
    {
        var surface = PinnedSurface();

        SurfaceGeometry.ApplyBounds(surface, Vector2.Zero, new Vector2(4, 1), RectangleEdits.Declared);

        Assert.Equal(new Vector2(4, 1), surface.SizeInMeters);
        AssertQuad(surface, new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.8f));
    }

    [Fact]
    public void Crop_KeepsMarksOnTheirPhotoFeature()
    {
        // The traced quad has perspective, so a proportional crop of it would not do.
        var surface = TracedSurface();
        var mark = MarkAt(surface, new Vector2(1.4f, 0.3f));
        Assert.True(TryMapToPhoto(surface, mark.P1, out var before));

        // Crop the bottom edge upward — the wall turns out to start higher than it was traced.
        SurfaceGeometry.ApplyBounds(surface, new Vector2(0, 0.2f), new Vector2(2, 1));

        Assert.True(TryMapToPhoto(surface, mark.P1, out var after));
        AssertPoint(before, after);
    }

    [Fact]
    public void Straighten_KeepsMarksOnTheirPhotoFeature()
    {
        var surface = TracedSurface();
        var mark = MarkAt(surface, new Vector2(1.4f, 0.3f));
        Assert.True(TryMapToPhoto(surface, mark.P1, out var before));

        // A straighten re-solves a quad, so the surface's space moves relative to the wall.
        Span<Vector2> rect = stackalloc Vector2[4];
        SurfaceGeometry.WriteLocalRect(surface, rect);
        SurfaceGeometry.CarrySpaceChange(surface, rect, SpaceCorrection(surface), solvedMapping: surface.OutputMappings[0]);

        Assert.True(TryMapToPhoto(surface, mark.P1, out var after));
        AssertPoint(before, after);
    }

    [Fact]
    public void TraceRefine_KeepsTheProjectorsAimedAtTheSameWall()
    {
        var surface = TracedSurface();
        var mapping = surface.OutputMappings[0];
        mapping.Quad = [new Vector2(0.15f, 0.1f), new Vector2(0.9f, 0.2f), new Vector2(0.85f, 0.7f), new Vector2(0.2f, 0.8f)];

        var mark = MarkAt(surface, new Vector2(1.4f, 0.3f));
        Assert.True(SurfaceGeometry.TryGetSurfaceToOutput(surface, mapping, Vector2.One, out var before));
        var beforePoint = before.TransformPoint(mark.P1);

        Span<Vector2> rect = stackalloc Vector2[4];
        SurfaceGeometry.WriteLocalRect(surface, rect);
        SurfaceGeometry.CarrySpaceChange(surface, rect, SpaceCorrection(surface), solvedTrace: true);

        // The mark moved with the space; the wall it points at did not.
        Assert.True(SurfaceGeometry.TryGetSurfaceToOutput(surface, mapping, Vector2.One, out var after));
        AssertPoint(beforePoint, after.TransformPoint(mark.P1));
    }

    [Fact]
    public void TraceRefine_KeepsMarksOnTheirPhotoFeature()
    {
        var surface = TracedSurface();
        var mark = MarkAt(surface, new Vector2(1.4f, 0.3f));
        Assert.True(TryMapToPhoto(surface, mark.P1, out var before));

        Span<Vector2> rect = stackalloc Vector2[4];
        SurfaceGeometry.WriteLocalRect(surface, rect);
        var trace = surface.Trace!;
        Assert.True(Homography.TryComputeQuadToQuad(trace.Quad, rect, out var photoToOldSurface));

        // Dragging the bottom edge up: the wall ends higher in the photo than it was traced.
        trace.Quad[2] -= new Vector2(0, 40);
        trace.Quad[3] -= new Vector2(0, 40);
        Assert.True(Homography.TryComputeQuadToQuad(rect, trace.Quad, out var newSurfaceToPhoto));

        SurfaceGeometry.CarrySpaceChange(surface, rect, Homography.Multiply(photoToOldSurface, newSurfaceToPhoto),
                                         solvedTrace: true);

        // The mark's coordinates changed, the feature it sits on did not.
        Assert.NotEqual(new Vector2(1.4f, 0.3f), mark.P1);
        Assert.True(TryMapToPhoto(surface, mark.P1, out var after));
        AssertPoint(before, after);
    }

    [Fact]
    public void ReferencePoint_BehavesLikeALine()
    {
        var surface = TracedSurface();
        var mapping = surface.OutputMappings[0];
        mapping.Quad = [new Vector2(0.15f, 0.1f), new Vector2(0.9f, 0.2f), new Vector2(0.85f, 0.7f), new Vector2(0.2f, 0.8f)];

        var line = MarkAt(surface, new Vector2(1.4f, 0.3f));
        var point = MarkAt(surface, new Vector2(1.4f, 0.3f));
        point.Kind = Annotation.Kinds.Point;
        point.P2 = point.P1; // a point is one spot; its second end mirrors the first

        Assert.True(TryMapToPhoto(surface, point.P1, out var photoBefore));
        Assert.True(SurfaceGeometry.TryGetSurfaceToOutput(surface, mapping, Vector2.One, out var toOutput));
        var aim = toOutput.TransformPoint(point.P1);

        // A crop, then a correction of the space — the two shapes every gesture here boils down to. Nothing is
        // named as solved, so the pin and the photo both come along and each has to keep the mark it holds.
        SurfaceGeometry.ApplyBounds(surface, new Vector2(0, 0.2f), new Vector2(2, 1));
        Span<Vector2> rect = stackalloc Vector2[4];
        SurfaceGeometry.WriteLocalRect(surface, rect);
        SurfaceGeometry.CarrySpaceChange(surface, rect, SpaceCorrection(surface));

        // Both ends of a point stay one spot, and it lands where the line's end does: same feature in the
        // photo, same place on the projector, so the mark it was aimed at still fits.
        AssertPoint(point.P1, point.P2);
        AssertPoint(line.P1, point.P1);
        Assert.True(TryMapToPhoto(surface, point.P1, out var photoAfter));
        AssertPoint(photoBefore, photoAfter);
        Assert.True(SurfaceGeometry.TryGetSurfaceToOutput(surface, mapping, Vector2.One, out var toOutputAfter));
        AssertPoint(aim, toOutputAfter.TransformPoint(point.P1));
    }

    /// <summary>A stand-in for what a straighten does to the surface's space: its rectangle read as a slightly
    /// keystoned one, so the correction is projective rather than a scale.</summary>
    private static Homography SpaceCorrection(Surface surface)
    {
        Span<Vector2> rect = stackalloc Vector2[4];
        SurfaceGeometry.WriteLocalRect(surface, rect);
        Span<Vector2> nudged = stackalloc Vector2[4];
        rect.CopyTo(nudged);
        nudged[1] += new Vector2(0.06f, 0.03f);
        nudged[2] -= new Vector2(0.02f, 0.05f);
        Assert.True(Homography.TryComputeQuadToQuad(nudged, rect, out var correction));
        return correction;
    }

    /// <summary>A measuring line whose first end marks a feature in the photo.</summary>
    private static Annotation MarkAt(Surface surface, Vector2 position)
    {
        var annotation = new Annotation { P1 = position, P2 = position + new Vector2(0.3f, 0) };
        surface.Annotations.Add(annotation);
        return annotation;
    }

    /// <summary>Where a point of the surface lands in the photo, through the traced quad.</summary>
    private static bool TryMapToPhoto(Surface surface, Vector2 surfacePoint, out Vector2 photoPoint)
    {
        photoPoint = Vector2.Zero;
        Span<Vector2> rect = stackalloc Vector2[4];
        SurfaceGeometry.WriteLocalRect(surface, rect);
        if (!Homography.TryComputeQuadToQuad(rect, surface.Trace!.Quad, out var surfaceToPhoto))
            return false;

        photoPoint = surfaceToPhoto.TransformPoint(surfacePoint);
        return true;
    }

    private static Surface TracedSurface()
    {
        var surface = MappedSurface(MappingModes.CornerPin);
        surface.Trace = new Surface.TraceBinding
                            {
                                Quad = [new Vector2(120, 80), new Vector2(980, 140), new Vector2(940, 620), new Vector2(160, 560)],
                            };
        return surface;
    }

    private static Surface PinnedSurface()
    {
        var surface = MappedSurface(MappingModes.CornerPin);
        surface.OutputMappings[0].Quad = [new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.2f), new Vector2(0.8f, 0.8f), new Vector2(0.2f, 0.8f)];
        return surface;
    }

    private static Surface MappedSurface(MappingModes mode)
    {
        var surface = new Surface { SizeInMeters = new Vector2(2, 1), Anchor = new Vector2(-1, -1) };
        var mapping = Surface.OutputMapping.CreateFilling(Guid.NewGuid());
        mapping.Mode = mode;
        surface.OutputMappings.Add(mapping);
        return surface;
    }

    private static void AssertQuad(Surface surface, Vector2 min, Vector2 max)
    {
        var quad = surface.OutputMappings[0].Quad;
        AssertPoint(min, quad[0]);
        AssertPoint(new Vector2(max.X, min.Y), quad[1]);
        AssertPoint(max, quad[2]);
        AssertPoint(new Vector2(min.X, max.Y), quad[3]);
    }

    private static void AssertPoint(Vector2 expected, Vector2 actual)
    {
        Assert.Equal(expected.X, actual.X, 2);
        Assert.Equal(expected.Y, actual.Y, 2);
    }
}
